using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Models;

/// <summary>בדיקות בנייה וולידציית שדות בסיסית של <see cref="CollectorPoint"/>.</summary>
public class CollectorPointTests
{
    /// <summary>בנייה עם דירה אחת (קולטן דירתי) שומרת את כל השדות כמו שסופקו.</summary>
    [Fact]
    public void Constructor_SingleApartment_SetsAllProperties()
    {
        var location = new Point3D(5, 5, 0);
        var apartmentIds = new List<string> { "apt-1" };
        var fixtureIds = new List<string> { "fixture-1" };

        var collector = new CollectorPoint("collector-1", location, apartmentIds, fixtureIds);

        Assert.Equal("collector-1", collector.Id);
        Assert.Equal(location, collector.Location);
        Assert.Same(apartmentIds, collector.ConnectedApartmentIds);
        Assert.Same(fixtureIds, collector.ConnectedFixtureIds);
    }

    /// <summary>קולטן יכול לשרת כמה דירות בו-זמנית (קולטן משותף לקומה).</summary>
    [Fact]
    public void Constructor_MultipleApartments_IsAllowed()
    {
        var apartmentIds = new List<string> { "apt-1", "apt-2", "apt-3" };

        var collector = new CollectorPoint("collector-shared", default, apartmentIds);

        Assert.Equal(3, collector.ConnectedApartmentIds.Count);
    }

    /// <summary>כשלא מסופקים מזהי אסלות, הקולטן מקבל רשימה ריקה (לא null).</summary>
    [Fact]
    public void Constructor_NoFixtureIds_DefaultsToEmptyList()
    {
        var collector = new CollectorPoint("collector-1", default, new List<string> { "apt-1" });

        Assert.NotNull(collector.ConnectedFixtureIds);
        Assert.Empty(collector.ConnectedFixtureIds);
    }

    /// <summary>Id ריק (או רק רווחים) זורק ArgumentException.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyId_Throws(string emptyId)
    {
        Assert.Throws<ArgumentException>(
            () => new CollectorPoint(emptyId, default, new List<string> { "apt-1" }));
    }

    /// <summary>
    /// קולטן שלא מחובר לאף דירה הוא שגיאת נתונים בסיסית (לא מצב הנדסי
    /// אפשרי) - זורק ArgumentException בין אם הרשימה null ובין אם ריקה.
    /// </summary>
    [Fact]
    public void Constructor_NullApartmentIds_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CollectorPoint("collector-1", default, null!));
    }

    /// <summary>ראו תיעוד <see cref="Constructor_NullApartmentIds_Throws"/>.</summary>
    [Fact]
    public void Constructor_EmptyApartmentIds_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new CollectorPoint("collector-1", default, new List<string>()));
    }
}
