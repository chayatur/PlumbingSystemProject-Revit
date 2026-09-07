using PlumbingSystem.Core.Domain;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>
/// בדיקות ל-<see cref="PipeModelReadiness.Evaluate"/> (PIPE Step 4 / 4.1):
/// מוכן רק אם יש טיפוס-מערכת Sanitary וגם גודל-Segment קיים שעומד ב-
/// <see cref="PipeDiameterRequirement"/> (מדויק / טווח); אחרת
/// <c>IsReady=false</c> + דוח ברור, **בלי לבחור קוטר חלופי**. כלל-הבחירה:
/// הגודל הקטן ביותר שעומד בדרישה.
/// </summary>
public class PipeModelReadinessTests
{
    private static readonly string[] OneSanitary = { "מערכת ביוב" };

    private static CandidatePipeType Pt(string name, params double[] sizesMm) => new(name, sizesMm);

    // ------- Exact requirement -------

    [Fact]
    public void Evaluate_Exact101_6_ModelHas101_6_IsReady_SelectsIt()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Exact(101.6), OneSanitary, new[] { Pt("PVC", 75, 101.6, 160) });

        Assert.True(result.IsReady);
        Assert.Equal(101.6, result.SelectedDiameterMm!.Value, precision: 6);
        Assert.Equal("PVC", result.MatchingPipeTypeName);
        Assert.Contains("מוכן", result.Report);
    }

    [Fact]
    public void Evaluate_Exact101_6_ModelHas110_Only_NotReady_NoFallback()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Exact(101.6), OneSanitary, new[] { Pt("PVC", 100, 110, 125) });

        Assert.False(result.IsReady);
        Assert.Null(result.SelectedDiameterMm);
        Assert.Null(result.MatchingPipeTypeName);
        Assert.Contains("לא מוכן", result.Report);
        Assert.Contains("לא בוחר קוטר חלופי", result.Report);
    }

    [Fact]
    public void Evaluate_Exact_MatchesRevitFloatNoise()
    {
        // 4" = 101.6 מ"מ, אבל המרת feet→mm של Revit יכולה להחזיר 101.59999999999998.
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Exact(101.6), OneSanitary, new[] { Pt("PVC", 101.59999999999998) });

        Assert.True(result.IsReady);
    }

    // ------- Range requirement -------

    [Fact]
    public void Evaluate_Range100To110_ModelHas101_6_IsReady()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Range(100, 110), OneSanitary, new[] { Pt("PVC", 76.2, 88.9, 101.6, 127) });

        Assert.True(result.IsReady);
        Assert.Equal(101.6, result.SelectedDiameterMm!.Value, precision: 6);
    }

    [Fact]
    public void Evaluate_Range100To110_ModelHas88_9_NotReady()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Range(100, 110), OneSanitary, new[] { Pt("PVC", 76.2, 88.9) });

        Assert.False(result.IsReady);
        Assert.Null(result.SelectedDiameterMm);
    }

    [Fact]
    public void Evaluate_Range100To110_ModelHas127_NotReady()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Range(100, 110), OneSanitary, new[] { Pt("PVC", 127, 152.4) });

        Assert.False(result.IsReady);
    }

    [Fact]
    public void Evaluate_Range100To160_MultipleInRange_SelectsSmallest()
    {
        // כלל הבחירה: 101.6, 127, 152.4 כולם בטווח → נבחר 101.6.
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Range(100, 160), OneSanitary, new[] { Pt("PVC", 101.6, 127, 152.4) });

        Assert.True(result.IsReady);
        Assert.Equal(101.6, result.SelectedDiameterMm!.Value, precision: 6);
    }

    [Fact]
    public void Evaluate_Range_MultiplePipeTypesShareSelectedSize_PicksAlphabeticallyFirst()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Range(100, 160), OneSanitary,
            new[] { Pt("Zeta", 101.6), Pt("Alpha", 101.6), Pt("Mu", 127) });

        Assert.True(result.IsReady);
        Assert.Equal(101.6, result.SelectedDiameterMm!.Value, precision: 6);
        Assert.Equal("Alpha", result.MatchingPipeTypeName);
    }

    [Fact]
    public void Evaluate_Range_NoSizeInRangeAnywhere_NotReady_ReportListsWhatWasFound()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Range(100, 110), OneSanitary,
            new[] { Pt("PVC", 76.2, 88.9), Pt("Steel", 200, 250) });

        Assert.False(result.IsReady);
        Assert.Contains("100-110", result.Report);
        Assert.Contains("76.2", result.Report);
        Assert.Contains("200", result.Report);
        Assert.Contains("לא בוחר קוטר חלופי", result.Report);
    }

    // ------- Sanitary system type -------

    [Fact]
    public void Evaluate_NoSanitarySystemType_NotReady_EvenIfSizeMatches()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Exact(110), Array.Empty<string>(), new[] { Pt("PVC", 110) });

        Assert.False(result.IsReady);
        Assert.Null(result.SanitarySystemTypeName);
        Assert.Contains("Sanitary", result.Report);
    }

    [Fact]
    public void Evaluate_MultipleSanitary_PicksAlphabeticallyFirst()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Exact(110), new[] { "ביוב B", "ביוב A" }, new[] { Pt("PVC", 110) });

        Assert.Equal("ביוב A", result.SanitarySystemTypeName);
    }

    // ------- Backward compatibility -------

    [Fact]
    public void Evaluate_ExactFromParsed110_BehavesAsExactRequirement()
    {
        PipeDiameterRequirement req = PipeDiameterRequirement.Parse("110");
        var result = PipeModelReadiness.Evaluate(req, OneSanitary, new[] { Pt("PVC", 100, 110, 125) });

        Assert.True(req.IsExact);
        Assert.True(result.IsReady);
        Assert.Equal(110.0, result.SelectedDiameterMm!.Value, precision: 6);
    }

    // ------- Edge cases -------

    [Fact]
    public void Evaluate_PipeTypeWithNoSizes_Ignored()
    {
        var result = PipeModelReadiness.Evaluate(
            PipeDiameterRequirement.Exact(110), OneSanitary,
            new[] { new CandidatePipeType("Empty", Array.Empty<double>()), Pt("PVC", 110) });

        Assert.True(result.IsReady);
        Assert.Equal("PVC", result.MatchingPipeTypeName);
    }

    [Fact]
    public void Evaluate_NullRequirement_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => PipeModelReadiness.Evaluate(null!, OneSanitary, Array.Empty<CandidatePipeType>()));
    }
}
