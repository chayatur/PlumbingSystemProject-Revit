using System.Globalization;

namespace PlumbingSystem.Core.Domain;

/// <summary>
/// דרישת-הקוטר של STARTARC למקטע-צנרת, כפי שהמשרד מגדיר אותה בקובץ-
/// ההגדרות (PIPE Step 4.1). שתי צורות:
/// <list type="bullet">
///   <item><b>קוטר מדויק</b> - מספר יחיד, למשל <c>101.6</c>.</item>
///   <item><b>טווח כולל</b> - <c>min-max</c>, למשל <c>100-110</c> (כלומר
///     <c>100 ≤ diameter ≤ 110</c>).</item>
/// </list>
/// דרישה מדויקת שקולה לטווח שבו <see cref="MinMm"/> == <see cref="MaxMm"/>.
/// </summary>
/// <remarks>
/// **לוגיקה טהורה** (בלי RevitAPI / קובץ / נתיב). שכבת-Revit
/// (<c>PipingOfficeSettings</c>) קוראת את המחרוזת מהקובץ וקוראת ל-
/// <see cref="Parse"/>; <see cref="PipeModelReadiness"/> משתמשת ב-
/// <see cref="Matches"/> כדי לבדוק אילו גדלי-<c>Segment</c> אמיתיים
/// במודל עומדים בדרישה.
///
/// **הבחנה חשובה** (ראו <see cref="FloatToleranceMm"/>): דרישה מדויקת
/// **נשארת מדויקת**. הסבילות היחידה היא ברמת floating-point (המרת
/// feet→mm של Revit צוברת שגיאת-ביט זעירה) - היא **אינה** הופכת דרישה
/// מדויקת לטווח הנדסי של ±ε. אם המשרד רוצה סבילות הנדסית, שיגדיר טווח.
/// </remarks>
public sealed record PipeDiameterRequirement
{
    /// <summary>
    /// סבילות מספרית **ל-floating-point בלבד** (מ"מ). קטרים שמגיעים מ-
    /// Revit עוברים המרה <c>feet → mm</c> שיכולה לצבור שגיאת-ביט זעירה
    /// (למשל <c>4"</c> = 101.6 מ"מ שנקרא כ-<c>101.59999999999998</c>).
    /// <c>1e-6</c> מ"מ הוא ננומטר - קטן בהרבה מכל הפרש-גדלים אמיתי, גדול
    /// בהרבה מכל שגיאת-המרה סבירה. **זו לא סבילות הנדסית.**
    /// </summary>
    public const double FloatToleranceMm = 1e-6;

    private PipeDiameterRequirement(double minMm, double maxMm)
    {
        MinMm = minMm;
        MaxMm = maxMm;
    }

    /// <summary>גבול תחתון (מ"מ, כולל). עבור דרישה מדויקת - שווה ל-<see cref="MaxMm"/>.</summary>
    public double MinMm { get; }

    /// <summary>גבול עליון (מ"מ, כולל). עבור דרישה מדויקת - שווה ל-<see cref="MinMm"/>.</summary>
    public double MaxMm { get; }

    /// <summary><c>true</c> אם הדרישה היא קוטר מדויק (<see cref="MinMm"/> == <see cref="MaxMm"/>).</summary>
    public bool IsExact => MinMm.Equals(MaxMm);

    /// <summary>
    /// מספר-קוטר יחיד המייצג את הדרישה כשנדרש אחד (למשל לצינור-<c>DirectShape</c>
    /// של PIPE Step 3, שהוא סמן-חזותי): הקוטר המדויק, או **הקצה התחתון**
    /// של הטווח - עקבי עם כלל-הבחירה של <see cref="PipeModelReadiness"/>
    /// ("הגודל הקטן ביותר שעומד בדרישה"). הגודל ה**אמיתי** שייבחר מהמודל
    /// מגיע מ-<see cref="PipeModelReadiness"/>, לא מכאן.
    /// </summary>
    public double NominalMm => MinMm;

    /// <summary>יוצרת דרישת-קוטר מדויקת. זורקת <see cref="ArgumentOutOfRangeException"/> אם אינו חיובי/סופי.</summary>
    public static PipeDiameterRequirement Exact(double diameterMm)
    {
        GuardPositiveFinite(diameterMm, nameof(diameterMm));
        return new PipeDiameterRequirement(diameterMm, diameterMm);
    }

    /// <summary>
    /// יוצרת דרישת-טווח כולל <c>[minMm, maxMm]</c>. <c>minMm == maxMm</c>
    /// מותר (= דרישה מדויקת). זורקת אם ערך אינו חיובי/סופי, או אם
    /// <paramref name="minMm"/> &gt; <paramref name="maxMm"/>.
    /// </summary>
    public static PipeDiameterRequirement Range(double minMm, double maxMm)
    {
        GuardPositiveFinite(minMm, nameof(minMm));
        GuardPositiveFinite(maxMm, nameof(maxMm));
        if (minMm > maxMm)
        {
            throw new ArgumentException(
                $"טווח קוטר לא תקין: הגבול התחתון ({Fmt(minMm)}) גדול מהעליון ({Fmt(maxMm)}). הפורמט הוא min-max.");
        }

        return new PipeDiameterRequirement(minMm, maxMm);
    }

    /// <summary>
    /// מנתחת מחרוזת מקובץ-ההגדרות: מספר יחיד → דרישה מדויקת;
    /// <c>min-max</c> (מקף בין שני מספרים) → טווח. מספרים בנקודה עשרונית
    /// (לא פסיק). זורקת <see cref="FormatException"/> עם הסבר קריא-לאדם אם
    /// המחרוזת אינה תקינה (הודעה נקייה, בלי "Parameter ...").
    /// </summary>
    public static PipeDiameterRequirement Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string trimmed = raw.Trim();

        // מספר יחיד **קודם** - כך "-5" נתפס כמספר (שלילי) ונפסל, ולא
        // מתפרש בטעות כטווח עם מקף מוביל.
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double single))
        {
            if (!double.IsFinite(single) || single <= 0.0)
            {
                throw Invalid(raw, "קוטר חייב להיות מספר חיובי");
            }

            return new PipeDiameterRequirement(single, single);
        }

        string[] parts = trimmed.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lo)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double hi))
        {
            if (!double.IsFinite(lo) || lo <= 0.0 || !double.IsFinite(hi) || hi <= 0.0)
            {
                throw Invalid(raw, "שני גבולות הטווח חייבים להיות מספרים חיוביים");
            }

            if (lo > hi)
            {
                throw Invalid(raw, $"הגבול התחתון ({Fmt(lo)}) גדול מהעליון ({Fmt(hi)})");
            }

            return new PipeDiameterRequirement(lo, hi);
        }

        throw Invalid(raw,
            "השתמש במספר יחיד (למשל 101.6) או בטווח min-max עם מקף (למשל 100-110), עם נקודה עשרונית ולא פסיק");
    }

    private static FormatException Invalid(string raw, string reason) =>
        new($"'{raw}' אינו דרישת-קוטר תקינה - {reason}.");

    /// <summary>
    /// <c>true</c> אם <paramref name="candidateDiameterMm"/> **עומד בדרישה**:
    /// עבור דרישה מדויקת - שווה לה עד כדי <see cref="FloatToleranceMm"/>
    /// (float בלבד); עבור טווח - בתוך <c>[Min, Max]</c> כולל (עם אותה
    /// סבילות-float על הקצוות). <c>false</c> עבור ערך לא-סופי.
    /// </summary>
    public bool Matches(double candidateDiameterMm)
    {
        if (!double.IsFinite(candidateDiameterMm))
        {
            return false;
        }

        return candidateDiameterMm >= MinMm - FloatToleranceMm
            && candidateDiameterMm <= MaxMm + FloatToleranceMm;
    }

    /// <summary>תיאור קריא-לאדם: <c>"101.6 מ\"מ (מדויק)"</c> / <c>"100-110 מ\"מ (טווח)"</c>.</summary>
    public string Describe() => IsExact
        ? $"{Fmt(MinMm)} מ\"מ (מדויק)"
        : $"{Fmt(MinMm)}-{Fmt(MaxMm)} מ\"מ (טווח)";

    /// <inheritdoc/>
    public override string ToString() => Describe();

    private static void GuardPositiveFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName, value, "קוטר חייב להיות מספר חיובי וסופי (מ\"מ).");
        }
    }

    private static string Fmt(double value) => value.ToString(CultureInfo.InvariantCulture);
}
