using PlumbingSystem.Core.Domain;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>
/// בדיקות ל-<see cref="PipeRouteParameters"/> (PIPE Step 3): בדיקת-השפיות
/// הבסיסית של Core על הקוטר/שיפוע שמוזנים מבחוץ, ו-<see cref="PipeRouteParameters.Default"/>
/// שמשקף את הערכים שהיו <c>const</c> לפני חיבור קובץ-ההגדרות המשרדי.
/// </summary>
public class PipeRouteParametersTests
{
    [Fact]
    public void Default_MatchesTheHistoricalHardcodedValues()
    {
        Assert.Equal(PipeRouteCalculator.PipeDiameterMm, PipeRouteParameters.Default.DiameterMm);
        Assert.Equal(PipeRouteCalculator.DefaultSlopePercent, PipeRouteParameters.Default.SlopePercent);
        Assert.Equal(110.0, PipeRouteParameters.Default.DiameterMm);
        Assert.Equal(1.75, PipeRouteParameters.Default.SlopePercent, precision: 6);
    }

    [Fact]
    public void ValidValues_AreStoredAsGiven()
    {
        var parameters = new PipeRouteParameters { DiameterMm = 100.0, SlopePercent = 2.0 };

        Assert.Equal(100.0, parameters.DiameterMm);
        Assert.Equal(2.0, parameters.SlopePercent);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-110.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonPositiveOrNonFiniteDiameter_Throws(double badDiameter)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PipeRouteParameters { DiameterMm = badDiameter, SlopePercent = 1.75 });
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.75)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonPositiveOrNonFiniteSlope_Throws(double badSlope)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PipeRouteParameters { DiameterMm = 110.0, SlopePercent = badSlope });
    }
}
