using Autodesk.Revit.DB;

namespace PlumbingSystem.Revit;

/// <summary>
/// עוזר משותף ליצירה/שמירה-במטמון של <see cref="ReferenceIntersector"/>
/// מסונן-קירות לכל Level. נדרש בשני מקומות: <c>RevitModelReader</c>
/// (בדיקת חסימת-קיר בין אסלה לאלמנט רטוב, שלב 4) ו-
/// <c>CollectorPlacementService</c> (מציאת הקיר הפיזי הקרוב לקולטן
/// באמצעות ray-casting, שלב 6) - הופרד לכאן כדי לא לשכפל את לוגיקת
/// "מצא View3D + בנה/שמור ReferenceIntersector לכל Level" פעמיים.
/// </summary>
internal sealed class WallRayCasting
{
    private readonly Document _document;
    private readonly Dictionary<ElementId, ReferenceIntersector> _intersectorsByLevel = new();
    private View3D? _any3DView;

    /// <summary>יוצר מופע עבור המסמך הנתון.</summary>
    public WallRayCasting(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// מחזירה (ובונה, בפעם הראשונה לכל Level) <see cref="ReferenceIntersector"/>
    /// שמסונן לקירות (<see cref="Autodesk.Revit.DB.BuiltInCategory.OST_Walls"/>) על אותו Level.
    /// </summary>
    public ReferenceIntersector GetWallIntersector(ElementId levelId)
    {
        if (_intersectorsByLevel.TryGetValue(levelId, out ReferenceIntersector? cached))
        {
            return cached;
        }

        ElementFilter wallsOnLevelFilter = new LogicalAndFilter(
            new ElementCategoryFilter(BuiltInCategory.OST_Walls),
            new ElementLevelFilter(levelId));

        var intersector = new ReferenceIntersector(wallsOnLevelFilter, FindReferenceTarget.Element, GetAny3DView());
        _intersectorsByLevel[levelId] = intersector;
        return intersector;
    }

    /// <summary>
    /// בודקת האם קיר (<see cref="Autodesk.Revit.DB.BuiltInCategory.OST_Walls"/>,
    /// על אותו <paramref name="levelId"/>) חוצה את הקו הישר בין
    /// <paramref name="from"/> ל-<paramref name="to"/>, באמצעות קרן
    /// (<see cref="ReferenceIntersector"/>), ומחזירה את ה-ElementId של
    /// הקיר החוסם הראשון שנמצא (או null אם אין). **קוד גנרי** - לא
    /// תלוי בפריסת-הבניין הספציפית של הפרויקט הנוכחי, כדי שאותה בדיקה
    /// תעבוד באמינות זהה בכל פרויקט עתידי. הופרדה לכאן (במקום להישאר
    /// כפולה ב-<c>RevitModelReader</c> ו-<c>DrawPipesCommand</c>) כדי
    /// שלוגיקת "בדיקת-חסימה בין שתי נקודות" תהיה במקום אחד בלבד - ראו
    /// docs/step4.md (המקור המקורי, לבדיקת חסימה בין אסלה לאלמנט רטוב)
    /// ו-docs/step7.md (שימוש חוזר, לבדיקת חסימה בין אסלה לצינור).
    /// </summary>
    /// <param name="from">נקודת המוצא של הקו (בד"כ מיקום אסלה).</param>
    /// <param name="to">נקודת היעד של הקו (בד"כ מיקום אלמנט רטוב או קולטן).</param>
    /// <param name="levelId">ה-Level שאליו מסננים קירות מועמדים לבדיקה.</param>
    /// <param name="excludeWallId1">
    /// קיר להחריג מהתוצאה (לרוב ה-Host wall של אלמנט המוצא, אם יש
    /// כזה - למשל אסלת "wall_hung") - בלי זה, אלמנט התלוי-קיר "פוגע"
    /// מיידית בקיר המארח שלו עצמו בכל כיוון, ולא זו חסימה אמיתית.
    /// </param>
    /// <param name="excludeWallId2">אותו הסבר כמו <paramref name="excludeWallId1"/>, עבור אלמנט היעד.</param>
    public ElementId? FindBlockingWall(
        XYZ from,
        XYZ to,
        ElementId levelId,
        ElementId? excludeWallId1 = null,
        ElementId? excludeWallId2 = null) =>
        FindBlockingWallDetailed(from, to, levelId, excludeWallId1, excludeWallId2)?.WallId;

    /// <summary>
    /// פגיעת-קיר-חוסם מלאה - לא רק **איזה** קיר (כמו <see cref="FindBlockingWall"/>),
    /// אלא גם **איפה בדיוק** הקרן פגעה בו (<see cref="HitPoint"/>, נקודת-
    /// Revit גולמית) ו**באיזה מרחק** לאורך הקרן מ-<c>from</c>
    /// (<see cref="ProximityFeet"/>, יחידות-Revit פנימיות - feet). נוצר
    /// עבור דיווח-אבחון מפורט (שלב 7, "דורש פתרון ידני") שצריך להראות
    /// למהנדס חיצוני **איפה בדיוק** כל ניסיון-עקיפה נתקל בקיר, לא רק
    /// "כן/לא נחסם".
    /// </summary>
    public readonly record struct BlockingWallHit(ElementId WallId, XYZ HitPoint, double ProximityFeet);

    /// <summary>
    /// אותה לוגיקה בדיוק כמו <see cref="FindBlockingWall"/> (ray-casting
    /// יחיד, אותם כללי-החרגה/סבילות), אבל מחזירה גם את נקודת-הפגיעה
    /// המדויקת ואת המרחק לאורכה - ראו <see cref="BlockingWallHit"/>.
    /// <see cref="FindBlockingWall"/> הוא כעטיפה-דקה סביב המתודה הזו
    /// (לא שכפול-קוד נפרד) - כדי ששתי הבדיקות (בוליאנית/מפורטת) לעולם
    /// לא יסטו זו מזו בטעות.
    /// </summary>
    /// <param name="from">נקודת המוצא של הקו (בד"כ מיקום אסלה או תחילת מקטע-דרך).</param>
    /// <param name="to">נקודת היעד של הקו (בד"כ מיקום אלמנט רטוב/קולטן או סוף מקטע-דרך).</param>
    /// <param name="levelId">ה-Level שאליו מסננים קירות מועמדים לבדיקה.</param>
    /// <param name="excludeWallId1">קיר להחריג לגמרי מהתוצאה (לרוב ה-Host wall של אלמנט המוצא).</param>
    /// <param name="excludeWallId2">אותו הסבר כמו <paramref name="excludeWallId1"/>, עבור אלמנט היעד.</param>
    /// <param name="penetrableWallIds">
    /// קירות שמותר ל-route לחדור לתוכם בקרבת סוף הקרן - לפי הכלל ההנדסי
    /// שאושר (2026-09-02): צינור ביוב רשאי להיכנס לתוך עובי הקיר שמכיל
    /// את הקולטן, בדרך אל הקולטן. פגיעה על קיר כזה שנמצאת בתוך
    /// <paramref name="penetrableWithinEndDistanceFeet"/> מסוף הקרן
    /// (<paramref name="to"/>) **אינה** נחשבת חסימה. <c>null</c> (ברירת
    /// מחדל) = ההתנהגות הישנה בדיוק (למשל בדיקת החסימה של
    /// <c>RevitModelReader</c> בין אסלה לאלמנט רטוב).
    /// </param>
    /// <param name="penetrableWithinEndDistanceFeet">
    /// המרחק המרבי (יחידות-Revit פנימיות, feet) מסוף הקרן שבתוכו פגיעה
    /// על <paramref name="penetrableWallIds"/> מותרת. מעבר לו - פגיעה על
    /// אותם קירות **כן** חוסמת (route ש"מתגלגל" לאורך הקיר, לא נכנס אליו
    /// רק בסוף). לא רלוונטי כש-<paramref name="penetrableWallIds"/> הוא <c>null</c>.
    /// </param>
    public BlockingWallHit? FindBlockingWallDetailed(
        XYZ from,
        XYZ to,
        ElementId levelId,
        ElementId? excludeWallId1 = null,
        ElementId? excludeWallId2 = null,
        IReadOnlySet<ElementId>? penetrableWallIds = null,
        double penetrableWithinEndDistanceFeet = 0.0)
    {
        XYZ vector = to - from;
        double distance = vector.GetLength();
        if (distance < 1e-6)
        {
            return null;
        }

        XYZ direction = vector.Normalize();
        ReferenceIntersector intersector = GetWallIntersector(levelId);
        IList<ReferenceWithContext> hits = intersector.Find(from, direction);

        foreach (ReferenceWithContext hit in hits)
        {
            // רק פגיעה **ממש בין** שתי הנקודות (לא בנקודת המוצא/היעד
            // עצמה, ולא אחרי היעד) יכולה להיחשב "קיר חוסם".
            if (hit.Proximity <= 1e-6 || hit.Proximity >= distance - 1e-6)
            {
                continue;
            }

            ElementId hitWallId = hit.GetReference().ElementId;
            if (hitWallId == excludeWallId1 || hitWallId == excludeWallId2)
            {
                continue;
            }

            // הכלל ההנדסי שאושר (שלב 7, 2026-09-02): route בדרך אל הקולטן
            // רשאי לחדור לתוך קיר שמכיל את הקולטן - פגיעה על אחד מ-
            // penetrableWallIds שנמצאת בתוך penetrableWithinEndDistanceFeet
            // מסוף הקרן (= מהקולטן) אינה חסימה. קירות אחרים, ופגיעות רחוקות
            // יותר מסוף הקרן - כן חוסמות (ראו DrawPipesCommand.FindCollectorWallPenetration).
            if (penetrableWallIds is not null
                && penetrableWallIds.Contains(hitWallId)
                && (distance - hit.Proximity) <= penetrableWithinEndDistanceFeet)
            {
                continue;
            }

            XYZ hitPoint = from + (direction * hit.Proximity);
            return new BlockingWallHit(hitWallId, hitPoint, hit.Proximity);
        }

        return null;
    }

    /// <summary>
    /// מוצאת View3D כלשהו (לא Template) במסמך - נדרש בכפייה על ידי
    /// <see cref="ReferenceIntersector"/>. אם אין אף אחד, זורקת שגיאה
    /// ברורה במקום לדלג בשקט על בדיקת ה-ray-casting (שהייתה מחזירה
    /// תוצאות שגויות באופן שקט).
    /// </summary>
    private View3D GetAny3DView()
    {
        if (_any3DView is not null)
        {
            return _any3DView;
        }

        View3D? view = new FilteredElementCollector(_document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate);

        if (view is null)
        {
            throw new InvalidOperationException(
                "לא נמצא אף View3D (לא-Template) במסמך - נדרש לכל בדיקת ray-casting מול קירות.");
        }

        _any3DView = view;
        return view;
    }
}
