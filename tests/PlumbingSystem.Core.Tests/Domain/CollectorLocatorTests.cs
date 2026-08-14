using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>בדיקות עבור <see cref="CollectorLocator.Locate"/>.</summary>
public class CollectorLocatorTests
{
    private const string ApartmentId = "apt-1";

    /// <summary>
    /// שלוש אסלות קרובות (הכל בטווח 4.0 מ') - קולטן יחיד מספיק. שירותי
    /// האורחים (A) ממוקם כך שהוא ה"מרכז" הגיאומטרי (סכום מרחקים 4,
    /// לעומת 6 מכל אסלה אחרת), כדי לוודא שהבחירה נובעת מהאלגוריתם
    /// (מיזעור סכום מרחקים) ולא רק מזה שהיא שירותי אורחים.
    /// </summary>
    [Fact]
    public void Locate_AllFixturesWithinRange_ReturnsSingleCollectorAtGuestBathroom()
    {
        var guestBathroom = new ToiletFixture("guest", new Point3D(0, 0, 0), ApartmentId, isGuestBathroom: true);
        var fixtureB = new ToiletFixture("b", new Point3D(2, 0, 0), ApartmentId, isGuestBathroom: false);
        var fixtureC = new ToiletFixture("c", new Point3D(-2, 0, 0), ApartmentId, isGuestBathroom: false);

        var apartment = new Apartment(ApartmentId, floorNumber: 2, new List<ToiletFixture> { guestBathroom, fixtureB, fixtureC });

        List<CollectorPoint> collectors = CollectorLocator.Locate(apartment);

        CollectorPoint collector = Assert.Single(collectors);
        Assert.Equal(guestBathroom.Location, collector.Location);
        Assert.Equal(3, collector.ConnectedFixtureIds.Count);
        Assert.Contains("guest", collector.ConnectedFixtureIds);
        Assert.Contains("b", collector.ConnectedFixtureIds);
        Assert.Contains("c", collector.ConnectedFixtureIds);
        Assert.Equal(ApartmentId, Assert.Single(collector.ConnectedApartmentIds));
        AssertAllFixturesCoveredWithinMaxDistance(apartment, collectors);
    }

    /// <summary>
    /// רגרסיה למקרה אמיתי (דירה 1131) שבו 3 אסלות "התכווצו" לקולטן
    /// יחיד אחרי תיקון יחידות המידה (שלב 6) - כאן עם שתי אסלות **קרובות
    /// לגבול** ה-4.0 מ' (3.5 ו-3.9, לא רק מרחקים קטנים כמו בבדיקות
    /// האחרות) כדי לוודא במפורש, לא רק להניח, שכל אסלה בפתרון-קולטן-יחיד
    /// באמת נמדדת ועומדת בטווח - לא רק "מספיק קרוב באופן כללי".
    /// </summary>
    [Fact]
    public void Locate_ThreeFixturesNearMaxDistanceBoundary_SingleCollectorCoversAll()
    {
        var guestBathroom = new ToiletFixture("guest", new Point3D(0, 0, 0), ApartmentId, isGuestBathroom: true);
        var fixtureB = new ToiletFixture("b", new Point3D(3.5, 0, 0), ApartmentId, isGuestBathroom: false);
        var fixtureC = new ToiletFixture("c", new Point3D(-3.9, 0, 0), ApartmentId, isGuestBathroom: false);

        var apartment = new Apartment(ApartmentId, floorNumber: 2, new List<ToiletFixture> { guestBathroom, fixtureB, fixtureC });

        List<CollectorPoint> collectors = CollectorLocator.Locate(apartment);

        CollectorPoint collector = Assert.Single(collectors);
        Assert.Equal(3, collector.ConnectedFixtureIds.Count);
        AssertAllFixturesCoveredWithinMaxDistance(apartment, collectors);
    }

    /// <summary>
    /// אסלה אחת (C) רחוקה מדי מכל אחת מהאחרות (מעל 4.0 מ') - קולטן יחיד
    /// לא יכול לכסות את כולן, ונדרשים 2 קולטנים. בודקת מספר קולטנים
    /// וכיסוי תקין (כל אסלה בטווח מהקולטן שלה), לא את הזהות המדויקת של
    /// הזוג הנבחר (יש כאן יותר מפתרון-שני-קולטנים תקין אחד).
    /// </summary>
    [Fact]
    public void Locate_OneFixtureBeyondMaxDistance_ReturnsTwoCollectors()
    {
        var guestBathroom = new ToiletFixture("guest", new Point3D(0, 0, 0), ApartmentId, isGuestBathroom: true);
        var fixtureB = new ToiletFixture("b", new Point3D(1, 0, 0), ApartmentId, isGuestBathroom: false);
        var fixtureC = new ToiletFixture("c", new Point3D(5.5, 0, 0), ApartmentId, isGuestBathroom: false);

        var apartment = new Apartment(ApartmentId, floorNumber: 2, new List<ToiletFixture> { guestBathroom, fixtureB, fixtureC });

        List<CollectorPoint> collectors = CollectorLocator.Locate(apartment);

        Assert.Equal(2, collectors.Count);
        AssertAllFixturesCoveredWithinMaxDistance(apartment, collectors);
    }

    /// <summary>
    /// שלוש אסלות בשורה (מרחקים 1,1,2) - כל אחת לבד מכסה את כל השאר
    /// (בטווח), אבל סכום המרחקים שונה בין המועמדים: B (האמצעית) נותנת
    /// סכום 2, לעומת 3 מ-A או מ-C. מוודאת שנבחר המועמד עם הסכום הקטן
    /// ביותר (B) - גם כשהוא **לא** שירותי האורחים (A) - זה בדיוק הבדיקה
    /// המפורשת שביקש המשתמש לסעיף 6 (מיזעור סכום מרחקים, לא בחירה
    /// שרירותית מבין אפשרויות תקינות באותו גודל).
    /// </summary>
    [Fact]
    public void Locate_MultipleEquallySizedSolutions_PicksMinimumTotalDistance()
    {
        var guestBathroom = new ToiletFixture("a", new Point3D(0, 0, 0), ApartmentId, isGuestBathroom: true);
        var fixtureB = new ToiletFixture("b", new Point3D(1, 0, 0), ApartmentId, isGuestBathroom: false);
        var fixtureC = new ToiletFixture("c", new Point3D(2, 0, 0), ApartmentId, isGuestBathroom: false);

        var apartment = new Apartment(ApartmentId, floorNumber: 2, new List<ToiletFixture> { guestBathroom, fixtureB, fixtureC });

        List<CollectorPoint> collectors = CollectorLocator.Locate(apartment);

        CollectorPoint collector = Assert.Single(collectors);
        Assert.Equal(fixtureB.Location, collector.Location);
        Assert.Equal(3, collector.ConnectedFixtureIds.Count);
        AssertAllFixturesCoveredWithinMaxDistance(apartment, collectors);
    }

    /// <summary>
    /// שלושה אשכולות אסלות רחוקים זה מזה (מעל 4.0 מ' בין אשכולות, מתחת
    /// לזה בתוך כל אשכול) - מספר הקולטנים אמור לגדול בהתאם למספר
    /// האשכולות (3), לא רק ל"יש חריגה" (בניגוד לבדיקה עם קולטן חורג
    /// בודד שנותנת 2).
    /// </summary>
    [Fact]
    public void Locate_MultipleDistantClusters_ReturnsCollectorPerCluster()
    {
        var fixtures = new List<ToiletFixture>
        {
            new("guest", new Point3D(0, 0, 0), ApartmentId, isGuestBathroom: true),
            new("cluster1-b", new Point3D(1, 0, 0), ApartmentId, isGuestBathroom: false),
            new("cluster2-a", new Point3D(10, 0, 0), ApartmentId, isGuestBathroom: false),
            new("cluster2-b", new Point3D(11, 0, 0), ApartmentId, isGuestBathroom: false),
            new("cluster3-a", new Point3D(20, 0, 0), ApartmentId, isGuestBathroom: false),
            new("cluster3-b", new Point3D(21, 0, 0), ApartmentId, isGuestBathroom: false),
        };

        var apartment = new Apartment(ApartmentId, floorNumber: 2, fixtures);

        List<CollectorPoint> collectors = CollectorLocator.Locate(apartment);

        Assert.Equal(3, collectors.Count);
        AssertAllFixturesCoveredWithinMaxDistance(apartment, collectors);
        Assert.Equal(6, collectors.Sum(c => c.ConnectedFixtureIds.Count));
    }

    /// <summary>דירה בלי אף אסלה עם IsGuestBathroom=true - אין נקודת התחלה, זורק שגיאה ברורה במקום לנחש.</summary>
    [Fact]
    public void Locate_NoGuestBathroom_Throws()
    {
        var fixtures = new List<ToiletFixture>
        {
            new("a", new Point3D(0, 0, 0), ApartmentId, isGuestBathroom: false),
            new("b", new Point3D(1, 0, 0), ApartmentId, isGuestBathroom: false),
        };

        var apartment = new Apartment(ApartmentId, floorNumber: 2, fixtures);

        Assert.Throws<InvalidOperationException>(() => CollectorLocator.Locate(apartment));
    }

    private static void AssertAllFixturesCoveredWithinMaxDistance(Apartment apartment, List<CollectorPoint> collectors)
    {
        Dictionary<string, ToiletFixture> fixturesById = apartment.Fixtures.ToDictionary(f => f.Id);

        foreach (CollectorPoint collector in collectors)
        {
            foreach (string fixtureId in collector.ConnectedFixtureIds)
            {
                double distance = PlumbingSystem.Core.Geometry.GeometryUtils.Distance2D(
                    fixturesById[fixtureId].Location,
                    collector.Location);

                Assert.True(
                    distance <= CollectorLocator.MaxDistanceMeters,
                    $"Fixture '{fixtureId}' is {distance:F2}m from its assigned collector - exceeds MaxDistanceMeters.");
            }
        }

        int totalAssigned = collectors.Sum(c => c.ConnectedFixtureIds.Count);
        Assert.Equal(apartment.Fixtures.Count, totalAssigned);
    }
}
