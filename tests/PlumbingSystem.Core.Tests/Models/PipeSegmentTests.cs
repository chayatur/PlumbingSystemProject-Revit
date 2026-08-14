using PlumbingSystem.Core.Models;
using Xunit;

namespace PlumbingSystem.Core.Tests.Models;

/// <summary>בדיקות בנייה וולידציית שדות בסיסית של <see cref="PipeSegment"/>.</summary>
public class PipeSegmentTests
{
    /// <summary>בנייה עם פרמטרים תקינים שומרת את כל השדות כמו שסופקו.</summary>
    [Fact]
    public void Constructor_ValidArguments_SetsAllProperties()
    {
        var start = new Point3D(0, 0, 0);
        var end = new Point3D(1, 0, 0);

        var segment = new PipeSegment("pipe-1", start, end, diameterMm: 110, slopePercent: 2.0);

        Assert.Equal("pipe-1", segment.Id);
        Assert.Equal(start, segment.StartPoint);
        Assert.Equal(end, segment.EndPoint);
        Assert.Equal(110, segment.DiameterMm);
        Assert.Equal(2.0, segment.SlopePercent);
    }

    /// <summary>
    /// ערכי שיפוע/קוטר "לא הנדסיים" (מחוץ לטווח החוקי) לא זורקים כאן -
    /// זה מכוון: ולידציה הנדסית מגיעה בשלב נפרד, לא בבניית האובייקט.
    /// </summary>
    [Fact]
    public void Constructor_OutOfEngineeringRangeValues_DoesNotThrow()
    {
        var segment = new PipeSegment("pipe-1", default, default, diameterMm: -5, slopePercent: 99);

        Assert.Equal(-5, segment.DiameterMm);
        Assert.Equal(99, segment.SlopePercent);
    }

    /// <summary>Id ריק (או רק רווחים) זורק ArgumentException.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyId_Throws(string emptyId)
    {
        Assert.Throws<ArgumentException>(
            () => new PipeSegment(emptyId, default, default, diameterMm: 110, slopePercent: 2.0));
    }
}
