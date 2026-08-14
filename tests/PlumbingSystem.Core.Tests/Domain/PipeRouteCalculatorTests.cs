using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>
/// בדיקות עבור <see cref="PipeRouteCalculator.Calculate"/> - מכסות את
/// המקרה התקין (מרחק אופקי סביר, שיפוע-בפועל בתוך 1.5%-2.0%) ואת מקרה
/// השגיאה (מרחק אופקי אפסי, שהופך את חישוב השיפוע למנוון/מחוץ לטווח).
/// </summary>
public class PipeRouteCalculatorTests
{
    [Fact]
    public void Calculate_ReasonableHorizontalDistance_ReturnsValidSegmentWithinSlopeRange()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 4, 10), new List<string> { "apt-1" });

        PipeSegment segment = PipeRouteCalculator.Calculate(fixture, collector);

        // מרחק אופקי = sqrt(3^2+4^2) = 5.
        Assert.Equal(new Point3D(0, 0, 10), segment.StartPoint);
        Assert.Equal(3, segment.EndPoint.X);
        Assert.Equal(4, segment.EndPoint.Y);
        Assert.Equal(PipeRouteCalculator.PipeDiameterMm, segment.DiameterMm);
        Assert.InRange(segment.SlopePercent, PipeRouteCalculator.MinSlopePercent, PipeRouteCalculator.MaxSlopePercent);
        Assert.Equal(PipeRouteCalculator.DefaultSlopePercent, segment.SlopePercent, precision: 6);

        // Z של הסיום צריך להיות נמוך מ-Z ההתחלה (ירידה, לא עלייה) בדיוק לפי השיפוע.
        double expectedZDrop = 5.0 * (PipeRouteCalculator.DefaultSlopePercent / 100.0);
        Assert.Equal(10 - expectedZDrop, segment.EndPoint.Z, precision: 9);
    }

    [Fact]
    public void Calculate_FixtureAndCollectorAtSameLocation_ThrowsWithBothIds()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(5, 5, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(5, 5, 10), new List<string> { "apt-1" });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PipeRouteCalculator.Calculate(fixture, collector));

        Assert.Contains("toilet-1", exception.Message);
        Assert.Contains("COL-toilet-1", exception.Message);
    }

    [Fact]
    public void Calculate_NullFixture_ThrowsArgumentNullException()
    {
        var collector = new CollectorPoint("COL-1", new Point3D(0, 0, 0), new List<string> { "apt-1" });

        Assert.Throws<ArgumentNullException>(() => PipeRouteCalculator.Calculate(null!, collector));
    }

    [Fact]
    public void Calculate_NullCollector_ThrowsArgumentNullException()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 0), "apt-1", isGuestBathroom: true);

        Assert.Throws<ArgumentNullException>(() => PipeRouteCalculator.Calculate(fixture, null!));
    }

    /// <summary>
    /// מוודאת ש-<see cref="PipeRouteCalculator.Calculate"/> (המסלול הישר,
    /// ללא חסימה) לא הושפע כלל מהוספת <see cref="PipeRouteCalculator.CalculateDetour"/> -
    /// עדיין מחזיר בדיוק מקטע ישר יחיד, לא מסלול-עוקף - כפי שנדרש
    /// במפורש ("מקרה בלי חסימה... קו-ישר עדיין נשאר קו-ישר, לא משתנה").
    /// </summary>
    [Fact]
    public void Calculate_NoObstruction_StillReturnsSingleStraightSegment()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(10, 0, 10), new List<string> { "apt-1" });

        PipeSegment segment = PipeRouteCalculator.Calculate(fixture, collector);

        Assert.Equal(fixture.Location, segment.StartPoint);
        Assert.Equal(collector.Location.X, segment.EndPoint.X);
        Assert.Equal(collector.Location.Y, segment.EndPoint.Y);
        Assert.Equal($"PIPE-{fixture.Id}-{collector.Id}", segment.Id);
    }

    /// <summary>
    /// מקרה עם חסימה: קיר שחוצה את המסלול הישר בין אסלה לקולטן (מרחק
    /// אופקי ריאלי, 3 מ' - בתוך המגבלה הפיזית). מוודאת שמתקבלים בדיוק
    /// שני מקטעים רציפים (סוף המקטע הראשון = תחילת השני), ששניהם
    /// בטווח השיפוע התקין, ושזווית-החיבור ביניהם **בדיוק** 45° (לא
    /// "בסביבות" - הבנייה מבטיחה את זה מתמטית, ראו התיעוד ב-
    /// <see cref="PipeRouteCalculator.CalculateDetour"/> לגבי המקרה
    /// הקודם שנתן 67.01° בטעות).
    /// </summary>
    [Fact]
    public void CalculateDetour_WithBlockingWall_ReturnsTwoConnectedSegmentsWithExactly45DegreeAngle()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1.5, 0.5, 0), new Point3D(1.5, 5, 0));

        IReadOnlyList<PipeSegment> route = PipeRouteCalculator.CalculateDetour(fixture, collector, blockingWall);

        Assert.Equal(2, route.Count);
        PipeSegment leg1 = route[0];
        PipeSegment leg2 = route[1];

        // רציפות: סוף מקטע 1 = תחילת מקטע 2 (אותו waypoint בדיוק).
        Assert.Equal(leg1.EndPoint, leg2.StartPoint);
        Assert.Equal(fixture.Location, leg1.StartPoint);
        Assert.Equal(collector.Location.X, leg2.EndPoint.X);
        Assert.Equal(collector.Location.Y, leg2.EndPoint.Y);

        Assert.Equal(PipeRouteCalculator.PipeDiameterMm, leg1.DiameterMm);
        Assert.Equal(PipeRouteCalculator.PipeDiameterMm, leg2.DiameterMm);
        Assert.InRange(leg1.SlopePercent, PipeRouteCalculator.MinSlopePercent, PipeRouteCalculator.MaxSlopePercent);
        Assert.InRange(leg2.SlopePercent, PipeRouteCalculator.MinSlopePercent, PipeRouteCalculator.MaxSlopePercent);

        // אורך המסלול הכולל (סכום שני המקטעים) לא עובר את המגבלה הפיזית.
        double leg1Horizontal = GeometryUtils.Distance2D(leg1.StartPoint, leg1.EndPoint);
        double leg2Horizontal = GeometryUtils.Distance2D(leg2.StartPoint, leg2.EndPoint);
        Assert.True(leg1Horizontal + leg2Horizontal <= CollectorLocator.MaxDistanceMeters);

        double leg1DirX = leg1.EndPoint.X - leg1.StartPoint.X;
        double leg1DirY = leg1.EndPoint.Y - leg1.StartPoint.Y;
        double leg2DirX = leg2.EndPoint.X - leg2.StartPoint.X;
        double leg2DirY = leg2.EndPoint.Y - leg2.StartPoint.Y;

        double leg1Length = Math.Sqrt((leg1DirX * leg1DirX) + (leg1DirY * leg1DirY));
        double leg2Length = Math.Sqrt((leg2DirX * leg2DirX) + (leg2DirY * leg2DirY));
        double cosAngle = ((leg1DirX * leg2DirX) + (leg1DirY * leg2DirY)) / (leg1Length * leg2Length);
        double angleDegrees = Math.Acos(Math.Clamp(cosAngle, -1.0, 1.0)) * (180.0 / Math.PI);

        // מהבנייה עצמה - לא "בטווח סביר", אלא כמעט בדיוק 45° (עד דיוק floating-point).
        Assert.Equal(45.0, angleDegrees, precision: 6);
        Assert.InRange(angleDegrees, PipeRouteCalculator.MinDetourAngleDegrees, PipeRouteCalculator.MaxDetourAngleDegrees);

        // Ids רציפים על בסיס אותו route id משותף.
        string expectedRouteId = $"PIPE-{fixture.Id}-{collector.Id}";
        Assert.Equal($"{expectedRouteId}-leg1", leg1.Id);
        Assert.Equal($"{expectedRouteId}-leg2", leg2.Id);
    }

    /// <summary>
    /// <c>useOppositeSide=true</c> חייב לתת waypoint שונה (הצד השני,
    /// תמונת-ראי) - לא את אותה תוצאה כמו ברירת-המחדל - אבל עדיין
    /// בזווית 45° מדויקת. זה מה ש-PlumbingSystem.Revit צריך: אפשרות
    /// אמיתית לנסות צד שני כשהראשון עדיין חוסם קיר, לא רק "לנחש" שאין
    /// פתרון.
    /// </summary>
    [Fact]
    public void CalculateDetour_UseOppositeSide_ReturnsMirroredWaypointWithExactly45DegreeAngle()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1.5, 0.5, 0), new Point3D(1.5, 5, 0));

        IReadOnlyList<PipeSegment> preferredSide = PipeRouteCalculator.CalculateDetour(fixture, collector, blockingWall);
        IReadOnlyList<PipeSegment> oppositeSide = PipeRouteCalculator.CalculateDetour(
            fixture, collector, blockingWall, useOppositeSide: true);

        // תמונת-ראי: אותו X של ה-waypoint, אבל Y הפוך (הצד השני של הקו הישר).
        Assert.Equal(preferredSide[0].EndPoint.X, oppositeSide[0].EndPoint.X, precision: 6);
        Assert.Equal(-preferredSide[0].EndPoint.Y, oppositeSide[0].EndPoint.Y, precision: 6);

        double leg1DirX = oppositeSide[0].EndPoint.X - oppositeSide[0].StartPoint.X;
        double leg1DirY = oppositeSide[0].EndPoint.Y - oppositeSide[0].StartPoint.Y;
        double leg2DirX = oppositeSide[1].EndPoint.X - oppositeSide[1].StartPoint.X;
        double leg2DirY = oppositeSide[1].EndPoint.Y - oppositeSide[1].StartPoint.Y;
        double leg1Length = Math.Sqrt((leg1DirX * leg1DirX) + (leg1DirY * leg1DirY));
        double leg2Length = Math.Sqrt((leg2DirX * leg2DirX) + (leg2DirY * leg2DirY));
        double cosAngle = ((leg1DirX * leg2DirX) + (leg1DirY * leg2DirY)) / (leg1Length * leg2Length);
        double angleDegrees = Math.Acos(Math.Clamp(cosAngle, -1.0, 1.0)) * (180.0 / Math.PI);

        Assert.Equal(45.0, angleDegrees, precision: 6);
    }

    /// <summary>
    /// <c>useWallDirectionAsReference=true</c> עם קיר שאינו מקביל/מאונך
    /// ל-D (הקו הישר) - חייב לתת waypoint **שונה** מ-ברירת-המחדל (D
    /// כייחוס), אבל עדיין בזווית **בדיוק** 45° ועדיין מגיע בדיוק
    /// לקואורדינטות הקולטן - מוודא שהחלופה הזו (זווית ביחס לכיוון-
    /// הקיר, לא לקו המקורי) עובדת נכון גיאומטרית, לא רק "לא זורקת".
    /// </summary>
    [Fact]
    public void CalculateDetour_UseWallDirectionAsReference_ProducesDifferentButStillValidWaypoint()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 0, 10), new List<string> { "apt-1" });
        // קיר "אלכסוני" עדין (~11° מ-D) - לא מקביל ל-D=(1,0) ולא מאונך לו,
        // אבל גם לא רחוק מדי (זווית-קיר גדולה מדי הופכת את ההצטלבות
        // ל"אחורה" - ראו התיעוד על מגבלת useWallDirectionAsReference).
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1, 0.3, 0), new Point3D(2, 0.5, 0));

        IReadOnlyList<PipeSegment> dReferenceRoute = PipeRouteCalculator.CalculateDetour(fixture, collector, blockingWall);
        IReadOnlyList<PipeSegment> wallReferenceRoute = PipeRouteCalculator.CalculateDetour(
            fixture, collector, blockingWall, useWallDirectionAsReference: true);

        // ה-waypoint שונה בפועל בין שתי הגישות (לא במקרה זהה).
        bool waypointsDiffer = Math.Abs(dReferenceRoute[0].EndPoint.X - wallReferenceRoute[0].EndPoint.X) > 1e-6
            || Math.Abs(dReferenceRoute[0].EndPoint.Y - wallReferenceRoute[0].EndPoint.Y) > 1e-6;
        Assert.True(waypointsDiffer, "ציפינו ל-waypoint שונה כשה-reference הוא כיוון-הקיר, לא D.");

        // עדיין מגיע בדיוק לקולטן, ועדיין זווית בדיוק 45°.
        Assert.Equal(collector.Location.X, wallReferenceRoute[1].EndPoint.X, precision: 6);
        Assert.Equal(collector.Location.Y, wallReferenceRoute[1].EndPoint.Y, precision: 6);

        double angleDegrees = AngleBetweenDegrees(wallReferenceRoute[0], wallReferenceRoute[1]);
        Assert.Equal(45.0, angleDegrees, precision: 6);
    }

    /// <summary>אותה בדיקה כמו לעיל, עבור מסלול "Y מדורג" - waypoint שונה, אבל עדיין שני בנים בדיוק 22.5° ועדיין מגיע לקולטן.</summary>
    [Fact]
    public void CalculateStaggeredDetour_UseWallDirectionAsReference_ProducesDifferentButStillValidRoute()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1, 0.3, 0), new Point3D(2, 0.5, 0));

        IReadOnlyList<PipeSegment> dReferenceRoute = PipeRouteCalculator.CalculateStaggeredDetour(
            fixture, collector, blockingWall, crossoverLengthMeters: 0.5);
        IReadOnlyList<PipeSegment> wallReferenceRoute = PipeRouteCalculator.CalculateStaggeredDetour(
            fixture, collector, blockingWall, crossoverLengthMeters: 0.5, useWallDirectionAsReference: true);

        bool waypointsDiffer = Math.Abs(dReferenceRoute[0].EndPoint.X - wallReferenceRoute[0].EndPoint.X) > 1e-6
            || Math.Abs(dReferenceRoute[0].EndPoint.Y - wallReferenceRoute[0].EndPoint.Y) > 1e-6;
        Assert.True(waypointsDiffer, "ציפינו ל-waypoint1 שונה כשה-reference הוא כיוון-הקיר, לא D.");

        Assert.Equal(collector.Location.X, wallReferenceRoute[2].EndPoint.X, precision: 6);
        Assert.Equal(collector.Location.Y, wallReferenceRoute[2].EndPoint.Y, precision: 6);

        double bend1 = AngleBetweenDegrees(wallReferenceRoute[0], wallReferenceRoute[1]);
        double bend2 = AngleBetweenDegrees(wallReferenceRoute[1], wallReferenceRoute[2]);
        Assert.Equal(PipeRouteCalculator.StaggeredDetourBendAngleDegrees, bend1, precision: 6);
        Assert.Equal(PipeRouteCalculator.StaggeredDetourBendAngleDegrees, bend2, precision: 6);
    }

    /// <summary>
    /// מרחק אופקי ישר גדול (10 מ') - מסלול-עוקף (שתמיד ארוך מהישר)
    /// חורג בוודאות מהמגבלה הפיזית הקשיחה (4.0 מ', עובי-מילוי) - חייב
    /// לזרוק, לא ליצור צינור בשקט תוך הפרת הכלל.
    /// </summary>
    [Fact]
    public void CalculateDetour_TotalRouteLengthExceedsMaxDistance_Throws()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(10, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(5, 0.5, 0), new Point3D(5, 5, 0));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PipeRouteCalculator.CalculateDetour(fixture, collector, blockingWall));

        Assert.Contains("toilet-1", exception.Message);
        Assert.Contains(CollectorLocator.MaxDistanceMeters.ToString("F1"), exception.Message);
    }

    [Fact]
    public void CalculateDetour_FixtureAndCollectorAtSameLocation_Throws()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(5, 5, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(5, 5, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment("wall-1", new Point3D(0, 0, 0), new Point3D(0, 1, 0));

        Assert.Throws<InvalidOperationException>(
            () => PipeRouteCalculator.CalculateDetour(fixture, collector, blockingWall));
    }

    [Fact]
    public void CalculateDetour_NullFixture_ThrowsArgumentNullException()
    {
        var collector = new CollectorPoint("COL-1", new Point3D(10, 0, 0), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment("wall-1", new Point3D(5, 0.5, 0), new Point3D(5, 5, 0));

        Assert.Throws<ArgumentNullException>(() => PipeRouteCalculator.CalculateDetour(null!, collector, blockingWall));
    }

    [Fact]
    public void CalculateDetour_NullCollector_ThrowsArgumentNullException()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 0), "apt-1", isGuestBathroom: true);
        var blockingWall = new WallEdgeSnapper.WallSegment("wall-1", new Point3D(5, 0.5, 0), new Point3D(5, 5, 0));

        Assert.Throws<ArgumentNullException>(() => PipeRouteCalculator.CalculateDetour(fixture, null!, blockingWall));
    }

    /// <summary>
    /// מקרה תקין: 3 מקטעים רציפים (אסלה→waypoint1→waypoint2→קולטן),
    /// כל אחד מהבנים (leg1-crossover, crossover-leg3) הוא **בדיוק**
    /// <see cref="PipeRouteCalculator.StaggeredDetourBendAngleDegrees"/>
    /// (22.5°) - לא קירוב - ומגיע **בדיוק** לקואורדינטות הקולטן.
    /// </summary>
    [Fact]
    public void CalculateStaggeredDetour_ValidCrossoverLength_ReturnsThreeConnectedSegmentsWithExactBendAngles()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1.5, 0.5, 0), new Point3D(1.5, 5, 0));

        IReadOnlyList<PipeSegment> route = PipeRouteCalculator.CalculateStaggeredDetour(
            fixture, collector, blockingWall, crossoverLengthMeters: 0.5);

        Assert.Equal(3, route.Count);
        PipeSegment leg1 = route[0];
        PipeSegment crossover = route[1];
        PipeSegment leg3 = route[2];

        // רציפות מלאה.
        Assert.Equal(fixture.Location, leg1.StartPoint);
        Assert.Equal(leg1.EndPoint, crossover.StartPoint);
        Assert.Equal(crossover.EndPoint, leg3.StartPoint);
        Assert.Equal(collector.Location.X, leg3.EndPoint.X, precision: 6);
        Assert.Equal(collector.Location.Y, leg3.EndPoint.Y, precision: 6);

        // אורך מקטע-הביניים בדיוק כפי שהתבקש.
        double crossoverLength = GeometryUtils.Distance2D(crossover.StartPoint, crossover.EndPoint);
        Assert.Equal(0.5, crossoverLength, precision: 6);

        // אורך המסלול הכולל בתוך המגבלה הפיזית.
        double leg1Length = GeometryUtils.Distance2D(leg1.StartPoint, leg1.EndPoint);
        double leg3Length = GeometryUtils.Distance2D(leg3.StartPoint, leg3.EndPoint);
        Assert.True(leg1Length + crossoverLength + leg3Length <= CollectorLocator.MaxDistanceMeters);

        foreach (PipeSegment segment in route)
        {
            Assert.Equal(PipeRouteCalculator.PipeDiameterMm, segment.DiameterMm);
            Assert.InRange(segment.SlopePercent, PipeRouteCalculator.MinSlopePercent, PipeRouteCalculator.MaxSlopePercent);
        }

        // שני הבנים (leg1->crossover, crossover->leg3) הם בדיוק β=22.5°.
        double bend1 = AngleBetweenDegrees(leg1, crossover);
        double bend2 = AngleBetweenDegrees(crossover, leg3);
        Assert.Equal(PipeRouteCalculator.StaggeredDetourBendAngleDegrees, bend1, precision: 6);
        Assert.Equal(PipeRouteCalculator.StaggeredDetourBendAngleDegrees, bend2, precision: 6);
    }

    /// <summary>
    /// מרחק אופקי גדול (3.9 מ', קרוב למגבלה) + מקטע-ביניים קטן - סך
    /// המסלול (שכולל את "מחיר" הזווית, גדול תמיד מהמרחק הישר) חורג
    /// מ-4.0 מ' - חייב לזרוק, לא ליצור צינור בשקט תוך הפרת הכלל.
    /// </summary>
    [Fact]
    public void CalculateStaggeredDetour_TotalRouteLengthExceedsMaxDistance_Throws()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3.9, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1.95, 0.5, 0), new Point3D(1.95, 5, 0));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PipeRouteCalculator.CalculateStaggeredDetour(
                fixture, collector, blockingWall, crossoverLengthMeters: 0.01));

        Assert.Contains("toilet-1", exception.Message);
        Assert.Contains(CollectorLocator.MaxDistanceMeters.ToString("F1"), exception.Message);
    }

    /// <summary>אורך מקטע-הביניים גדול-או-שווה למרחק האופקי הכולל - אין מרחק נותר לשני מקטעי-הקצה.</summary>
    [Fact]
    public void CalculateStaggeredDetour_CrossoverLengthExceedsTotalDistance_Throws()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 10), "apt-1", isGuestBathroom: true);
        var collector = new CollectorPoint("COL-toilet-1", new Point3D(3, 0, 10), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment(
            "wall-1", new Point3D(1.5, 0.5, 0), new Point3D(1.5, 5, 0));

        Assert.Throws<InvalidOperationException>(
            () => PipeRouteCalculator.CalculateStaggeredDetour(
                fixture, collector, blockingWall, crossoverLengthMeters: 3.5));
    }

    [Fact]
    public void CalculateStaggeredDetour_NullFixture_ThrowsArgumentNullException()
    {
        var collector = new CollectorPoint("COL-1", new Point3D(3, 0, 0), new List<string> { "apt-1" });
        var blockingWall = new WallEdgeSnapper.WallSegment("wall-1", new Point3D(1.5, 0.5, 0), new Point3D(1.5, 5, 0));

        Assert.Throws<ArgumentNullException>(() => PipeRouteCalculator.CalculateStaggeredDetour(
            null!, collector, blockingWall, crossoverLengthMeters: 0.5));
    }

    [Fact]
    public void CalculateStaggeredDetour_NullCollector_ThrowsArgumentNullException()
    {
        var fixture = new ToiletFixture("toilet-1", new Point3D(0, 0, 0), "apt-1", isGuestBathroom: true);
        var blockingWall = new WallEdgeSnapper.WallSegment("wall-1", new Point3D(1.5, 0.5, 0), new Point3D(1.5, 5, 0));

        Assert.Throws<ArgumentNullException>(() => PipeRouteCalculator.CalculateStaggeredDetour(
            fixture, null!, blockingWall, crossoverLengthMeters: 0.5));
    }

    /// <summary>זווית (מעלות) בין כיווני שני מקטעים עוקבים - עוזר-בדיקה בלבד.</summary>
    private static double AngleBetweenDegrees(PipeSegment first, PipeSegment second)
    {
        double firstDirX = first.EndPoint.X - first.StartPoint.X;
        double firstDirY = first.EndPoint.Y - first.StartPoint.Y;
        double secondDirX = second.EndPoint.X - second.StartPoint.X;
        double secondDirY = second.EndPoint.Y - second.StartPoint.Y;

        double firstLength = Math.Sqrt((firstDirX * firstDirX) + (firstDirY * firstDirY));
        double secondLength = Math.Sqrt((secondDirX * secondDirX) + (secondDirY * secondDirY));

        double cosAngle = ((firstDirX * secondDirX) + (firstDirY * secondDirY)) / (firstLength * secondLength);
        return Math.Acos(Math.Clamp(cosAngle, -1.0, 1.0)) * (180.0 / Math.PI);
    }
}