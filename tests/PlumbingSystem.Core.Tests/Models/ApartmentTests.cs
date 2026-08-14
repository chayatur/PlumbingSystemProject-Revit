using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Models;

/// <summary>בדיקות בנייה וולידציית שדות בסיסית של <see cref="Apartment"/>.</summary>
public class ApartmentTests
{
    /// <summary>בנייה עם רשימת אסלות מפורשת שומרת אותה כמו שסופקה.</summary>
    [Fact]
    public void Constructor_WithFixtures_SetsAllProperties()
    {
        var fixtures = new List<ToiletFixture>
        {
            new("fixture-1", default, "apt-1", isGuestBathroom: true),
        };

        var apartment = new Apartment("apt-1", floorNumber: 3, fixtures);

        Assert.Equal("apt-1", apartment.Id);
        Assert.Equal(3, apartment.FloorNumber);
        Assert.Same(fixtures, apartment.Fixtures);
    }

    /// <summary>כשלא מסופקת רשימת אסלות, הדירה מקבלת רשימה ריקה (לא null).</summary>
    [Fact]
    public void Constructor_NoFixtures_DefaultsToEmptyList()
    {
        var apartment = new Apartment("apt-1", floorNumber: 0);

        Assert.NotNull(apartment.Fixtures);
        Assert.Empty(apartment.Fixtures);
    }

    /// <summary>Id ריק (או רק רווחים) זורק ArgumentException.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyId_Throws(string emptyId)
    {
        Assert.Throws<ArgumentException>(() => new Apartment(emptyId, floorNumber: 1));
    }
}
