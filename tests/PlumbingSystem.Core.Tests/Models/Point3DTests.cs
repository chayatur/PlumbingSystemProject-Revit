using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Models;

/// <summary>בדיקות שוויון-ערך ופורמט התצוגה של <see cref="Point3D"/>.</summary>
public class Point3DTests
{
    /// <summary>שתי נקודות עם אותם X, Y, Z נחשבות שוות.</summary>
    [Fact]
    public void Equals_SameCoordinates_ReturnsTrue()
    {
        var a = new Point3D(1.0, 2.0, 3.0);
        var b = new Point3D(1.0, 2.0, 3.0);

        Assert.True(a == b);
        Assert.Equal(a, b);
    }

    /// <summary>נקודות עם קואורדינטה שונה לא נחשבות שוות.</summary>
    [Fact]
    public void Equals_DifferentCoordinates_ReturnsFalse()
    {
        var a = new Point3D(1.0, 2.0, 3.0);
        var b = new Point3D(1.0, 2.0, 3.1);

        Assert.True(a != b);
        Assert.NotEqual(a, b);
    }

    /// <summary>ToString מציג את שלוש הקואורדינטות בפורמט "(X, Y, Z)".</summary>
    [Fact]
    public void ToString_ReturnsReadableFormat()
    {
        var point = new Point3D(1.5, -2.25, 0);

        Assert.Equal("(1.5, -2.25, 0)", point.ToString());
    }
}
