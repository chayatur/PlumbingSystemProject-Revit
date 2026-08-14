using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>
/// בדיקות עבור <see cref="WallEdgeSnapper.SnapToNearestWallEdge"/> - נכתבו
/// בעקבות באג שהתגלה ויזואלית: בלי סינון-רדיוס, "הקיר הקרוב ביותר
/// מתוך הרשימה" עדיין יכול להיות רחוק מאוד אם הרשימה עצמה (מ-Room
/// שמוגדר ברמת דירה שלמה) כוללת קירות לא-קשורים. הבדיקות שזורקות
/// (<c>..._Throws...</c>) גם מוכיחות במפורש את הדרישה ש"אסלה בלי קיר
/// בטווח" נכשלת בקול רם (Exception אמיתי, לא ערך-ברירת-מחדל שקט) -
/// זו הערובה היחידה שניתנת לבדיקה אוטומטית לכך ש-
/// <c>CollectorPlacementService</c> (Revit, לא ניתן ל-unit test) לא
/// יכול "לבלוע" כשל כזה בשקט: אם הפונקציה הזו זורקת, אין לה שום ערך
/// להחזיר שהקוד הקורא יכול להתעלם ממנו.
/// </summary>
public class WallEdgeSnapperTests
{
    /// <summary>
    /// קיר יחיד בטווח - התוצאה היא ה-Endpoint הקרוב יותר לנקודת ההיטל,
    /// לא נקודת ההיטל עצמה (שנופלת באמצע הקטע כאן).
    /// </summary>
    [Fact]
    public void SnapToNearestWallEdge_WallWithinRadius_SnapsToNearerEndpoint()
    {
        var point = new Point3D(1.5, 0.5, 3);
        var walls = new List<WallEdgeSnapper.WallSegment>
        {
            new("wall-1", new Point3D(0, 0, 0), new Point3D(0, 3, 0)),
        };

        Point3D result = WallEdgeSnapper.SnapToNearestWallEdge(point, walls, "fixture-1");

        // היטל על wall-1 = (0, 0.5); מרחק לקצה (0,0)=0.5, לקצה (0,3)=2.5 -> (0,0) קרוב יותר.
        Assert.Equal(new Point3D(0, 0, 3), result);
    }

    /// <summary>
    /// קיר קרוב (בטווח) וקיר רחוק (מחוץ לטווח, אבל היה "המרחק המינימלי
    /// מתוך הרשימה" אילו לא היה סינון-רדיוס בכלל) - מוודאת שהתוצאה
    /// מבוססת אך ורק על הקיר הקרוב, בלי שהרחוק ישפיע.
    /// </summary>
    [Fact]
    public void SnapToNearestWallEdge_IgnoresWallsBeyondSearchRadius()
    {
        var point = new Point3D(0, 0, 0);
        var walls = new List<WallEdgeSnapper.WallSegment>
        {
            new("near", new Point3D(1, 0, 0), new Point3D(1, 5, 0)),   // היטל (1,0), מרחק 1.0 - בטווח
            new("far", new Point3D(9, 0, 0), new Point3D(9, 5, 0)),    // היטל (9,0), מרחק 9.0 - מחוץ לטווח
        };

        Point3D result = WallEdgeSnapper.SnapToNearestWallEdge(point, walls, "fixture-1");

        Assert.Equal(new Point3D(1, 0, 0), result);
    }

    /// <summary>
    /// אף קיר לא בטווח - זורק InvalidOperationException עם ElementId
    /// האסלה בהודעה, ולא נופל חזרה על הקיר הכי-פחות-רחוק כברירת מחדל.
    /// </summary>
    [Fact]
    public void SnapToNearestWallEdge_NoWallWithinRadius_ThrowsWithFixtureId()
    {
        var point = new Point3D(0, 0, 0);
        var walls = new List<WallEdgeSnapper.WallSegment>
        {
            new("far-1", new Point3D(9, 0, 0), new Point3D(9, 5, 0)),
            new("far-2", new Point3D(-9, 0, 0), new Point3D(-9, 5, 0)),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => WallEdgeSnapper.SnapToNearestWallEdge(point, walls, "fixture-42"));

        Assert.Contains("fixture-42", exception.Message);
    }

    /// <summary>רשימת קירות מועמדים ריקה - אותה התנהגות כמו "אף קיר בטווח" (זורק, לא קורס).</summary>
    [Fact]
    public void SnapToNearestWallEdge_NoCandidateWalls_Throws()
    {
        var point = new Point3D(0, 0, 0);
        var walls = new List<WallEdgeSnapper.WallSegment>();

        Assert.Throws<InvalidOperationException>(
            () => WallEdgeSnapper.SnapToNearestWallEdge(point, walls, "fixture-1"));
    }
}
