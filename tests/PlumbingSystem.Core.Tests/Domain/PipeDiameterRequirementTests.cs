using PlumbingSystem.Core.Domain;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>
/// בדיקות ל-<see cref="PipeDiameterRequirement"/> (PIPE Step 4.1):
/// ניתוח מחרוזת מקובץ-ההגדרות (מספר מדויק / טווח <c>min-max</c>),
/// והשוואת קוטר-מועמד מול הדרישה - כולל תאימות-לאחור ל-<c>= 110</c>.
/// </summary>
public class PipeDiameterRequirementTests
{
    // ---------- Parse ----------

    [Theory]
    [InlineData("110", 110.0)]
    [InlineData("101.6", 101.6)]
    [InlineData("  101.6  ", 101.6)]
    [InlineData("4", 4.0)]
    public void Parse_SingleNumber_IsExact(string raw, double expected)
    {
        PipeDiameterRequirement req = PipeDiameterRequirement.Parse(raw);

        Assert.True(req.IsExact);
        Assert.Equal(expected, req.MinMm, precision: 6);
        Assert.Equal(expected, req.MaxMm, precision: 6);
        Assert.Equal(expected, req.NominalMm, precision: 6);
    }

    [Theory]
    [InlineData("100-110", 100.0, 110.0)]
    [InlineData("100 - 110", 100.0, 110.0)]
    [InlineData("101.6-160", 101.6, 160.0)]
    [InlineData("100-100", 100.0, 100.0)]
    public void Parse_Range_IsRange(string raw, double min, double max)
    {
        PipeDiameterRequirement req = PipeDiameterRequirement.Parse(raw);

        Assert.Equal(min, req.MinMm, precision: 6);
        Assert.Equal(max, req.MaxMm, precision: 6);
        Assert.Equal(min, req.NominalMm, precision: 6); // NominalMm = הקצה התחתון
    }

    [Fact]
    public void Parse_EqualRange_IsExact()
    {
        Assert.True(PipeDiameterRequirement.Parse("100-100").IsExact);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("100-")]
    [InlineData("-110")]          // מקף מוביל: "-110" נקרא כמספר שלילי → נפסל
    [InlineData("100--110")]
    [InlineData("100-110-120")]
    [InlineData("0")]             // לא חיובי
    [InlineData("-5")]            // שלילי
    [InlineData("110-100")]       // min > max
    [InlineData("1,75")]          // פסיק עשרוני - לא נתמך (נקודה בלבד)
    [InlineData("100,110")]       // פסיק כמפריד-טווח - לא נתמך
    [InlineData("NaN")]
    public void Parse_Invalid_ThrowsFormatException(string raw)
    {
        Assert.Throws<FormatException>(() => PipeDiameterRequirement.Parse(raw));
    }

    // ---------- Matches: exact ----------

    [Fact]
    public void Matches_Exact_SameValue_True()
    {
        Assert.True(PipeDiameterRequirement.Exact(101.6).Matches(101.6));
    }

    [Fact]
    public void Matches_Exact_FloatNoise_True()
    {
        // 4" = 101.6, Revit feet→mm יכול לתת 101.59999999999998.
        Assert.True(PipeDiameterRequirement.Exact(101.6).Matches(101.59999999999998));
        Assert.True(PipeDiameterRequirement.Exact(101.6).Matches(101.60000000000001));
    }

    [Theory]
    [InlineData(110.0)]
    [InlineData(100.0)]
    [InlineData(101.5)]
    [InlineData(102.0)]
    public void Matches_Exact_DifferentEngineeringSize_False(double candidate)
    {
        // דרישה מדויקת נשארת מדויקת - לא הופכת לטווח הנדסי.
        Assert.False(PipeDiameterRequirement.Exact(101.6).Matches(candidate));
    }

    // ---------- Matches: range ----------

    [Theory]
    [InlineData(100.0, true)]   // קצה תחתון כולל
    [InlineData(110.0, true)]   // קצה עליון כולל
    [InlineData(101.6, true)]
    [InlineData(99.9, false)]
    [InlineData(88.9, false)]
    [InlineData(127.0, false)]
    [InlineData(110.001, false)]
    public void Matches_Range_100To110(double candidate, bool expected)
    {
        Assert.Equal(expected, PipeDiameterRequirement.Range(100, 110).Matches(candidate));
    }

    [Fact]
    public void Matches_NonFinite_False()
    {
        Assert.False(PipeDiameterRequirement.Range(100, 110).Matches(double.NaN));
        Assert.False(PipeDiameterRequirement.Exact(110).Matches(double.PositiveInfinity));
    }

    // ---------- Factories ----------

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Exact_NonPositive_Throws(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PipeDiameterRequirement.Exact(bad));
    }

    [Fact]
    public void Range_MinGreaterThanMax_Throws()
    {
        Assert.Throws<ArgumentException>(() => PipeDiameterRequirement.Range(110, 100));
    }

    [Fact]
    public void Describe_ReadsWell()
    {
        Assert.Contains("מדויק", PipeDiameterRequirement.Exact(101.6).Describe());
        Assert.Contains("101.6", PipeDiameterRequirement.Exact(101.6).Describe());
        Assert.Contains("טווח", PipeDiameterRequirement.Range(100, 110).Describe());
        Assert.Contains("100-110", PipeDiameterRequirement.Range(100, 110).Describe());
    }
}
