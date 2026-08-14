using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Geometry;

/// <summary>בדיקות עבור <see cref="GeometryUtils.Distance2D"/>.</summary>
public class GeometryUtilsTests
{
    /// <summary>נקודה כלפי עצמה - מרחק אפס.</summary>
    [Fact]
    public void Distance2D_SamePoint_ReturnsZero()
    {
        var point = new Point3D(5, 5, 5);

        Assert.Equal(0, GeometryUtils.Distance2D(point, point));
    }

    /// <summary>הפרש רק ב-Z לא משפיע על התוצאה - Z לא נכנס לחישוב.</summary>
    [Fact]
    public void Distance2D_OnlyZDiffers_ReturnsZero()
    {
        var a = new Point3D(1, 1, 0);
        var b = new Point3D(1, 1, 100);

        Assert.Equal(0, GeometryUtils.Distance2D(a, b));
    }

    /// <summary>משולש 3-4-5 קלאסי - מוודא את נוסחת המרחק עצמה.</summary>
    [Fact]
    public void Distance2D_ThreeFourFiveTriangle_ReturnsFive()
    {
        var a = new Point3D(0, 0, 0);
        var b = new Point3D(3, 4, 0);

        Assert.Equal(5, GeometryUtils.Distance2D(a, b), precision: 6);
    }

    /// <summary>המרחק סימטרי - Distance2D(a, b) == Distance2D(b, a).</summary>
    [Fact]
    public void Distance2D_IsSymmetric()
    {
        var a = new Point3D(2, 7, 1);
        var b = new Point3D(-3, 4, 9);

        Assert.Equal(GeometryUtils.Distance2D(a, b), GeometryUtils.Distance2D(b, a), precision: 10);
    }
}
