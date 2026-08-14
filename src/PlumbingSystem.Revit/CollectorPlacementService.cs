using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Revit;

/// <summary>
/// משלימה שני חלקים שדורשים RevitAPI אמיתי, ולכן לא יכולים לשבת
/// ב-PlumbingSystem.Core: (1) תיקון מיקום קולטן ממיקום-אסלה גולמי
/// (כפי ש-<see cref="Core.Domain.CollectorLocator"/> מחזיר) ל"צמוד
/// לקצה קיר" (סוגר את ה-TODO משלב 5), ו-(2) יצירת אלמנט אמיתי במודל
/// שמייצג את הקולטן. שתי הפעולות מקבלות/מחזירות <see cref="CollectorPoint"/>
/// (מ-Core) - הן "המתרגם" בין החלטת המיקום ההגיונית (Core) לבין הביצוע
/// בפועל בתוך Revit.
/// </summary>
public class CollectorPlacementService
{
    /// <summary>
    /// קוטר **תצוגה** (מטרים) של גליל הסימון - **לא** הקוטר ההנדסי
    /// האמיתי של הקולטן (שנשאר <see cref="PipeRouteCalculator.PipeDiameterMm"/>,
    /// 110 מ"מ, בדיוק כמו הצינורות - אין שום שינוי בנתון הזה, ולא
    /// משמש בשום מקום חוץ מהגיאומטריה כאן). מוגדל במפורש ל-0.32 (32
    /// ס"מ, טווח מבוקש 30-35 ס"מ) כדי שהסמן יהיה נראה-לעין בקנה-מידה
    /// 1:100 - אותו עיקרון בדיוק שכבר הנחה את קוביית-הסימון המקורית
    /// (שלב 6): זהו **סמן גנרי לתצוגה**, לא ייצוג-גודל-אמיתי-לסקייל
    /// של הקולטן הפיזי, ולכן מותר (ואף נדרש) שיהיה גדול מהקוטר ההנדסי
    /// כדי להיראות בפועל.
    /// </summary>
    private const double MarkerVisualDiameterMeters = 0.32;

    /// <summary>
    /// גובה גליל הסימון (מטרים). **הופחת מ-35 ס"מ ל-0.2 (20 ס"מ)** בבקשה
    /// מפורשת (טווח מבוקש: 15-20 ס"מ, "גובה סביר לתצוגה") - נבחר הערך
    /// **העליון** של הטווח המבוקש, במכוון, כדי לשמר חלק מהיתרון של
    /// התיקון הקודם: ה-35 ס"מ נבחרו במקור (ראו למטה) כדי שהקוביה לא
    /// תיחתך/תוסתר מאחורי אביזרים רטובים חופפים (אמבטיה/מקלחון) בגובה
    /// החיתוך הרגיל של Plan View. **המשמעות**: הורדת הגובה בחזרה לכיוון
    /// 15-20 ס"מ עלולה להחזיר את בעיית ה"מוסתר מאחורי אביזר" הזו באופן
    /// חלקי - זו לא סתירה שהוחמצה, זו החלטה מודעת לטובת מראה קרוב-יותר
    /// לתמונת-הייחוס, ששווה לוודא ויזואלית בהרצה הבאה (בלי Isolate).
    /// </summary>
    private const double MarkerHeightMeters = 0.2;

    /// <summary>
    /// היסט (מטרים) של בסיס הקוביה מעל ה-Z של הקולטן - "מרכז אותה מעט
    /// מעל גובה הרצפה הסטנדרטי" כדי שהגובה הנוסף (<see cref="MarkerHeightMeters"/>)
    /// יבלוט מעל אביזרים סמוכים, לא רק ימשיך מטה לתוך הרצפה.
    /// </summary>
    private const double MarkerBaseOffsetMeters = 0.1;

    /// <summary>מספר הכיוונים ש-ray-casting יורה בהם סביב האסלה (כל 360/8=45 מעלות).</summary>
    private const int RayDirectionCount = 8;

    /// <summary>
    /// שם ה-Material הטורקיז הייחודי של הקולטנים. **לעולם לא נעשה בו
    /// שימוש חוזר** אם הוא כבר קיים - <c>GetCollectorMaterialId</c> מוחקת
    /// כל Material קודם בשם הזה ויוצרת חדש במקומו, במקום להניח שהוא
    /// "שלנו" ולהשתמש בו (זו בדיוק הדרך שבה עלול לקרות שיתוף-בשוגג עם
    /// אלמנטים אחרים שכבר משתמשים במשהו באותו שם). גם <see cref="DeleteExistingCollectorMaterial"/>
    /// (נקראת מוקדם יותר, ב-<c>PlaceCollectorsCommand.Execute</c>) וגם
    /// <c>GetCollectorMaterialId</c> (נקראת בפועל, ממש לפני היצירה)
    /// מוחקות Material קודם בשם הזה - שכבת-הגנה כפולה, לא רק אחת, כך
    /// שיצירת Material טרי לחלוטין **לעולם לא נכשלת** עקב התנגשות-שם.
    /// </summary>
    private const string CollectorMaterialName = "PlumbingSystem Collector Turquoise";

    /// <summary>
    /// שם קודם ביותר (מקורי, אדום) של Material הקולטנים - נמחק במפורש
    /// ב-<see cref="DeleteExistingCollectorMaterial"/> כניקוי-הגירה,
    /// באותו עיקרון בדיוק כמו <c>ObsoleteNormalPipeMaterialName</c>
    /// ב-<c>DrawPipesCommand</c>.
    /// </summary>
    private const string ObsoleteCollectorMaterialName = "PlumbingSystem Collector Red";

    /// <summary>
    /// שם קודם **שני** (כחול-כהה-במקצת, האיחוד-הצבעוני הראשון עם
    /// הצינורות) - התברר ב-Isolate בפועל שהוא **קרוב מדי** לצבע-הצינור
    /// (<c>NormalPipeColor</c>, <c>(0,112,192)</c>) - קולטן וצינור-צמוד
    /// כמעט התמזגו לכתם אחד, גם כשהקולטן גדול (32 ס"מ). נמחק במפורש
    /// כניקוי-הגירה נוסף, באותו עיקרון בדיוק כמו <see cref="ObsoleteCollectorMaterialName"/>.
    /// </summary>
    private const string ObsoleteCollectorMaterialNameV2 = "PlumbingSystem Collector Blue";

    /// <summary>
    /// צבע טורקיז/תכלת-בהיר - **שונה בבירור** מהצינורות
    /// (<c>DrawPipesCommand.NormalPipeColor</c>, כחול-כהה-רגיל
    /// <c>(0,112,192)</c>), אבל עדיין **אותה משפחה** (שניהם ללא רכיב
    /// אדום, כחול/טורקיז): קולטן וצינור שייכים לאותה מערכת-ביוב אחת,
    /// אבל אמורים להיות **קלים-להבחנה** גם כשהם נוגעים זה-בזה (התגלה
    /// ב-Isolate בפועל: הגוון הכחול-כהה-במקצת הקודם היה קרוב מדי
    /// לצבע-הצינור והתמזג איתו ויזואלית לכתם אחד). ה-G הגבוה-משמעותית
    /// (180 מול 112 בצינור) הוא מה שהופך את הגוון לטורקיז ולא רק
    /// "כחול אחר" - הבדל-גוון (hue), לא רק הבדל-בהירות, נועד להבטיח
    /// ניגודיות מספקת גם מקרוב. מקור יחיד, משמש גם ל-Material
    /// (<see cref="GetCollectorMaterialId"/>) וגם ל-Override
    /// (<see cref="BuildCollectorOverrides"/>).
    /// </summary>
    private static readonly Color CollectorColor = new(0, 180, 200);

    private readonly Document _document;
    private readonly WallRayCasting _wallRayCasting;
    private ElementId? _solidFillPatternId;
    private ElementId? _collectorMaterialId;

    /// <summary>יוצר שירות עבור המסמך הנתון.</summary>
    public CollectorPlacementService(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _wallRayCasting = new WallRayCasting(document);
    }

    /// <summary>
    /// מרווח קבוע (מטרים), מעבר לחצי-עובי-הקיר, בין מרכז-הציור של
    /// קוביית הסימון לבין קו-האמצע של הקיר - **לצורך תצוגה בלבד**, ראו
    /// <see cref="ComputeVisualDrawCenter"/>. ערך לדוגמה קטן, לא נתון
    /// הנדסי מאושר - קל לכיוונון בעתיד.
    /// </summary>
    private const double VisualWallClearanceMarginMeters = 0.05;

    /// <summary>
    /// מתקנת את <see cref="CollectorPoint.Location"/> ממיקום-אסלה גולמי
    /// למיקום "צמוד לקצה קיר". מוצאת את הקירות הפיזיים שבאמת מקיפים את
    /// האסלה באמצעות ray-casting (<see cref="FindNearbyWallsByRayCasting"/>) -
    /// לא לפי גבולות ה-Room (ראו docs/step6.md לגבי באג שהוביל להחלטה
    /// הזו) - ומעבירה את התוצאה ל-<see cref="WallEdgeSnapper.SnapToNearestWallEdge"/>
    /// (Core, לוגיקה טהורה) שבוחרת את הקיר הקרוב ביותר ואת ה-Endpoint
    /// המתאים שלו.
    /// </summary>
    /// <param name="collector">קולטן עם מיקום גולמי (מיקום אסלה), כפי שמוחזר מ-<see cref="Core.Domain.CollectorLocator"/>.</param>
    /// <returns>
    /// <c>Snapped</c>: עותק של הקולטן (<c>with</c>) עם
    /// <see cref="CollectorPoint.Location"/> מתוקן - **הנתון ההנדסי**
    /// שמשמש verification/דוח/Room-membership, ללא שום שינוי נוסף. אם
    /// לא נמצא Room בנקודה המקורית, מוחזר הקולטן **ללא שינוי** - זה
    /// מצב לא-צפוי שראוי לבדוק ידנית בדוח, לא סיבה לזרוק שגיאה שעוצרת
    /// דירות אחרות.
    /// <c>VisualDrawCenter</c>: נקודה **נפרדת**, לצורך ציור הגיאומטריה
    /// בלבד (ראו <see cref="ComputeVisualDrawCenter"/>) - מוזזת מעט אל
    /// תוך החדר כדי שקוביית הסימון לא תיפול חלקית בתוך גוף-הקיר. אסור
    /// להשתמש בה לשום בדיקה הנדסית - ראו docs/step6.md לגבי הסיבה
    /// שהפרדה הזו נדרשה במפורש.
    /// </returns>
    /// <exception cref="System.InvalidOperationException">
    /// אם אף אחת מ-<see cref="RayDirectionCount"/> הקרניים לא פוגעת
    /// בקיר בטווח סביר, או אם <see cref="WallEdgeSnapper.SnapToNearestWallEdge"/>
    /// (הגנת-שפיות נוספת) לא מוצאת מועמד - שני המקרים לא אמורים לקרות,
    /// ועדיף להיכשל בבירור מאשר לבחור קיר רחוק בשקט.
    /// </exception>
    public (CollectorPoint Snapped, XYZ VisualDrawCenter) SnapToNearestWallEdge(CollectorPoint collector)
    {
        XYZ originalPoint = RevitUnitConversion.ToRevitPoint(collector.Location);

        Room? room = _document.GetRoomAtPoint(originalPoint);
        if (room is null)
        {
            return (collector, originalPoint);
        }

        List<WallEdgeSnapper.WallSegment> candidateWalls =
            FindNearbyWallsByRayCasting(originalPoint, room.LevelId, collector.Id);

        Point3D correctedLocation = WallEdgeSnapper.SnapToNearestWallEdge(
            collector.Location,
            candidateWalls,
            originatingFixtureId: collector.Id);

        XYZ correctedPoint = RevitUnitConversion.ToRevitPoint(correctedLocation);
        XYZ visualDrawCenter = ComputeVisualDrawCenter(correctedPoint, originalPoint, candidateWalls);

        return (collector with { Location = correctedLocation }, visualDrawCenter);
    }

    /// <summary>
    /// מחשבת נקודת-ציור **חזותית בלבד** לקוביית הסימון - שונה מ-
    /// <see cref="CollectorPoint.Location"/> ההנדסי (שנשאר בדיוק על
    /// ה-Endpoint של הקיר, ללא שום שינוי, לצורך verification/דוח - כך
    /// ביקשה המשתמשת במפורש). הבעיה שהתגלתה ויזואלית (Select by ID +
    /// Isolate + זום): מרכז מדויק בדיוק על קו-האמצע של הקיר מטביע חלק
    /// ניכר מקוביית הסימון (20x20 ס"מ) בתוך עובי-הקיר האטום עצמו (קיר
    /// טיפוסי עבה יותר מ-20 ס"מ) - נראה רק "פס" קטן בולט אל תוך החדר,
    /// לא קוביה שלמה.
    /// </summary>
    /// <remarks>
    /// הפתרון: היסט קטן, **מאונך** לכיוון הקיר שאליו בוצע ה-snap, אל
    /// תוך החדר (לא לתוך הקיר) - בגודל חצי-עובי-הקיר (<c>Wall.Width</c>)
    /// + <see cref="VisualWallClearanceMarginMeters"/>. כיוון "תוך
    /// החדר" (מבין שני הצדדים האפשריים של המאונך) נקבע לפי איזה מהם
    /// קרוב יותר ל-<paramref name="originalPoint"/> (מיקום האסלה, טרם
    /// snap) - האסלה עצמה תמיד בתוך החדר, אף פעם לא בתוך קיר, ולכן זה
    /// סימן אמין לכיוון הנכון בלי תלות בגיאומטריית ה-Room.
    /// </remarks>
    /// <param name="correctedPoint">המיקום ההנדסי אחרי snap (Endpoint של הקיר) - נקודת הבסיס להיסט.</param>
    /// <param name="originalPoint">המיקום המקורי (טרם snap) - משמש רק לקביעת כיוון ה"תוך החדר".</param>
    /// <param name="candidateWalls">אותה רשימת קירות-מועמדים שכבר שימשה את ה-snap - כדי לזהות איזה קיר בדיוק נבחר.</param>
    /// <returns>
    /// נקודה מוזזת לציור, או <paramref name="correctedPoint"/> ללא שינוי
    /// אם לא ניתן לזהות את הקיר התואם (מצב לא-צפוי) - נפילה בטוחה,
    /// כי זו הטבה חזותית בלבד, לא נתון קריטי.
    /// </returns>
    private XYZ ComputeVisualDrawCenter(
        XYZ correctedPoint,
        XYZ originalPoint,
        IReadOnlyList<WallEdgeSnapper.WallSegment> candidateWalls)
    {
        const double endpointMatchToleranceFeet = 1e-6;

        WallEdgeSnapper.WallSegment? matchedWall = null;

        foreach (WallEdgeSnapper.WallSegment segment in candidateWalls)
        {
            XYZ startPoint = RevitUnitConversion.ToRevitPoint(segment.Start);
            XYZ endPoint = RevitUnitConversion.ToRevitPoint(segment.End);

            if (startPoint.DistanceTo(correctedPoint) <= endpointMatchToleranceFeet
                || endPoint.DistanceTo(correctedPoint) <= endpointMatchToleranceFeet)
            {
                matchedWall = segment;
                break;
            }
        }

        if (matchedWall is null)
        {
            return correctedPoint;
        }

        WallEdgeSnapper.WallSegment wallSegment = matchedWall.Value;
        XYZ segmentStart = RevitUnitConversion.ToRevitPoint(wallSegment.Start);
        XYZ segmentEnd = RevitUnitConversion.ToRevitPoint(wallSegment.End);

        XYZ wallDirection = (segmentEnd - segmentStart).Normalize();
        XYZ perpendicular = new(-wallDirection.Y, wallDirection.X, 0.0);

        XYZ towardOriginal = originalPoint - correctedPoint;
        if (perpendicular.DotProduct(towardOriginal) < 0)
        {
            perpendicular = -perpendicular;
        }

        double wallWidthFeet = long.TryParse(wallSegment.Id, out long wallElementIdValue)
            && _document.GetElement(new ElementId(wallElementIdValue)) is Wall wall
                ? wall.Width
                : 0.0;

        double marginFeet = UnitUtils.ConvertToInternalUnits(VisualWallClearanceMarginMeters, UnitTypeId.Meters);
        double offsetFeet = (wallWidthFeet / 2.0) + marginFeet;

        return new XYZ(
            correctedPoint.X + (perpendicular.X * offsetFeet),
            correctedPoint.Y + (perpendicular.Y * offsetFeet),
            correctedPoint.Z);
    }

    /// <summary>
    /// יורה <see cref="RayDirectionCount"/> קרניים קצרות (כל 45 מעלות,
    /// אופקיות - Z קבוע) מ-<paramref name="origin"/>, מסונן לקטגוריית
    /// <see cref="Autodesk.Revit.DB.BuiltInCategory.OST_Walls"/> על <paramref name="levelId"/>,
    /// ואוספת את הקיר **הפיזי הראשון** שכל קרן פוגעת בו בפועל (בטווח
    /// <see cref="WallEdgeSnapper.MaxSearchRadiusMeters"/>) - זה נותן
    /// את הקירות שבאמת מקיפים/צמודים לאסלה מבחינה גיאומטרית, בניגוד
    /// ל"קרוב מספיק לפי מרחק" מתוך רשימת גבולות-Room שעלולה לכלול
    /// קירות לא-קשורים (ראו docs/step6.md).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// אם אף אחת מהקרניים לא פוגעת בקיר בטווח - במפורש, בלי לבחור
    /// קיר רחוק כברירת מחדל שקטה.
    /// </exception>
    private List<WallEdgeSnapper.WallSegment> FindNearbyWallsByRayCasting(
        XYZ origin,
        ElementId levelId,
        string originatingFixtureId)
    {
        ReferenceIntersector intersector = _wallRayCasting.GetWallIntersector(levelId);

        double maxRayDistanceFeet = UnitUtils.ConvertToInternalUnits(
            WallEdgeSnapper.MaxSearchRadiusMeters, UnitTypeId.Meters);

        var wallsById = new Dictionary<ElementId, Wall>();

        for (int i = 0; i < RayDirectionCount; i++)
        {
            double angle = i * (2 * Math.PI / RayDirectionCount);
            var direction = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);

            IList<ReferenceWithContext> hits = intersector.Find(origin, direction);

            ReferenceWithContext? closestHit = hits
                .Where(hit => hit.Proximity > 1e-6 && hit.Proximity <= maxRayDistanceFeet)
                .OrderBy(hit => hit.Proximity)
                .FirstOrDefault();

            if (closestHit is null)
            {
                continue;
            }

            ElementId wallId = closestHit.GetReference().ElementId;
            if (!wallsById.ContainsKey(wallId) && _document.GetElement(wallId) is Wall wall)
            {
                wallsById[wallId] = wall;
            }
        }

        var segments = new List<WallEdgeSnapper.WallSegment>();

        foreach (Wall wall in wallsById.Values)
        {
            if (wall.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line line)
            {
                // קירות מעוקלים (Arc/Spline) לא מטופלים בשלב הזה - ראו docs/step6.md.
                continue;
            }

            segments.Add(new WallEdgeSnapper.WallSegment(
                wall.Id.Value.ToString(),
                RevitUnitConversion.ToCorePoint(line.GetEndPoint(0)),
                RevitUnitConversion.ToCorePoint(line.GetEndPoint(1))));
        }

        if (segments.Count == 0)
        {
            throw new InvalidOperationException(
                $"אף אחת מ-{RayDirectionCount} הקרניים (כל 45 מעלות) סביב האסלה " +
                $"'{originatingFixtureId}' לא פגעה בקיר בטווח {WallEdgeSnapper.MaxSearchRadiusMeters:F1} " +
                "מ' - לא ניתן לקבוע מיקום קולטן צמוד-קיר.");
        }

        return segments;
    }

    /// <summary>
    /// סבילות (מטרים) לבדיקת "האם הקולטן באמת צמוד לקיר" - ערך לדוגמה
    /// שנקבע לפי בקשה מפורשת. מכיוון שההצמדה (<see cref="WallEdgeSnapper"/>)
    /// מזיזה את המיקום ממש ל-Endpoint של קיר, המרחק האמיתי אמור להיות
    /// כמעט 0 - הסבילות הזו היא שוליים לדיוק floating-point, לא קריטריון
    /// הנדסי (בניגוד ל-<see cref="Core.Domain.CollectorLocator.MaxDistanceMeters"/>).
    /// </summary>
    public const double WallProximityToleranceMeters = 0.1;

    /// <summary>
    /// סבילות (מטרים) לזיהוי "קיר שני קרוב" כעדות ל-**פינה אמיתית**
    /// (מפגש בין שני קירות) - לא רק קצה של קטע-קיר בודד שבמקרה נגמר שם
    /// (שיכול להיות אמצע-קיר-פיזי מבחינה הנדסית, אם ה-Room boundary
    /// מפצל קיר לקטעים). ערך לדוגמה בתוך הטווח שהוצע (0.3-0.5 מ') -
    /// קבוע יחיד לכיוונון, לא קריטריון מדויק שאושר הנדסית.
    /// </summary>
    public const double CornerSecondWallToleranceMeters = 0.4;

    /// <summary>
    /// תוצאת אימות מיקום קולטן - ראו <see cref="VerifyPlacement"/>.
    /// <see cref="OverallPass"/> הוא הדגל היחיד שצריך כדי לדעת "תקין/לא
    /// תקין" בלי לפרש נתונים גולמיים. <see cref="CornerStatus"/> הוא
    /// מידע נוסף, **לא** חלק מ-PASS/FAIL - ראו שם.
    /// </summary>
    public readonly record struct PlacementVerification(
        bool WallProximityPass,
        double DistanceToNearestWallMeters,
        bool RoomMembershipPass,
        string ActualRoomNumber,
        double? SecondNearestWallDistanceMeters)
    {
        /// <summary>PASS רק אם שתי הבדיקות (קרבה לקיר, שיוך ל-Room הנכון) עברו.</summary>
        public bool OverallPass => WallProximityPass && RoomMembershipPass;

        /// <summary>
        /// <c>"CONFIRMED"</c> אם נמצא קיר שני בתוך <see cref="CornerSecondWallToleranceMeters"/>
        /// מהמיקום הסופי - עדות (לא הוכחה מוחלטת) שזו פינה אמיתית.
        /// <c>"UNKNOWN"</c> אחרת - **לא** FAIL: קיר בודד ארוך מספיק הוא
        /// מצב תקין לגמרי, זה רק סימן לבדוק ידנית אם רוצים לוודא.
        /// </summary>
        public string CornerStatus =>
            SecondNearestWallDistanceMeters is double d && d <= CornerSecondWallToleranceMeters
                ? "CONFIRMED"
                : "UNKNOWN";
    }

    /// <summary>
    /// מאמתת **באופן עצמאי** (לא סומכת על הפנימיות של
    /// <see cref="SnapToNearestWallEdge"/>) שהקולטן הסופי אכן: (1) קרוב
    /// בפועל לקיר כלשהו (בתוך <see cref="WallProximityToleranceMeters"/>) -
    /// נבדק ע"י חיפוש-מרחק גס (brute-force) על כל קירות ה-Level
    /// (<see cref="FindSortedWallDistancesMeters"/>), **לא** ע"י שימוש
    /// חוזר ב-ray-casting - זה מה שנבדק, לא הכלי שהפיק אותו; (2) שייך
    /// בפועל לדירה הנכונה - לא רק "יש שם Room כלשהו". נוצרה כי זיהוי
    /// ויזואלי בצילומי מסך לא נתן תשובה חד-משמעית.
    ///
    /// בנוסף, מחשבת (לא PASS/FAIL - ראו <see cref="PlacementVerification.CornerStatus"/>)
    /// אם יש **קיר שני** קרוב למיקום הסופי - עדות ל"פינה אמיתית" (מפגש
    /// שני קירות), לא רק קצה של קטע-קיר בודד שבמקרה נגמר שם (שיכול
    /// להיות אמצע-קיר-פיזי מבחינה הנדסית, אם ה-Room boundary מפצל את
    /// הקיר לקטעים). "הקיר השני" נגזר מאותו חיפוש-מרחק-ממוין (אינדקס 1) -
    /// לא בדיקה נפרדת.
    /// </summary>
    /// <param name="originalCollector">
    /// הקולטן במיקום ה**מקורי** (מיקום האסלה, לפני snap) - משמש **רק**
    /// לבדיקת שיוך ל-Room, לא לבדיקת קרבה-לקיר. חייב להיות המקור לבדיקת
    /// ה-Room, לא המיקום הסופי: המיקום הסופי צמוד ממש לקצה קיר (מרחק
    /// כמעט 0, כמו שרוצים), ולכן `GetRoomAtPoint` עליו נופל לפעמים
    /// מחוץ לפוליגון ה-Room (כישלון-שווא) גם כשהקולטן הנדסית שייך לחדר -
    /// בדיוק אותה תופעה שכבר תוקנה עבור "Location context" בדוח; בדיקת
    /// Room-membership סבלה מאותו באג ותוקנה כאן באותו אופן.
    /// </param>
    /// <param name="finalCollector">הקולטן במיקום הסופי (אחרי snap) - משמש רק לבדיקת קרבה-לקיר.</param>
    /// <param name="expectedApartmentId">ה-Room.Number הצפוי (=Apartment.Id).</param>
    /// <param name="levelId">ה-Level לחיפוש קירות - מחושב מראש על ידי הקורא מהמיקום המקורי.</param>
    public PlacementVerification VerifyPlacement(
        CollectorPoint originalCollector,
        CollectorPoint finalCollector,
        string expectedApartmentId,
        ElementId levelId)
    {
        XYZ originalPoint = RevitUnitConversion.ToRevitPoint(originalCollector.Location);
        XYZ finalPoint = RevitUnitConversion.ToRevitPoint(finalCollector.Location);

        Room? room = _document.GetRoomAtPoint(originalPoint);
        bool roomMembershipPass = room is not null && room.Number == expectedApartmentId;
        string actualRoomNumber = room?.Number ?? "(no Room found)";

        List<double> sortedWallDistances = FindSortedWallDistancesMeters(finalPoint, levelId);

        double distanceToNearestWall = sortedWallDistances.Count > 0 ? sortedWallDistances[0] : double.MaxValue;
        bool wallProximityPass = distanceToNearestWall <= WallProximityToleranceMeters;

        // "קיר שני" = השני-הכי-קרוב מתוך כל הקירות, לא קיר ספציפי שמזוהה
        // בנפרד מהראשון - הראשון (אינדקס 0) הוא כמעט תמיד זה שה-snap
        // כבר הצמיד אליו (מרחק ~0), אז "השני" (אינדקס 1) הוא בדיוק
        // המועמד ל"קיר נוסף שנפגש כאן" אם הוא קרוב מספיק.
        double? secondNearestWallDistance = sortedWallDistances.Count > 1 ? sortedWallDistances[1] : null;

        return new PlacementVerification(
            wallProximityPass,
            distanceToNearestWall,
            roomMembershipPass,
            actualRoomNumber,
            secondNearestWallDistance);
    }

    /// <summary>
    /// חיפוש-מרחק גס (brute-force): עובר על **כל** הקירות ב-Level הנתון,
    /// מחשב את המרחק (מטרים) מ-<paramref name="point"/> לנקודת ההיטל
    /// (קבועה לגבולות הקטע) על כל אחד, וממיין מהקרוב לרחוק. במכוון
    /// **לא** משתמש ב-ray-casting - זו בדיקה עצמאית, לא חזרה על אותה
    /// שיטה שכבר נבדקת. משמש גם לבדיקת קרבה-לקיר (אינדקס 0) וגם
    /// לזיהוי-פינה (אינדקס 1 - "קיר שני") מאותו חיפוש יחיד.
    /// </summary>
    private List<double> FindSortedWallDistancesMeters(XYZ point, ElementId levelId)
    {
        ElementFilter wallsOnLevelFilter = new LogicalAndFilter(
            new ElementCategoryFilter(BuiltInCategory.OST_Walls),
            new ElementLevelFilter(levelId));

        List<Wall> wallsOnLevel = new FilteredElementCollector(_document)
            .WherePasses(wallsOnLevelFilter)
            .WhereElementIsNotElementType()
            .OfType<Wall>()
            .ToList();

        var distancesMeters = new List<double>();

        foreach (Wall wall in wallsOnLevel)
        {
            if (wall.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line line)
            {
                continue;
            }

            IntersectionResult? projection = line.Project(point);
            if (projection is null)
            {
                continue;
            }

            double distanceFeet = projection.XYZPoint.DistanceTo(point);
            distancesMeters.Add(UnitUtils.ConvertFromInternalUnits(distanceFeet, UnitTypeId.Meters));
        }

        distancesMeters.Sort();
        return distancesMeters;
    }

    /// <summary>
    /// מוחקת מהמודל את כל אלמנטי הקולטן שנוצרו בהרצות קודמות (זיהוי
    /// לפי קטגוריית Generic Model + Comments/Mark שמתחיל ב-
    /// <see cref="CollectorLocator.CollectorIdPrefix"/> - אותה קידומת
    /// שבה כל קולטן חדש מתויג ב-<see cref="CreateCollectorElement"/>).
    /// לא נוגעת בשום Generic Model אחר שקיים במודל משיקולים אחרים -
    /// רק באלמנטים שהתוסף הזה עצמו יצר. חייבת לרוץ בתוך אותו Transaction
    /// כמו היצירה של הקולטנים החדשים (לא Transaction נפרד לפני) - כדי
    /// שאם היצירה נכשלת באמצע, ה-Rollback המלא יבטל גם את המחיקה, ולא
    /// יישאר המודל בלי קולטנים בכלל.
    /// </summary>
    /// <returns>כמה אלמנטים נמחקו - לדיווח בדוח, כדי שהניקוי יהיה גלוי ולא רק "בהנחה".</returns>
    public int DeleteExistingCollectors()
    {
        List<ElementId> existingCollectorIds = new FilteredElementCollector(_document)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .Where(IsExistingCollector)
            .Select(element => element.Id)
            .ToList();

        if (existingCollectorIds.Count > 0)
        {
            _document.Delete(existingCollectorIds);
        }

        return existingCollectorIds.Count;
    }

    /// <summary>
    /// קידומת קבועה של <see cref="Autodesk.Revit.DB.Element.Name"/> לכל
    /// קולטן (ראו <see cref="CreateCollectorElement"/>) - אות-זיהוי
    /// שלישי, עצמאי מ-Comments/Mark, לזיהוי-לצורך-מחיקה
    /// (<see cref="IsExistingCollector"/>). <c>Element.Name</c> היא
    /// תכונת-ליבה בסיסית של Revit (לא פרמטר מותאם), ולכן שכבת-הגנה
    /// נוספת שלא תלויה באותו מנגנון בדיוק כמו Comments/Mark - אם יש
    /// ספק כלשהו לגבי אמינות זיהוי אחד, עדיף לבדוק כמה סימנים בלתי-
    /// תלויים מאשר לסמוך על אחד בלבד.
    /// </summary>
    private const string CollectorElementNamePrefix = "PlumbingSystem Collector ";

    /// <summary>
    /// אלמנט נחשב "קולטן שיצרנו" אם ה-Comments **או** ה-Mark **או**
    /// ה-<see cref="Autodesk.Revit.DB.Element.Name"/> שלו מתחילים
    /// בקידומת המתאימה (ראו <see cref="CreateCollectorElement"/> -
    /// שלושתם מוצבים על כל קולטן חדש). שלושה אותות עצמאיים, לא רק אחד -
    /// כדי שזיהוי-לצורך-מחיקה יהיה עמיד גם אם אחד מהם לא ייקרא נכון
    /// מסיבה כלשהי; ראו docs/step6.md לגבי המקרה שבו "Deleted 0" נראה
    /// כמו כשל-זיהוי אבל בפועל היה תוצאה נכונה (קובץ לא נשמר בין
    /// הרצות, אז לא היה מה למצוא).
    /// </summary>
    private static bool IsExistingCollector(Element element)
    {
        string? comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        string? mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();

        return (comments?.StartsWith(CollectorLocator.CollectorIdPrefix, StringComparison.Ordinal) ?? false)
            || (mark?.StartsWith(CollectorLocator.CollectorIdPrefix, StringComparison.Ordinal) ?? false)
            || element.Name.StartsWith(CollectorElementNamePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// יוצרת אלמנט <see cref="DirectShape"/> (גליל, קטגוריית Generic
    /// Model - **שונה מקוביה לגליל** בבקשה מפורשת, ראו <see cref="MarkerVisualDiameterMeters"/>/
    /// <see cref="MarkerHeightMeters"/>) במיקום הקולטן, מתויגת ב-Comments
    /// וב-Mark עם <see cref="CollectorPoint.Id"/>. הצביעה (<see cref="CollectorColor"/>)
    /// מוחלת **בשתי שכבות עצמאיות**, כי כל אחת מהן יכולה להיחסם בנפרד:
    /// (1) <c>Material</c> צרוב ישירות בתוך הגיאומטריה (<c>SolidOptions</c>) -
    /// תכונה מהותית של האלמנט עצמו, נראית בכל View בתצוגת Shaded/Realistic,
    /// ולא נחסמת ע"י View Template (בניגוד ל-Override); (2)
    /// <see cref="Autodesk.Revit.DB.OverrideGraphicSettings"/> על
    /// ה-<paramref name="view"/> הנתון - צובעת גם קווים ב-Hidden Line,
    /// אבל היא **per-view** (רק ה-view שהיה פעיל בזמן היצירה) ותיתכן
    /// חסומה אם ל-view הזה מוצמד View Template שנועל V/G Overrides.
    /// חייבת לרוץ בתוך Transaction פעיל (זו פעולת כתיבה למודל).
    /// </summary>
    /// <remarks>
    /// **גיאומטריית הגליל**: פרופיל-מעגל מלא נבנה משני קשתות-חצי-מעגל
    /// (<c>Arc.Create</c>, 0-180° ו-180-360°) מוצמדות ל-<see cref="CurveLoop"/>
    /// אחד - הדרך הרגילה ב-Revit API לבנות מעגל סגור לצורך אקסטרוזיה
    /// (Revit לא תומך בעקומת-אליפסה/מעגל בודדת וסגורה בתוך <c>CurveLoop</c>,
    /// רק קשתות חלקיות שיחד סוגרות מעגל שלם).
    /// </remarks>
    /// <param name="collector">הקולטן (במיקום הסופי, אחרי snap) - משמש **רק** לתיוג (Id ב-Comments/Mark/Name), לא לגיאומטריה.</param>
    /// <param name="visualDrawCenter">
    /// מרכז הגיאומטריה בפועל - מגיע מ-<see cref="SnapToNearestWallEdge"/>
    /// (<c>VisualDrawCenter</c>), **לא** מ-<c>collector.Location</c>.
    /// הפרדה מכוונת: <c>collector.Location</c> הוא הנתון ההנדסי
    /// (verification/דוח), ואסור לגעת בו לצורך תצוגה - ראו docs/step6.md.
    /// </param>
    /// <param name="view">ה-View שעליו מוחל ה-Override הפר-view (שכבה 2 - ראו למעלה).</param>
    /// <remarks>
    /// נבחר <c>DirectShape</c> ולא <c>FamilyInstance</c> של family קיים,
    /// כי אין family ייעודי לקולטן בפרויקט הזה (ואין סיבה להניח שיהיה
    /// כזה בפרויקטים עתידיים - ראו גם ההחלטה על <c>FixtureFamilyNames</c>
    /// בשלב 4). <c>DirectShape</c> היא הדרך הרשמית ב-Revit API ליצור
    /// גיאומטריה + אלמנט מזוהה-קטגוריה בלי שום תלות בטעינת family מראש -
    /// היא עובדת בכל מסמך Revit, בניגוד ל-<c>FamilyInstance</c> שדורש
    /// <c>FamilySymbol</c> קיים וטעון (ואיזה family לטעון היה עוד ניחוש).
    /// </remarks>
    public DirectShape CreateCollectorElement(CollectorPoint collector, XYZ visualDrawCenter, View view)
    {
        double radius = UnitUtils.ConvertToInternalUnits(MarkerVisualDiameterMeters, UnitTypeId.Meters) / 2.0;
        double height = UnitUtils.ConvertToInternalUnits(MarkerHeightMeters, UnitTypeId.Meters);
        double baseOffset = UnitUtils.ConvertToInternalUnits(MarkerBaseOffsetMeters, UnitTypeId.Meters);

        var baseCenter = new XYZ(visualDrawCenter.X, visualDrawCenter.Y, visualDrawCenter.Z + baseOffset);

        Curve halfCircle1 = Arc.Create(baseCenter, radius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
        Curve halfCircle2 = Arc.Create(baseCenter, radius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

        var profile = new CurveLoop();
        profile.Append(halfCircle1);
        profile.Append(halfCircle2);

        var solidOptions = new SolidOptions(GetCollectorMaterialId(), ElementId.InvalidElementId);
        Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
            new List<CurveLoop> { profile },
            XYZ.BasisZ,
            height,
            solidOptions);

        DirectShape directShape = DirectShape.CreateElement(_document, new ElementId(BuiltInCategory.OST_GenericModel));
        directShape.SetShape(new List<GeometryObject> { solid });
        directShape.Name = $"{CollectorElementNamePrefix}{collector.Id}";

        directShape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(collector.Id);
        directShape.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(collector.Id);

        view.SetElementOverrides(directShape.Id, BuildCollectorOverrides());

        return directShape;
    }

    /// <summary>
    /// מוחקת (אם קיים) Material בשם <see cref="CollectorMaterialName"/>
    /// מהרצה קודמת - **לפני** שנוצרים קולטנים חדשים, באותו עקרון בדיוק
    /// כמו <see cref="DeleteExistingCollectors"/> לגבי הגיאומטריה. חייבת
    /// לרוץ בתוך אותו Transaction כמו היצירה (Rollback מלא אם היצירה
    /// נכשלת באמצע). המטרה: כל הרצה מבטיחה Material **טרי וייחודי**,
    /// לא שימוש-חוזר במשהו שנוצר בהרצה קודמת (ולכן, תיאורטית, יכול
    /// להיות כבר משויך-בטעות לאלמנטים אחרים).
    /// </summary>
    /// <returns><c>true</c> אם נמצא ונמחק Material קודם בשם הנוכחי, אחרת <c>false</c>.</returns>
    public bool DeleteExistingCollectorMaterial()
    {
        // ניקוי-הגירה חד-פעמי של שני השמות/הצבעים הישנים (אדום, ואז
        // כחול-כהה-במקצת) - לא חלק מהערך המוחזר, כי הדוח מדווח רק על
        // מצב ה-Material הנוכחי-בשימוש.
        DeleteMaterialByName(ObsoleteCollectorMaterialName);
        DeleteMaterialByName(ObsoleteCollectorMaterialNameV2);

        return DeleteMaterialByName(CollectorMaterialName);
    }

    /// <summary>
    /// מוחקת (אם קיים) <c>Material</c> בשם הנתון. הופרדה מתוך
    /// <see cref="DeleteExistingCollectorMaterial"/> כדי ש-
    /// <see cref="GetCollectorMaterialId"/> תוכל לקרוא לאותה לוגיקה
    /// **שוב, ממש לפני היצירה** - שכבת-הגנה כפולה: גם אם
    /// <see cref="DeleteExistingCollectorMaterial"/> כבר רץ קודם באותה
    /// הרצה (ב-<c>PlaceCollectorsCommand.Execute</c>) ולא מצא כלום,
    /// הבדיקה החוזרת הזו מבטיחה שיצירת ה-Material **לעולם לא תיכשל**
    /// עקב התנגשות-שם - "תמיד למחוק וליצור מחדש" עדיף על "לזרוק שגיאה
    /// ולבטל את כל ה-Transaction" (ראו docs/step6.md).
    /// </summary>
    private bool DeleteMaterialByName(string name)
    {
        Material? existing = new FilteredElementCollector(_document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(m => m.Name == name);

        if (existing is null)
        {
            return false;
        }

        _document.Delete(existing.Id);
        return true;
    }

    /// <summary>
    /// יוצרת (ושומרת במטמון, לשימוש חוזר **בתוך אותה הרצה בלבד** - בין
    /// 5 הקולטנים של אותה הרצה, לא בין הרצות) <c>Material</c> חדש בצבע
    /// <see cref="CollectorColor"/> בשם <see cref="CollectorMaterialName"/>, נצרב לתוך הגיאומטריה של
    /// כל קולטן. **לא** משתמשת חוזרת ב-Material קיים בשם הזה - אם נמצא
    /// כזה, מוחקת אותו (<see cref="DeleteMaterialByName"/>) ויוצרת
    /// חדש במקומו, במקום להניח שהוא "שלנו" ולשתף אותו בשוגג עם אלמנטים
    /// אחרים שאולי כבר משתמשים בו.
    /// </summary>
    /// <remarks>
    /// גרסה קודמת של המתודה הזו **זרקה** <c>InvalidOperationException</c>
    /// אם נמצא Material קיים בשם הזה בשלב הזה, במקום למחוק אותו - מתוך
    /// הנחה ש-<see cref="DeleteExistingCollectorMaterial"/> כבר היה
    /// אמור לרוץ קודם באותה הרצה ולנקות אותו. זה בוטל: זריקת שגיאה כאן
    /// מבטלת (Rollback) את **כל** ה-Transaction - כולל קולטנים שכבר
    /// נוצרו בדירות קודמות באותה הרצה - במקום פשוט לנקות ולהמשיך. אין
    /// שום סיבה טובה לא-ליצור Material טרי כאן: המחיקה-ואז-יצירה
    /// אידמפוטנטית לגמרי, ולא תלויה בכך ש-<see cref="DeleteExistingCollectorMaterial"/>
    /// כבר רץ/הצליח קודם.
    /// </remarks>
    private ElementId GetCollectorMaterialId()
    {
        if (_collectorMaterialId is not null)
        {
            return _collectorMaterialId;
        }

        DeleteMaterialByName(ObsoleteCollectorMaterialName);
        DeleteMaterialByName(ObsoleteCollectorMaterialNameV2);
        DeleteMaterialByName(CollectorMaterialName);

        ElementId materialId = Material.Create(_document, CollectorMaterialName);
        if (_document.GetElement(materialId) is Material material)
        {
            material.Color = CollectorColor;

            // בלי זה, Shaded View עלול להציג את ה-Render Appearance
            // (Asset) של ה-Material במקום את צבע ה-Shading שקבענו למעלה -
            // Material חדש שנוצר בלי Asset מפורש עלול לקבל ברירת-מחדל
            // חיוורת/שקופה-למחצה שנראית כמו "כתם ורוד" ומתמזגת עם
            // אלמנטים סמוכים, במקום הצבע המלא והברור שקבענו.
            material.UseRenderAppearanceForShading = false;
        }

        _collectorMaterialId = materialId;
        return materialId;
    }

    /// <summary>
    /// בונה <see cref="Autodesk.Revit.DB.OverrideGraphicSettings"/> בצבע
    /// <see cref="CollectorColor"/> (קווים + מילוי מלא, גם ב-Projection
    /// וגם ב-Cut) - כדי שהקולטן יבלוט בבירור בכל תצוגה (Plan חתוך או לא),
    /// בלי תלות בהגדרת Visibility/Graphics ידנית.
    /// </summary>
    private OverrideGraphicSettings BuildCollectorOverrides()
    {
        var ogs = new OverrideGraphicSettings();

        ogs.SetProjectionLineColor(CollectorColor);
        ogs.SetCutLineColor(CollectorColor);
        ogs.SetSurfaceForegroundPatternColor(CollectorColor);
        ogs.SetCutForegroundPatternColor(CollectorColor);

        ElementId? solidFillPatternId = GetSolidFillPatternId();
        if (solidFillPatternId is not null)
        {
            ogs.SetSurfaceForegroundPatternId(solidFillPatternId);
            ogs.SetSurfaceForegroundPatternVisible(true);
            ogs.SetCutForegroundPatternId(solidFillPatternId);
            ogs.SetCutForegroundPatternVisible(true);
        }

        return ogs;
    }

    /// <summary>מוצאת (ושומרת במטמון) את ה-ElementId של תבנית "Solid fill" הראשונה שנמצאת במסמך.</summary>
    private ElementId? GetSolidFillPatternId()
    {
        if (_solidFillPatternId is not null)
        {
            return _solidFillPatternId == ElementId.InvalidElementId ? null : _solidFillPatternId;
        }

        FillPatternElement? solidFill = new FilteredElementCollector(_document)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);

        _solidFillPatternId = solidFill?.Id ?? ElementId.InvalidElementId;
        return solidFill?.Id;
    }
}
