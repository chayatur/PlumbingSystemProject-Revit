using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Models;

/// <summary>בדיקות בנייה וולידציית שדות בסיסית של <see cref="ToiletFixture"/>.</summary>
public class ToiletFixtureTests
{
    /// <summary>בנייה עם פרמטרים תקינים שומרת את כל השדות כמו שסופקו.</summary>
    [Fact]
    public void Constructor_ValidArguments_SetsAllProperties()
    {
        var location = new Point3D(1, 2, 3);

        var fixture = new ToiletFixture("fixture-1", location, "apt-1", isGuestBathroom: true);

        Assert.Equal("fixture-1", fixture.Id);
        Assert.Equal(location, fixture.Location);
        Assert.Equal("apt-1", fixture.ApartmentId);
        Assert.True(fixture.IsGuestBathroom);
    }

    /// <summary>Id ריק (או רק רווחים) זורק ArgumentException.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyId_Throws(string emptyId)
    {
        Assert.Throws<ArgumentException>(
            () => new ToiletFixture(emptyId, default, "apt-1", isGuestBathroom: false));
    }

    /// <summary>ApartmentId ריק זורק ArgumentException.</summary>
    [Fact]
    public void Constructor_EmptyApartmentId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ToiletFixture("fixture-1", default, string.Empty, isGuestBathroom: false));
    }
}
