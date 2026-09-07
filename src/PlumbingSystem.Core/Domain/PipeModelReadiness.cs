using System.Globalization;
using System.Text;

namespace PlumbingSystem.Core.Domain;

/// <summary>
/// מועמד <c>PipeType</c> של Revit כפי שנשלף על ידי שכבת-Revit: השם,
/// ורשימת הקטרים הנומינליים (מ"מ) ש-<c>Segment</c>-ים שלו תומכים בהם
/// **בפועל** (נקרא מ-<c>RoutingPreferenceManager</c> →
/// <c>PipeSegment.GetSizes()</c> → <c>MEPSize.NominalDiameter</c> → המרה
/// למ"מ, לא הנחה מהטקסט המוצג כמו <c>"4\""</c>).
/// </summary>
/// <param name="Name"><c>PipeType.Name</c>.</param>
/// <param name="SupportedNominalDiametersMm">הקטרים (מ"מ) שנתמכים בפועל - יכול להיות ריק (אין Segment rules).</param>
public sealed record CandidatePipeType(string Name, IReadOnlyList<double> SupportedNominalDiametersMm);

/// <summary>
/// תוצאת <see cref="PipeModelReadiness.Evaluate"/> - האם המודל **מוכן**
/// לספק צינור העומד בדרישת-הקוטר של המשרד, איזה גודל קיים ייבחר, ו-
/// <see cref="Report"/> קריא-לאדם המוכן להצגה ב-<c>TaskDialog</c>.
/// </summary>
public sealed record PipeModelReadinessResult
{
    /// <summary><c>true</c> רק אם קיים גם טיפוס-מערכת Sanitary וגם גודל-<c>Segment</c> קיים שעומד בדרישה.</summary>
    public required bool IsReady { get; init; }

    /// <summary>דרישת-הקוטר שנבדקה (מדויק / טווח) - כפי שהגיעה מקובץ-ההגדרות המשרדי.</summary>
    public required PipeDiameterRequirement Requirement { get; init; }

    /// <summary>
    /// הגודל הקיים (מ"מ) שנבחר לשימוש - הקטן ביותר מבין הגדלים שעומדים
    /// בדרישה (ראו כלל-הבחירה ב-<see cref="PipeModelReadiness.Evaluate"/>).
    /// <c>null</c> אם אף גודל לא עמד בדרישה.
    /// </summary>
    public double? SelectedDiameterMm { get; init; }

    /// <summary>שם טיפוס-המערכת ה-Sanitary שנבחר (הראשון בסדר א"ב), או <c>null</c> אם אין.</summary>
    public string? SanitarySystemTypeName { get; init; }

    /// <summary>שם ה-<c>PipeType</c> שכולל את <see cref="SelectedDiameterMm"/> (הראשון בסדר א"ב), או <c>null</c>.</summary>
    public string? MatchingPipeTypeName { get; init; }

    /// <summary>דוח קריא לאדם - מסביר בדיוק מה נמצא, ואם לא מוכן - מה חסר ומה לתקן במודל.</summary>
    public required string Report { get; init; }
}

/// <summary>
/// **מדיניות STARTARC** לבחירת <c>PipingSystemType</c>/<c>PipeType</c>/גודל
/// ליצירת <c>Pipe</c> אמיתי (PIPE Step 4): דרישת-הקוטר
/// (<see cref="PipeDiameterRequirement"/> - מדויק או טווח) היא נתון-קלט
/// מקובץ-ההגדרות המשרדי, והמודל **חייב** לספק <c>Segment</c> שגודלו עומד
/// בדרישה. <b>אין fallback שקט</b> - אם אין גודל מתאים, מוחזר
/// <see cref="PipeModelReadinessResult.IsReady"/> = <c>false</c> + דוח
/// שמסביר מה להגדיר במודל.
/// </summary>
/// <remarks>
/// לוגיקה טהורה (בלי RevitAPI) - שכבת-Revit
/// (<c>PipeModelReadinessInspector</c>) שולפת את הנתונים
/// (<c>PipingSystemType.SystemClassification == Sanitary</c>, ולכל
/// <c>PipeType</c> את קטרי-ה-<c>Segment</c> בפועל, מומרים למ"מ) ומעבירה
/// לכאן כמחרוזות/מספרים. כך ההחלטה ("מוכן / לא-מוכן, איזה גודל, ולמה")
/// ניתנת לבדיקת-יחידה בלי Revit. ראו
/// docs/pipe-step4-1-diameter-requirements.md,
/// docs/pipe-step4-revit-pipe-creation.md.
/// </remarks>
public static class PipeModelReadiness
{
    /// <summary>
    /// מעריכה מוכנות: <see cref="PipeModelReadinessResult.IsReady"/> = <c>true</c>
    /// רק אם (א) קיים לפחות טיפוס-מערכת אחד המסווג Sanitary, ו-(ב) קיים
    /// לפחות גודל-<c>Segment</c> אחד (על <c>PipeType</c> כלשהו) ש-
    /// <paramref name="requirement"/>.<see cref="PipeDiameterRequirement.Matches"/>
    /// מחזיר עליו <c>true</c>.
    /// </summary>
    /// <remarks>
    /// **כלל הבחירה** (דטרמיניסטי, מתועד): כשכמה גדלים עומדים בדרישה
    /// (רלוונטי בעיקר לטווח) - נבחר ה**גודל הקטן ביותר** מביניהם, כדי לא
    /// לבחור קוטר גדול יותר ללא צורך. כשכמה <c>PipeType</c>-ים כוללים את
    /// אותו גודל נבחר - הראשון בסדר א"ב. דוגמה: טווח <c>100-160</c>,
    /// המודל מכיל <c>101.6, 127, 152.4</c> → נבחר <c>101.6</c>.
    /// </remarks>
    /// <param name="requirement">דרישת-הקוטר (מדויק / טווח) מקובץ-ההגדרות המשרדי.</param>
    /// <param name="sanitarySystemTypeNames">שמות ה-<c>PipingSystemType</c> שסווגו Sanitary (יכול להיות ריק).</param>
    /// <param name="pipeTypes">כל ה-<c>PipeType</c> במודל + הקטרים (מ"מ) שהם תומכים בהם בפועל.</param>
    public static PipeModelReadinessResult Evaluate(
        PipeDiameterRequirement requirement,
        IReadOnlyList<string> sanitarySystemTypeNames,
        IReadOnlyList<CandidatePipeType> pipeTypes)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(sanitarySystemTypeNames);
        ArgumentNullException.ThrowIfNull(pipeTypes);

        string? sanitaryName = sanitarySystemTypeNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        // כל הזוגות (PipeType, גודל) שהגודל בהם עומד בדרישה.
        List<(string PipeType, double Mm)> matches = pipeTypes
            .SelectMany(pt => pt.SupportedNominalDiametersMm
                .Where(requirement.Matches)
                .Select(mm => (pt.Name, mm)))
            .ToList();

        double? selectedMm = null;
        string? matchingPipeType = null;
        if (matches.Count > 0)
        {
            // כלל הבחירה: הגודל הקיים הקטן ביותר שעומד בדרישה.
            selectedMm = matches.Min(m => m.Mm);
            matchingPipeType = matches
                .Where(m => Math.Abs(m.Mm - selectedMm.Value) <= PipeDiameterRequirement.FloatToleranceMm)
                .Select(m => m.PipeType)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        bool isReady = sanitaryName is not null && selectedMm is not null;
        string report = BuildReport(requirement, sanitaryName, matchingPipeType, selectedMm, pipeTypes);

        return new PipeModelReadinessResult
        {
            IsReady = isReady,
            Requirement = requirement,
            SelectedDiameterMm = selectedMm,
            SanitarySystemTypeName = sanitaryName,
            MatchingPipeTypeName = matchingPipeType,
            Report = report,
        };
    }

    private static string BuildReport(
        PipeDiameterRequirement requirement,
        string? sanitaryName,
        string? matchingPipeType,
        double? selectedMm,
        IReadOnlyList<CandidatePipeType> pipeTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"דרישת-קוטר (מקובץ-ההגדרות המשרדי): {requirement.Describe()}.");

        sb.AppendLine(sanitaryName is not null
            ? $"טיפוס-מערכת Sanitary: נמצא - \"{sanitaryName}\"."
            : "טיפוס-מערכת Sanitary: לא נמצא אף PipingSystemType עם SystemClassification == Sanitary.");

        sb.AppendLine();
        sb.AppendLine("PipeType-ים שנבדקו (שם: קטרים במ\"מ, ✓ = עומד בדרישה):");
        if (pipeTypes.Count == 0)
        {
            sb.AppendLine("  (אין אף PipeType במודל)");
        }
        else
        {
            foreach (CandidatePipeType pt in pipeTypes.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                string sizes = pt.SupportedNominalDiametersMm.Count > 0
                    ? string.Join(", ", pt.SupportedNominalDiametersMm
                        .OrderBy(mm => mm)
                        .Select(mm => requirement.Matches(mm)
                            ? $"{Fmt(mm)}✓"
                            : Fmt(mm)))
                    : "(אין Segment rules / אין קטרים)";
                sb.AppendLine($"  \"{pt.Name}\": {sizes}");
            }
        }

        sb.AppendLine();
        bool isReady = sanitaryName is not null && selectedMm is not null;
        if (isReady)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "מוכן: הגודל שנבחר הוא {0:0.###} מ\"מ (הקטן ביותר שעומד בדרישה) מ-PipeType \"{1}\", טיפוס-מערכת \"{2}\".",
                selectedMm!.Value, matchingPipeType, sanitaryName));
            return sb.ToString();
        }

        sb.AppendLine("לא מוכן - המודל אינו יכול לספק צינור העומד בדרישה:");
        if (sanitaryName is null)
        {
            sb.AppendLine("  - אין טיפוס-מערכת המסווג Sanitary. יש להגדיר PipingSystemType לביוב (SystemClassification = Sanitary).");
        }

        if (selectedMm is null)
        {
            sb.AppendLine($"  - אף גודל-Segment קיים במודל אינו עומד בדרישה {requirement.Describe()}.");
            sb.AppendLine("  - יש להגדיר / להוסיף במודל (או ב-Template המשרד) PipeSegment בגודל מתאים תחת PipeType מתאים - Manage -> MEP Settings -> Segments and Sizes.");
        }

        sb.AppendLine();
        sb.AppendLine("STARTARC לא בוחר קוטר חלופי שאינו עומד בדרישה, לא מעגל, ולא יוצר גודל חדש - הדרישה נקבעת במשרד, והמודל צריך להיות מוגדר בהתאם.");
        return sb.ToString();
    }

    private static string Fmt(double mm) => mm.ToString("0.###", CultureInfo.InvariantCulture);
}
