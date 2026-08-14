using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Core.Domain;

/// <summary>
/// מחשבת את מקטע הצינור (<see cref="PipeSegment"/>) הישר בין אסלה
/// לקולטן שאליו היא משויכת (לפי <see cref="CollectorPoint.ConnectedFixtureIds"/>),
/// לפי הכללים המאושרים ב-rules.md (חוק 2 - שיפוע; קוטר קבוע למקטע
/// אסלה-לקולטן). זו לוגיקה עסקית טהורה (אין כאן RevitAPI) - יצירת
/// הגיאומטריה/ה-DirectShape בפועל, ובדיקת חסימת-קיר, נשארות ב-
/// PlumbingSystem.Revit (ראו docs/step7.md).
/// </summary>
/// <remarks>
/// **גרסה שנייה**: מסלול קו-ישר (<see cref="Calculate"/>) כשאין
/// חסימה, ומסלול-עוקף דו-מקטעי (<see cref="CalculateDetour"/>, חוק 9 -
/// איסור זווית 90° חדה) כשזוהתה חסימה בפועל (לא מראש - ראו
/// docs/step7.md לגבי הנימוק ל-YAGNI שהוביל לסדר הבנייה הזה: קודם
/// בדיקת-חסימה בלי פתרון, ורק אחרי שהוכח בנתונים אמיתיים ש-5 מתוך 11
/// מקטעים באמת חוצים קיר, נבנתה לוגיקת-העקיפה).
/// </remarks>
public static class PipeRouteCalculator
{
    /// <summary>קוטר קבוע (מ"מ) לכל מקטע צינור אסלה-לקולטן, לפי הדרישה המאושרת.</summary>
    public const double PipeDiameterMm = 110.0;

    /// <summary>גבול תחתון (אחוזים) לטווח השיפוע התקף, לפי חוק 2.</summary>
    public const double MinSlopePercent = 1.5;

    /// <summary>גבול עליון (אחוזים) לטווח השיפוע התקף, לפי חוק 2.</summary>
    public const double MaxSlopePercent = 2.0;

    /// <summary>
    /// שיפוע ברירת-המחדל (אחוזים) שלפיו מחושב הפרש ה-Z של כל מקטע -
    /// **אמצע** הטווח התקף (1.5-2.0%), לא אחד הקצוות. הנימוק (החלטת
    /// המנהלת): הטווח קיים כדי לתת גמישות הנדסית, לא כדי להצמד לקצה
    /// אחד ממנו - קצה (למשל 2.0%) הוא הכי-קרוב-לחריגה אם מצטרפת שגיאת
    /// עיגול/חישוב קטנה כלשהי בהמשך השרשרת. אמצע-הטווח נותן מרווח-
    /// ביטחון משני הכיוונים - עקבי עם ההחלטה המקבילה לגבי מרחק-קולטן
    /// מקסימלי (<see cref="CollectorLocator.MaxDistanceMeters"/> הוא
    /// הגבול, <see cref="CollectorLocator.PreferredDistanceMeters"/>
    /// הוא המועדף - לא הגבול עצמו).
    /// </summary>
    /// <remarks>
    /// **החלטת-ביניים, לא סופית**: גישת "חישוב-עצמאי-לכל-צינור" (כל
    /// מקטע אסלה-קולטן מחושב בנפרד, בלי לדרוש Z משותף לכל הצינורות
    /// שמתחברים לאותו קולטן פיזי) נבחרה כי היא קרובה יותר להיגיון
    /// ההנדסי הכללי בשרברבות (שוחה/קולטן יכולה לקבל כמה צינורות
    /// בגבהים מעט שונים, לא בדיוק באותה נקודה-פיזית) - אך זה **ממתין
    /// לאישור סופי** מהמנהלת/אינסטלטור. אם התשובה תהיה שונה (למשל: כל
    /// הצינורות המתחברים לקולטן אחד חייבים לחלוק את אותו Z פיזי) - יש
    /// לעדכן את השיטה הזו (ואת <see cref="Calculate"/>) בהתאם. ראו
    /// docs/step7.md.
    /// </remarks>
    public const double DefaultSlopePercent = (MinSlopePercent + MaxSlopePercent) / 2.0;

    /// <summary>
    /// קידומת קבועה ל-Id של כל מקטע צינור (למשל <c>"PIPE-5283771-COL-5283870"</c>) -
    /// קבוע ציבורי (לא רק מחרוזת מוטבעת), באותו עיקרון בדיוק כמו
    /// <see cref="CollectorLocator.CollectorIdPrefix"/>, כדי ש-
    /// <c>PlumbingSystem.Revit</c> (בזיהוי/מחיקת צינורות ישנים לפני
    /// יצירת חדשים) יוכל להתאים לזיהוי הזה בלי לשכפל את המחרוזת.
    /// </summary>
    public const string PipeIdPrefix = "PIPE-";

    /// <summary>
    /// חצי-זווית-הפנייה (מעלות) שכל אחד משני מקטעי-העקיפה סוטה מהקו
    /// הישר המקורי (D) - לכיוונים מנוגדים. הזווית **בין שני המקטעים**
    /// היא, מהבנייה עצמה (ראו <see cref="CalculateDetour"/>), תמיד
    /// <c>2 × 22.5° = 45°</c> - בדיוק, לא בקירוב, ולא תלוית-מרחקים -
    /// לפי חוק 9.
    /// </summary>
    public const double DetourHalfBendAngleDegrees = 22.5;

    /// <summary>
    /// גבול תחתון (מעלות) לטווח-הסבילות סביב 45° - **רק** רשת-ביטחון
    /// נגד שגיאות floating-point (הבנייה עצמה מבטיחה 45° מדויק) - לא
    /// קריטריון-בחירה. ±5° לפי דרישה מפורשת (לא ±15° כמו בגרסה קודמת).
    /// </summary>
    public const double MinDetourAngleDegrees = 40.0;

    /// <summary>גבול עליון (מעלות) לטווח-הסבילות סביב 45° - ראו <see cref="MinDetourAngleDegrees"/>.</summary>
    public const double MaxDetourAngleDegrees = 50.0;

    /// <summary>
    /// זווית-כל-פנייה (מעלות) במסלול "Y מדורג" (<see cref="CalculateStaggeredDetour"/>) -
    /// האלטרנטיבה שחוק 9 מזכיר ("2 זוויות של 45° **או** Y מדורג") -
    /// שתי פניות **עדינות יותר** (לא 45° כל אחת - ראו הנימוק ב-
    /// <see cref="CalculateStaggeredDetour"/>) במקום פנייה בודדת חדה.
    /// ערך לדוגמה (~22°), ניתן-לכיוונון - לא נתון הנדסי מאושר.
    /// </summary>
    public const double StaggeredDetourBendAngleDegrees = 22.5;

    /// <summary>
    /// בונה את מזהה-המסלול המשותף לאסלה-קולטן נתונים - מקור יחיד
    /// לפורמט ה-Id, כדי ש-<see cref="Calculate"/> (מקטע יחיד) ו-
    /// <see cref="CalculateDetour"/> (שני מקטעים, כל אחד עם סיומת
    /// <c>-leg1</c>/<c>-leg2</c> על בסיס המזהה הזה) לא ישכפלו את
    /// לוגיקת-הפורמט, וכדי ש-<c>PlumbingSystem.Revit</c> יוכל לתייג את
    /// ה-<c>DirectShape</c> המשותף (לכל המקטעים של אותו מסלול, גם
    /// כשהם שניים) באותו Id בדיוק בלי לגזור/לנחש אותו בחזרה.
    /// </summary>
    public static string BuildRouteId(ToiletFixture fixture, CollectorPoint collector)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(collector);

        return $"{PipeIdPrefix}{fixture.Id}-{collector.Id}";
    }

    /// <summary>
    /// מחשבת את מקטע הצינור הישר בין <paramref name="fixture"/> ל-
    /// <paramref name="collector"/>. נקודת ההתחלה היא מיקום האסלה
    /// בדיוק כפי שהוא; נקודת הסיום שומרת על X,Y של הקולטן (מיקום
    /// הקולטן הפיזי), אבל ה-Z שלה **מחושב מחדש** (לא נלקח מ-
    /// <see cref="CollectorPoint.Location"/>.Z, שעדיין שווה כיום ל-Z
    /// הגולמי של האסלה שממנה נגזר הקולטן - ראו docs/step7.md) לפי
    /// <see cref="DefaultSlopePercent"/> על המרחק האופקי בין השניים.
    /// </summary>
    /// <param name="fixture">האסלה - נקודת המוצא של המקטע.</param>
    /// <param name="collector">הקולטן - נקודת הסיום (X,Y) של המקטע.</param>
    /// <returns>מקטע צינור עם קוטר <see cref="PipeDiameterMm"/> ושיפוע בפועל בטווח התקף.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fixture"/> או <paramref name="collector"/> הם null.</exception>
    /// <exception cref="InvalidOperationException">
    /// אם השיפוע המחושב בפועל (הפרש Z חלקי מרחק אופקי) יוצא מחוץ לטווח
    /// <see cref="MinSlopePercent"/>-<see cref="MaxSlopePercent"/> -
    /// במבנה הנוכחי זה קורה רק אם המרחק האופקי אפסי/כמעט-אפסי (האסלה
    /// והקולטן כמעט באותה נקודה, X,Y) - מצב מנוון שלא אמור לקרות
    /// בנתונים אמיתיים (הקולטן תמיד עבר wall-snap שהזיז אותו מהאסלה),
    /// אבל עדיף לזרוק שגיאה ברורה מאשר לחלק באפס בשקט.
    /// </exception>
    public static PipeSegment Calculate(ToiletFixture fixture, CollectorPoint collector)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(collector);

        double horizontalDistance = GeometryUtils.Distance2D(fixture.Location, collector.Location);
        double zDrop = horizontalDistance * (DefaultSlopePercent / 100.0);
        double endZ = fixture.Location.Z - zDrop;

        double computedSlopePercent = horizontalDistance > 1e-9
            ? ((fixture.Location.Z - endZ) / horizontalDistance) * 100.0
            : double.NaN;

        if (double.IsNaN(computedSlopePercent)
            || computedSlopePercent < MinSlopePercent
            || computedSlopePercent > MaxSlopePercent)
        {
            throw new InvalidOperationException(
                $"השיפוע המחושב בין אסלה '{fixture.Id}' לקולטן '{collector.Id}' " +
                $"({computedSlopePercent:F4}%) חורג מהטווח המאושר " +
                $"{MinSlopePercent:F1}%-{MaxSlopePercent:F1}% - כנראה מרחק אופקי " +
                $"אפסי/כמעט-אפסי ({horizontalDistance:F4} מ') בין השניים.");
        }

        var endPoint = new Point3D(collector.Location.X, collector.Location.Y, endZ);

        return new PipeSegment(
            id: BuildRouteId(fixture, collector),
            startPoint: fixture.Location,
            endPoint: endPoint,
            diameterMm: PipeDiameterMm,
            slopePercent: computedSlopePercent);
    }

    /// <summary>
    /// מחשבת מסלול-עוקף דו-מקטעי בין <paramref name="fixture"/> ל-
    /// <paramref name="collector"/>, כשזוהתה חסימה (קיר) על המסלול
    /// הישר - לפי חוק 9 (איסור זווית 90° חדה; פנייה חובה של 45°).
    /// </summary>
    /// <remarks>
    /// **גרסה שנייה של הבנייה** - הגרסה הראשונה (היסט קבוע מהאסלה,
    /// לפי <c>tan(45°)=1</c>) קבעה זווית מדויקת רק למקטע **הראשון**
    /// ביחס לקו המקורי; זווית-המקטע **השני** נותרה תלוית-גיאומטריה
    /// (בפועל: 67.01° בנתונים אמיתיים - הרבה מעבר ל-45°) כי היא
    /// הניחה, בלי להבטיח, שהיסט קטן ביחס למרחק הכולל "יתקרב" ל-45°
    /// - הנחה שהתבררה כלא-אמינה. **הבנייה הנוכחית לא מניחה כלום**:
    /// שני הכיוונים (<c>U1</c>, <c>U2</c>) נקבעים **מראש**, כל אחד
    /// סוטה בדיוק <see cref="DetourHalfBendAngleDegrees"/> (22.5°)
    /// מ-<c>D</c> (הקו הישר המקורי) לכיוונים מנוגדים - כך שהזווית
    /// **ביניהם** היא <c>22.5°+22.5°=45°</c> **תמיד**, בכל גיאומטריה,
    /// ללא תלות במרחקים. ה-waypoint עצמו נמצא **אחרי** קביעת הכיוונים -
    /// כנקודת-ההצטלבות של הקרן מהאסלה (בכיוון <c>U1</c>) עם הקרן
    /// מהקולטן אחורה (בכיוון <c>-U2</c>) - פתרון מדויק של שתי משוואות
    /// ליניאריות (לא נוסחת-היסט מקורבת).
    /// </remarks>
    /// <remarks>
    /// **בחירת הצד**: בין שני הכיוונים האפשריים ל-<c>U1</c> (D מסובב
    /// +22.5° או -22.5°) נבחרת ברירת-המחדל (<paramref name="useOppositeSide"/><c>=false</c>)
    /// זו שמצביעה **הרחק** ממרכז הקיר החוסם (<paramref name="blockingWall"/>) -
    /// כדי שהמקטע הראשון, היוצא מהאסלה, יפנה מיד לצד הפנוי (היוריסטיקה
    /// הסבירה ביותר). <c>U2</c> הוא תמיד המראה של <c>U1</c> (הסטייה
    /// הנגדית), כך שהזווית ביניהם מובטחת 45° בלי קשר לבחירת הצד.
    /// </remarks>
    /// <remarks>
    /// **מגבלה ידועה, לא-נפתרת כאן**: הבנייה קובעת זווית-פנייה תקינה
    /// (חוק 9) בביטחון מתמטי, אבל **אינה מבטיחה** שהמסלול המתקבל אכן
    /// עוקף פיזית את הקיר. חשוב: יש **בדיוק שני** מועמדים אפשריים
    /// (לא רצף) - הצד המועדף (<paramref name="useOppositeSide"/><c>=false</c>)
    /// והצד השני (<c>=true</c>). למרחקים קצרים מאוד (במרחב צר, למשל
    /// ליד פינת-שני-קירות) ייתכן ש**שני** המועמדים חוסמים - זו מגבלה
    /// גיאומטרית אמיתית, לא באג: <c>PlumbingSystem.Revit</c> אחראית
    /// לנסות את שני הצדדים (לא רק אחד) ולסמן "דורש פתרון ידני של
    /// מהנדס" אם שניהם נכשלים, במקום לזרוק שגיאה שעוצרת ריצה שלמה -
    /// ראו docs/step7.md.
    /// </remarks>
    /// <param name="fixture">האסלה - נקודת המוצא של המסלול.</param>
    /// <param name="collector">הקולטן - נקודת הסיום (X,Y) של המסלול.</param>
    /// <param name="blockingWall">
    /// הקיר החוסם שזוהה על המסלול הישר (ייצוג גיאומטרי טהור - אותו
    /// <see cref="WallEdgeSnapper.WallSegment"/> שכבר משמש למציאת-
    /// קירות בשלב 6, לא <c>Autodesk.Revit.DB.Wall</c>) - נקודת-האמצע
    /// שלו משמשת לבחירת הצד (ראו למעלה).
    /// </param>
    /// <param name="useOppositeSide">
    /// <c>false</c> (ברירת מחדל) - הצד המועדף היוריסטית (הרחק ממרכז
    /// הקיר). <c>true</c> - הצד השני (המראה) - לשימוש כשהצד המועדף
    /// נכשל (גיאומטרית, או עדיין חוסם קיר בפועל) - ראו "מגבלה ידועה" למעלה.
    /// </param>
    /// <param name="useWallDirectionAsReference">
    /// <c>false</c> (ברירת מחדל) - <c>U1</c>/<c>U2</c> נגזרים מ-<c>D</c>
    /// (הקו הישר אסלה-קולטן), כמתועד למעלה. <c>true</c> - נגזרים
    /// מכיוון **הקיר החוסם עצמו** (מנוטרל-סימן לכיוון D, כדי שלא יהיה
    /// תלוי-שרירותי בסדר Start/End של הקיר) - זווית-החיבור בין שני
    /// המקטעים עדיין מובטחת 45° (הבנייה לא תלויה בבחירת-הכיוון-הבסיסי
    /// כדי לתת את התוצאה הזו), אבל **מיקום** ה-waypoint בפועל שונה -
    /// ראו docs/step7.md לגבי הנימוק (ונתוני-אימות אמיתיים).
    /// </param>
    /// <returns>
    /// שני מקטעי צינור, בסדר: אסלה→waypoint, ואז waypoint→קולטן - שני
    /// המקטעים יחד שומרים על שיפוע <see cref="DefaultSlopePercent"/>
    /// (כל אחד בנפרד, ולכן גם על פני האורך הכולל - ראו docs/step7.md),
    /// ואורכם הכולל (אורך-בפועל של הצינור, לא המרחק האווירי) לא עובר
    /// את <see cref="CollectorLocator.MaxDistanceMeters"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">אחד הפרמטרים הוא null.</exception>
    /// <exception cref="InvalidOperationException">
    /// אם המרחק האופקי (אסלה-קולטן) אפסי/כמעט-אפסי (כמו ב-<see cref="Calculate"/>);
    /// אם אחד משני האורכים המחושבים (<c>t1</c>/<c>t2</c>) יוצא שלילי -
    /// כלומר נקודת-ההצטלבות אינה "קדימה" משני הכיוונים, ולא ניתן לבנות
    /// מסלול-עוקף תקין לגיאומטריה הזו בזווית המבוקשת; אם סך שני
    /// האורכים חורג מ-<see cref="CollectorLocator.MaxDistanceMeters"/> -
    /// מגבלה פיזית קשיחה (עובי-מילוי), לא ניתנת-לחריגה, ולכן נבדקת
    /// במפורש (לא רק על המרחק האווירי, שכבר נבדק קודם ב-<see cref="CollectorLocator"/>,
    /// אלא על האורך **בפועל** של המסלול-העוקף שגדול ממנו); או אם
    /// הזווית בין שני המקטעים (בדיקת-בטיחות בלבד - ראו
    /// <see cref="MinDetourAngleDegrees"/>) חורגת מ-45°±5° - לא אמור
    /// לקרות מהבנייה עצמה, אבל עדיף להיכשל בבירור מאשר לסמוך על הנחה.
    /// </exception>
    public static IReadOnlyList<PipeSegment> CalculateDetour(
        ToiletFixture fixture,
        CollectorPoint collector,
        WallEdgeSnapper.WallSegment blockingWall,
        bool useOppositeSide = false,
        bool useWallDirectionAsReference = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(collector);

        double totalHorizontalDistance = GeometryUtils.Distance2D(fixture.Location, collector.Location);
        if (totalHorizontalDistance <= 1e-9)
        {
            throw new InvalidOperationException(
                $"לא ניתן לחשב מסלול-עוקף בין אסלה '{fixture.Id}' לקולטן '{collector.Id}' - " +
                $"המרחק האופקי ({totalHorizontalDistance:F4} מ') אפסי/כמעט-אפסי.");
        }

        (double u1X, double u1Y, double u2X, double u2Y, _, _) = ComputeBendDirections(
            fixture.Location, collector.Location, blockingWall, DetourHalfBendAngleDegrees,
            useWallDirectionAsReference, useOppositeSide, totalHorizontalDistance);

        // פתרון מדויק (לא קירוב): t1·U1 + t2·U2 = (קולטן - אסלה).
        // הדטרמיננטה היא sin(±45°) - אף פעם לא אפס, כי הזווית בין
        // U1 ל-U2 קבועה (45°) ולעולם לא 0°/180°.
        double rx = collector.Location.X - fixture.Location.X;
        double ry = collector.Location.Y - fixture.Location.Y;
        double determinant = (u1X * u2Y) - (u1Y * u2X);

        double t1 = ((rx * u2Y) - (ry * u2X)) / determinant;
        double t2 = ((u1X * ry) - (u1Y * rx)) / determinant;

        if (t1 <= 0 || t2 <= 0)
        {
            throw new InvalidOperationException(
                $"לא ניתן לבנות מסלול-עוקף תקין (זווית 45°) בין אסלה '{fixture.Id}' " +
                $"לקולטן '{collector.Id}' לגיאומטריה הנתונה - נקודת-ההצטלבות של שני " +
                $"המקטעים בזווית המבוקשת יוצאת 'אחורה' (t1={t1:F4}, t2={t2:F4}), לא קדימה.");
        }

        double totalRouteLength = t1 + t2;
        if (totalRouteLength > CollectorLocator.MaxDistanceMeters)
        {
            throw new InvalidOperationException(
                $"אורך מסלול-העוקף הכולל ({totalRouteLength:F4} מ') בין אסלה '{fixture.Id}' " +
                $"לקולטן '{collector.Id}' חורג מהמגבלה הפיזית הקשיחה " +
                $"({CollectorLocator.MaxDistanceMeters:F1} מ', עובי-מילוי - לא ניתנת לחריגה) - " +
                "העקיפה מאריכה את הצינור מעבר למה שמותר, גם אם המרחק האווירי המקורי היה תקין.");
        }

        double waypointX = fixture.Location.X + (t1 * u1X);
        double waypointY = fixture.Location.Y + (t1 * u1Y);

        double leg1ZDrop = t1 * (DefaultSlopePercent / 100.0);
        double waypointZ = fixture.Location.Z - leg1ZDrop;
        var waypoint = new Point3D(waypointX, waypointY, waypointZ);

        double leg2ZDrop = t2 * (DefaultSlopePercent / 100.0);
        double collectorEndZ = waypointZ - leg2ZDrop;
        var collectorEndPoint = new Point3D(collector.Location.X, collector.Location.Y, collectorEndZ);

        // בדיקת-בטיחות (לא קריטריון-בחירה - ראו MinDetourAngleDegrees):
        // הזווית בין U1 ל-U2 אמורה להיות 45° מדויק מהבנייה עצמה.
        double cosAngle = (u1X * u2X) + (u1Y * u2Y);
        double angleDegrees = Math.Acos(Math.Clamp(cosAngle, -1.0, 1.0)) * (180.0 / Math.PI);

        if (angleDegrees < MinDetourAngleDegrees || angleDegrees > MaxDetourAngleDegrees)
        {
            throw new InvalidOperationException(
                $"זווית-החיבור המחושבת ({angleDegrees:F2}°) במסלול-העוקף בין אסלה " +
                $"'{fixture.Id}' לקולטן '{collector.Id}' חורגת מ-45°±5° - לא אמור לקרות " +
                "מהבנייה הגיאומטרית עצמה; ייתכן קלט חריג.");
        }

        string routeId = BuildRouteId(fixture, collector);

        var leg1 = new PipeSegment(
            id: $"{routeId}-leg1",
            startPoint: fixture.Location,
            endPoint: waypoint,
            diameterMm: PipeDiameterMm,
            slopePercent: DefaultSlopePercent);

        var leg2 = new PipeSegment(
            id: $"{routeId}-leg2",
            startPoint: waypoint,
            endPoint: collectorEndPoint,
            diameterMm: PipeDiameterMm,
            slopePercent: DefaultSlopePercent);

        return new[] { leg1, leg2 };
    }

    /// <summary>
    /// מחשבת מסלול "Y מדורג" (staggered) תלת-מקטעי - האלטרנטיבה
    /// שחוק 9 מזכיר במפורש ("2 זוויות של 45° **או** Y מדורג"), לשימוש
    /// כשגם הצד המועדף וגם הצד השני של <see cref="CalculateDetour"/>
    /// (המסלול הדו-מקטעי) עדיין חוסמים קיר בפועל.
    /// </summary>
    /// <remarks>
    /// **למה זה עשוי להצליח איפה ש-<see cref="CalculateDetour"/> נכשל**:
    /// למסלול הדו-מקטעי יש **בדיוק שני** waypoint-ים אפשריים (לא רצף) -
    /// אין שום פרמטר לכוונן אם שניהם חוסמים. למסלול המדורג הזה יש
    /// **דרגת-חופש נוספת**: אורך מקטע-הביניים (<paramref name="crossoverLengthMeters"/>),
    /// שנבחר על ידי הקורא (לא מחושב אוטומטית) - כלומר יש **רצף שלם**
    /// של מסלולים אפשריים (אחד לכל אורך-ביניים), לא רק שתי אפשרויות
    /// בדידות. <c>PlumbingSystem.Revit</c> יכולה לנסות כמה ערכים
    /// (וגם שני הצדדים לכל ערך) עד שהיא מוצאת אחד שבאמת עוקף את הקיר.
    /// </remarks>
    /// <remarks>
    /// **הבנייה הגיאומטרית**: שלושה מקטעים - <c>leg1</c> (אסלה→waypoint1,
    /// בכיוון <c>U1</c> = D מסובב ±<see cref="StaggeredDetourBendAngleDegrees"/>),
    /// <c>crossover</c> (waypoint1→waypoint2, בכיוון <c>D</c> עצמו - **אורך
    /// קבוע-מראש**, לא מחושב), <c>leg3</c> (waypoint2→קולטן, בכיוון
    /// <c>U2</c> = D מסובב בכיוון המנוגד ל-<c>U1</c>). מכיוון ש-
    /// <c>U1</c>+<c>U2</c> = <c>2·cos(β)·D</c> בדיוק (סימטריה מסביב
    /// ל-D), הפתרון הוא **סגור ופשוט** - לא מערכת-משוואות: אם
    /// <c>remaining = totalHorizontalDistance - crossoverLengthMeters</c>,
    /// אז <c>t1 = t3 = remaining / (2·cos(β))</c> - שני האורכים תמיד
    /// שווים (סימטריה), והמסלול מגיע **בדיוק** לקולטן (הוכחה: ראו
    /// התיעוד בקוד עצמו, לא רק בהערה זו). כל אחד משני הבנים (leg1-crossover,
    /// crossover-leg3) הוא בדיוק <c>β</c> מעלות - לא בקירוב.
    /// </remarks>
    /// <param name="fixture">האסלה - נקודת המוצא של המסלול.</param>
    /// <param name="collector">הקולטן - נקודת הסיום (X,Y) של המסלול.</param>
    /// <param name="blockingWall">הקיר החוסם - נקודת-האמצע שלו קובעת את בחירת-הצד, כמו ב-<see cref="CalculateDetour"/>.</param>
    /// <param name="crossoverLengthMeters">
    /// אורך מקטע-הביניים (מטרים) - **הפרמטר החופשי**. ערך גדול יותר =
    /// יותר עקיפה (מרוחק יותר מהקו הישר), אבל גם מקטע כולל ארוך יותר
    /// (עלול להתנגש במגבלת <see cref="CollectorLocator.MaxDistanceMeters"/>).
    /// </param>
    /// <param name="useOppositeSide">כמו ב-<see cref="CalculateDetour"/> - <c>false</c>=הצד המועדף (הרחק מהקיר), <c>true</c>=הצד השני.</param>
    /// <param name="useWallDirectionAsReference">
    /// כמו ב-<see cref="CalculateDetour"/> - <c>false</c> (ברירת מחדל):
    /// <c>U1</c>/<c>U2</c> וכיוון-הביניים (<c>crossover</c>) נגזרים
    /// מ-<c>D</c>. <c>true</c>: נגזרים מכיוון **הקיר עצמו** - מקטע-
    /// הביניים "רץ" לאורך הקיר (לא לאורך הקו הישר המקורי), עם דרגת-
    /// החופש (אורך-הביניים) עדיין קיימת. ראו docs/step7.md.
    /// </param>
    /// <returns>שלושה מקטעי צינור רציפים: אסלה→waypoint1→waypoint2→קולטן, שיפוע נשמר על פני האורך הכולל (כל מקטע לפי אורכו-שלו).</returns>
    /// <exception cref="ArgumentNullException">אחד הפרמטרים הוא null.</exception>
    /// <exception cref="InvalidOperationException">
    /// אם המרחק האופקי אפסי/כמעט-אפסי; אם אחד משני מקטעי-הקצה (<c>t1</c>/<c>t3</c>)
    /// יוצא שלילי (לא ניתן לבנות מסלול-תקין לגיאומטריה/פרמטרים הנתונים);
    /// אם אורך המסלול הכולל (t1+crossoverLengthMeters+t3) חורג מ-<see cref="CollectorLocator.MaxDistanceMeters"/>.
    /// </exception>
    public static IReadOnlyList<PipeSegment> CalculateStaggeredDetour(
        ToiletFixture fixture,
        CollectorPoint collector,
        WallEdgeSnapper.WallSegment blockingWall,
        double crossoverLengthMeters,
        bool useOppositeSide = false,
        bool useWallDirectionAsReference = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(collector);

        double totalHorizontalDistance = GeometryUtils.Distance2D(fixture.Location, collector.Location);
        if (totalHorizontalDistance <= 1e-9)
        {
            throw new InvalidOperationException(
                $"לא ניתן לחשב מסלול Y-מדורג בין אסלה '{fixture.Id}' לקולטן '{collector.Id}' - " +
                $"המרחק האופקי ({totalHorizontalDistance:F4} מ') אפסי/כמעט-אפסי.");
        }

        (double u1X, double u1Y, double u2X, double u2Y, double referenceX, double referenceY) = ComputeBendDirections(
            fixture.Location, collector.Location, blockingWall, StaggeredDetourBendAngleDegrees,
            useWallDirectionAsReference, useOppositeSide, totalHorizontalDistance);

        // פתרון כללי (לא הנחת-סימטריה מוקדמת - תקף בין אם ה-reference
        // הוא D ובין אם הוא כיוון-הקיר): t1·U1 + crossover·reference + t3·U2 = קולטן-אסלה.
        // מחסירים את תרומת מקטע-הביניים ופותרים את שני מקטעי-הקצה על
        // השארית, באותה שיטת-Cramer בדיוק כמו CalculateDetour.
        double rx = collector.Location.X - fixture.Location.X;
        double ry = collector.Location.Y - fixture.Location.Y;
        double adjustedRx = rx - (crossoverLengthMeters * referenceX);
        double adjustedRy = ry - (crossoverLengthMeters * referenceY);

        double determinant = (u1X * u2Y) - (u1Y * u2X);
        double t1 = ((adjustedRx * u2Y) - (adjustedRy * u2X)) / determinant;
        double t3 = ((u1X * adjustedRy) - (u1Y * adjustedRx)) / determinant;

        if (t1 <= 0 || t3 <= 0)
        {
            throw new InvalidOperationException(
                $"לא ניתן לבנות מסלול Y-מדורג תקין בין אסלה '{fixture.Id}' לקולטן '{collector.Id}' " +
                $"לגיאומטריה/אורך-הביניים הנתונים - מקטעי-הקצה יוצאים 'אחורה' (t1={t1:F4}, t3={t3:F4}), לא קדימה.");
        }

        double totalRouteLength = t1 + crossoverLengthMeters + t3;
        if (totalRouteLength > CollectorLocator.MaxDistanceMeters)
        {
            throw new InvalidOperationException(
                $"אורך מסלול ה-Y-המדורג הכולל ({totalRouteLength:F4} מ') בין אסלה '{fixture.Id}' " +
                $"לקולטן '{collector.Id}' חורג מהמגבלה הפיזית הקשיחה " +
                $"({CollectorLocator.MaxDistanceMeters:F1} מ', עובי-מילוי - לא ניתנת לחריגה).");
        }

        double waypoint1X = fixture.Location.X + (t1 * u1X);
        double waypoint1Y = fixture.Location.Y + (t1 * u1Y);
        double leg1ZDrop = t1 * (DefaultSlopePercent / 100.0);
        double waypoint1Z = fixture.Location.Z - leg1ZDrop;
        var waypoint1 = new Point3D(waypoint1X, waypoint1Y, waypoint1Z);

        double waypoint2X = waypoint1X + (crossoverLengthMeters * referenceX);
        double waypoint2Y = waypoint1Y + (crossoverLengthMeters * referenceY);
        double crossoverZDrop = crossoverLengthMeters * (DefaultSlopePercent / 100.0);
        double waypoint2Z = waypoint1Z - crossoverZDrop;
        var waypoint2 = new Point3D(waypoint2X, waypoint2Y, waypoint2Z);

        double leg3ZDrop = t3 * (DefaultSlopePercent / 100.0);
        double collectorEndZ = waypoint2Z - leg3ZDrop;
        var collectorEndPoint = new Point3D(collector.Location.X, collector.Location.Y, collectorEndZ);

        string routeId = BuildRouteId(fixture, collector);

        var leg1 = new PipeSegment(
            id: $"{routeId}-leg1",
            startPoint: fixture.Location,
            endPoint: waypoint1,
            diameterMm: PipeDiameterMm,
            slopePercent: DefaultSlopePercent);

        var crossover = new PipeSegment(
            id: $"{routeId}-crossover",
            startPoint: waypoint1,
            endPoint: waypoint2,
            diameterMm: PipeDiameterMm,
            slopePercent: DefaultSlopePercent);

        var leg3 = new PipeSegment(
            id: $"{routeId}-leg3",
            startPoint: waypoint2,
            endPoint: collectorEndPoint,
            diameterMm: PipeDiameterMm,
            slopePercent: DefaultSlopePercent);

        return new[] { leg1, crossover, leg3 };
    }

    /// <summary>
    /// לוגיקה משותפת ל-<see cref="CalculateDetour"/> ו-<see cref="CalculateStaggeredDetour"/>:
    /// קובעת את <c>U1</c>/<c>U2</c> (שני כיוונים בזווית ±<paramref name="bendAngleDegrees"/>
    /// מ-"כיוון-ייחוס" - ראו <paramref name="useWallDirectionAsReference"/>),
    /// בוחרת ביניהם את הצד (לפי מרכז-הקיר החוסם, או ההפך אם
    /// <paramref name="useOppositeSide"/>), ומחזירה גם את כיוון-הייחוס
    /// עצמו (נדרש ב-<see cref="CalculateStaggeredDetour"/> ככיוון
    /// מקטע-הביניים).
    /// </summary>
    /// <param name="fixtureLocation">מיקום האסלה.</param>
    /// <param name="collectorLocation">מיקום הקולטן.</param>
    /// <param name="blockingWall">הקיר החוסם (למרכז - בחירת-צד; לכיוון - אם <paramref name="useWallDirectionAsReference"/>).</param>
    /// <param name="bendAngleDegrees">חצי-זווית-הפנייה (מעלות) - 22.5 עבור שתי הקריאות הקיימות.</param>
    /// <param name="useWallDirectionAsReference">
    /// <c>false</c> - כיוון-הייחוס הוא <c>D</c> (הקו הישר אסלה-קולטן).
    /// <c>true</c> - כיוון-הייחוס הוא כיוון **הקיר החוסם עצמו**
    /// (<paramref name="blockingWall"/>.End - .Start, מנוטרל-סימן כך
    /// שמכפלתו הסקלרית עם D תמיד חיובית - לא תלוי-שרירותי בסדר
    /// Start/End של הקיר). בשני המקרים, הזווית בין U1 ל-U2 היא בדיוק
    /// <c>2 × bendAngleDegrees</c> - הבנייה לא תלויה בבחירת-הייחוס כדי
    /// להבטיח את זה, רק **מיקום** ה-waypoint בפועל משתנה.
    /// </param>
    /// <param name="useOppositeSide">בחירת הצד ההפוך מהיוריסטיקה (ראו קוראים - <see cref="CalculateDetour"/>/<see cref="CalculateStaggeredDetour"/>).</param>
    /// <param name="totalHorizontalDistance">המרחק האופקי (אסלה-קולטן) - כבר מחושב ומאומת (לא אפס) על ידי הקורא.</param>
    private static (double U1X, double U1Y, double U2X, double U2Y, double ReferenceX, double ReferenceY) ComputeBendDirections(
        Point3D fixtureLocation,
        Point3D collectorLocation,
        WallEdgeSnapper.WallSegment blockingWall,
        double bendAngleDegrees,
        bool useWallDirectionAsReference,
        bool useOppositeSide,
        double totalHorizontalDistance)
    {
        double directionX = (collectorLocation.X - fixtureLocation.X) / totalHorizontalDistance;
        double directionY = (collectorLocation.Y - fixtureLocation.Y) / totalHorizontalDistance;

        double referenceX = directionX;
        double referenceY = directionY;

        if (useWallDirectionAsReference)
        {
            double wallDirX = blockingWall.End.X - blockingWall.Start.X;
            double wallDirY = blockingWall.End.Y - blockingWall.Start.Y;
            double wallDirLength = Math.Sqrt((wallDirX * wallDirX) + (wallDirY * wallDirY));

            if (wallDirLength > 1e-9)
            {
                wallDirX /= wallDirLength;
                wallDirY /= wallDirLength;

                // מכוונים את כיוון-הקיר "קדימה" (מכפלה סקלרית חיובית מול
                // D) - כדי שלא יהיה תלוי-שרירותי בסדר Start/End של הקיר.
                if (((wallDirX * directionX) + (wallDirY * directionY)) < 0)
                {
                    wallDirX = -wallDirX;
                    wallDirY = -wallDirY;
                }

                referenceX = wallDirX;
                referenceY = wallDirY;
            }
        }

        double bendAngleRadians = bendAngleDegrees * (Math.PI / 180.0);
        (double rotatedPlusX, double rotatedPlusY) = Rotate2D(referenceX, referenceY, bendAngleRadians);
        (double rotatedMinusX, double rotatedMinusY) = Rotate2D(referenceX, referenceY, -bendAngleRadians);

        // בחירת צד: המאונך לכיוון-הייחוס קובע איזה צד "רחוק מהקיר".
        double perpendicularX = -referenceY;
        double perpendicularY = referenceX;

        double wallMidpointX = (blockingWall.Start.X + blockingWall.End.X) / 2.0;
        double wallMidpointY = (blockingWall.Start.Y + blockingWall.End.Y) / 2.0;
        double towardWallMidpointX = wallMidpointX - fixtureLocation.X;
        double towardWallMidpointY = wallMidpointY - fixtureLocation.Y;

        if (((perpendicularX * towardWallMidpointX) + (perpendicularY * towardWallMidpointY)) > 0)
        {
            perpendicularX = -perpendicularX;
            perpendicularY = -perpendicularY;
        }

        double dotPlus = (rotatedPlusX * perpendicularX) + (rotatedPlusY * perpendicularY);
        double dotMinus = (rotatedMinusX * perpendicularX) + (rotatedMinusY * perpendicularY);

        bool preferPlusForU1 = dotPlus >= dotMinus;
        if (useOppositeSide)
        {
            preferPlusForU1 = !preferPlusForU1;
        }

        return preferPlusForU1
            ? (rotatedPlusX, rotatedPlusY, rotatedMinusX, rotatedMinusY, referenceX, referenceY)
            : (rotatedMinusX, rotatedMinusY, rotatedPlusX, rotatedPlusY, referenceX, referenceY);
    }

    /// <summary>מסובבת וקטור-יחידה 2D בזווית נתונה (רדיאנים) - חיובי = נגד כיוון השעון.</summary>
    private static (double X, double Y) Rotate2D(double x, double y, double angleRadians)
    {
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);
        return ((x * cos) - (y * sin), (x * sin) + (y * cos));
    }
}