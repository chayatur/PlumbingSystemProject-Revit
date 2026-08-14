using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Core.Domain;

/// <summary>
/// לוגיקה טהורה (בלי RevitAPI) לבחירת "הקיר הקרוב ביותר" מתוך רשימת
/// מועמדים, ומציאת נקודת ההצמדה שלו לקצה. נוצר בעקבות באג שהתגלה
/// ויזואלית בשלב 6: קולטנים "קפצו" עד 9.84 מ' מהמיקום המקורי שלהם,
/// לקירות לגמרי לא-קשורים. הניסיון הראשון לתקן (סינון-רדיוס על
/// קירות שנאספו מ-<c>Room.GetBoundarySegments</c>) עדיין נכשל בדירה
/// אחרת: הבעיה לא הייתה "הרדיוס קטן מדי" אלא ש-<c>Room.GetBoundarySegments</c>
/// **עצמו** הוא מקור לא-אמין (מחזיר את כל הקירות שתוחמים את ה-Room,
/// שמוגדר ברמת הדירה השלמה - לא חדר פיזי בודד). הפתרון הסופי: הקורא
/// (<c>CollectorPlacementService</c>) מאתר קירות באמצעות ray-casting
/// פיזי (<c>ReferenceIntersector</c>, 8 כיוונים) - כך שרשימת קירות
/// המועמדים שמגיעה לכאן כבר מכילה רק קירות
/// שבאמת "רואים" את האסלה, לא קירות רחוקים ממקור לא-אמין. הסינון כאן
/// (<see cref="MaxSearchRadiusMeters"/>) נשאר כהגנת-שפיות נוספת
/// (defense in depth), לא כקריטריון הבחירה המרכזי.
/// </summary>
public static class WallEdgeSnapper
{
    /// <summary>
    /// רדיוס חיפוש (מטרים) - הגנת-שפיות בלבד: מכיוון שהקורא כבר מספק
    /// רשימת מועמדים שנאספה באמצעות ray-casting פיזי (לא לפי Room),
    /// כל מועמד שמגיע לכאן כבר אמור להיות בטווח הזה ממילא. השדה הזה
    /// שומר על הפונקציה בטוחה-לשימוש-עצמאי גם אם ייקרא בעתיד ממקור
    /// מועמדים אחר. ערך סביר לתא שירותים טיפוסי (לא נתון מדויק שאושר
    /// הנדסית) - קבוע יחיד לעדכן אם יתקבל מספר מדויק יותר.
    /// </summary>
    public const double MaxSearchRadiusMeters = 3.0;

    /// <summary>קטע קיר מועמד: שתי נקודות הקצה שלו (ב-2D למעשה - Z לא משמש בחישוב).</summary>
    public readonly record struct WallSegment(string Id, Point3D Start, Point3D End);

    /// <summary>
    /// מוצאת, מבין <paramref name="candidateWalls"/>, את הקיר הקרוב
    /// ביותר ל-<paramref name="point"/> (2D, X/Y בלבד) **שנמצא בטווח
    /// <see cref="MaxSearchRadiusMeters"/>**, ומחזירה את ה-Endpoint
    /// הקרוב יותר של אותו קיר כמיקום המתוקן (לא את נקודת ההיטל עצמה,
    /// שיכולה ליפול באמצע הקיר). גובה ה-Z נשמר מ-<paramref name="point"/>.
    /// </summary>
    /// <param name="point">המיקום המקורי (מיקום האסלה שממנה נגזר הקולטן).</param>
    /// <param name="candidateWalls">כל קטעי הקיר המועמדים (לא מסוננים מראש - הסינון קורה כאן).</param>
    /// <param name="originatingFixtureId">מזהה האסלה המקורית - לצורך הודעת שגיאה ברורה אם אין מועמד בטווח.</param>
    /// <exception cref="InvalidOperationException">
    /// אם אף קיר לא נמצא בטווח <see cref="MaxSearchRadiusMeters"/> -
    /// במפורש, בלי ליפול חזרה על הקיר הכי-פחות-רחוק כברירת מחדל שקטה.
    /// </exception>
    public static Point3D SnapToNearestWallEdge(
        Point3D point,
        IReadOnlyList<WallSegment> candidateWalls,
        string originatingFixtureId)
    {
        WallSegment? bestWall = null;
        Point3D bestProjection = default;
        double bestDistance = double.MaxValue;

        foreach (WallSegment wall in candidateWalls)
        {
            Point3D projection = ProjectOntoSegment(point, wall.Start, wall.End);
            double distance = GeometryUtils.Distance2D(point, projection);

            if (distance <= MaxSearchRadiusMeters && distance < bestDistance)
            {
                bestDistance = distance;
                bestWall = wall;
                bestProjection = projection;
            }
        }

        if (bestWall is null)
        {
            throw new InvalidOperationException(
                $"לא נמצא אף קיר בטווח {MaxSearchRadiusMeters:F1} מ' מהאסלה " +
                $"'{originatingFixtureId}' - לא ניתן לקבוע מיקום קולטן צמוד-קיר. " +
                "ראו docs/step6.md לגבי הבאג שהוביל לדרישה הזו.");
        }

        WallSegment wallSegment = bestWall.Value;
        double distanceToStart = GeometryUtils.Distance2D(bestProjection, wallSegment.Start);
        double distanceToEnd = GeometryUtils.Distance2D(bestProjection, wallSegment.End);

        Point3D nearerEndpoint = distanceToStart <= distanceToEnd ? wallSegment.Start : wallSegment.End;

        return new Point3D(nearerEndpoint.X, nearerEndpoint.Y, point.Z);
    }

    /// <summary>
    /// מוצאת את הנקודה הקרובה ביותר על **קטע** (לא קו אינסופי) שבין
    /// <paramref name="segmentStart"/> ל-<paramref name="segmentEnd"/>,
    /// ב-2D (X,Y בלבד) - היטל אורתוגונלי, קבוע (clamped) לגבולות הקטע.
    /// </summary>
    private static Point3D ProjectOntoSegment(Point3D point, Point3D segmentStart, Point3D segmentEnd)
    {
        double dx = segmentEnd.X - segmentStart.X;
        double dy = segmentEnd.Y - segmentStart.Y;
        double lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared < 1e-12)
        {
            // קטע באורך אפס (שתי הנקודות זהות) - אין מה להטיל עליו חוץ מעצמו.
            return segmentStart;
        }

        double t = (((point.X - segmentStart.X) * dx) + ((point.Y - segmentStart.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);

        return new Point3D(segmentStart.X + (t * dx), segmentStart.Y + (t * dy), point.Z);
    }
}
