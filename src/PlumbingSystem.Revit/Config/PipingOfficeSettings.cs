using System.Globalization;
using PlumbingSystem.Core.Domain;

namespace PlumbingSystem.Revit.Config;

/// <summary>
/// הגדרות-הצנרת המשרדיות של STARTARC (דרישת קוטר צינור-ביוב, שיפוע
/// ברירת-מחדל), כפי שנקראו **מקובץ-ההגדרות החיצוני** (<see cref="OfficeConfig"/>
/// → <c>PlumbingSystem.OfficeConfig.txt</c>). אלה **נתוני-Config, לא
/// <c>const</c> בקוד** - המשרד / מנהל-BIM עורך את הערכים בעורך-טקסט
/// רגיל, בלי Visual Studio / קוד-מקור / <c>.csproj</c> / rebuild.
///
/// **PIPE Step 4.1**: המפתח <c>Sewer pipe diameter mm</c> מקבל כעת או
/// מספר יחיד (קוטר מדויק, למשל <c>101.6</c>) או טווח כולל
/// (<c>min-max</c>, למשל <c>100-110</c>) - ראו <see cref="PipeDiameterRequirement"/>.
/// <c>Sewer pipe diameter mm = 110</c> ממשיך לעבוד כדרישה מדויקת (תאימות
/// לאחור).
/// </summary>
/// <remarks>
/// **גבול-שכבות מכוון**: המחלקה יושבת ב-<c>PlumbingSystem.Revit</c>
/// (שכבת-Revit), ו**לא** ב-<c>PlumbingSystem.Core</c>. Core הוא לוגיקה
/// עסקית טהורה ואסור שיהיה תלוי ב-RevitAPI או בקובץ-הגדרות של שכבת-
/// Revit. הזרימה: קובץ → <see cref="PipingOfficeSettings"/> → ייצוג-דומיין
/// של Core (<see cref="PipeDiameterRequirement"/> / <c>double</c>) →
/// <c>PipeRouteCalculator</c> (PIPE Step 3) ו-<c>PipeModelReadiness</c>
/// (PIPE Step 4).
///
/// **אין <c>fallback</c> שקט**: ערך חסר / לא-תקין (לא-מספר, טווח הפוך,
/// אינו חיובי) גורם ל-<see cref="OfficeConfigException"/> עם הודעה
/// שאומרת בדיוק איזו שורה בקובץ לתקן - לא בחירת קוטר/שיפוע חלופי בשקט.
///
/// **מנגנון-ההגדרות עצמו לא השתנה**: אותו קובץ, אותו פורמט
/// (<c>Key = Value</c>, <c>#</c> להערה, UTF-8), אותה הפצה
/// (<c>CopyToOutputDirectory</c> + Target <c>CopyAddinToRevit</c>
/// ב-<c>.csproj</c>) - רק ה-parsing של ערך-הקוטר הורחב (מספר יחיד או טווח).
/// </remarks>
public sealed record PipingOfficeSettings
{
    /// <summary>מפתח קובץ-ההגדרות לדרישת-הקוטר של צינור הביוב (מספר מדויק במ"מ, או טווח <c>min-max</c>).</summary>
    public const string SewerPipeDiameterMmKey = "Sewer pipe diameter mm";

    /// <summary>מפתח קובץ-ההגדרות לשיפוע ברירת-המחדל (אחוזים).</summary>
    public const string DefaultSlopePercentKey = "Default slope percent";

    /// <summary>
    /// תקרת-שפיות (**לא** מגבלה הנדסית) על השיפוע: ערך ≥ 100% (= 45°)
    /// אינו שיפוע-ביוב-בכבידה סביר ומעיד כמעט תמיד על טעות-הקלדה בקובץ.
    /// טווח-השיפוע ההנדסי האמיתי (1.5%-2.0%, חוק 2) נבדק במקום אחר
    /// (<c>PipeRouteCalculator</c>) - ואינו משתנה כאן.
    /// </summary>
    private const double SlopeSanityCeilingPercent = 100.0;

    /// <summary>
    /// דרישת-הקוטר של STARTARC לצינור הביוב - מדויק (למשל 101.6) או טווח
    /// (למשל 100-110). ראו <see cref="PipeDiameterRequirement"/>. מזינה את
    /// בדיקת מוכנות-המודל (<c>PipeModelReadiness</c>, PIPE Step 4).
    /// </summary>
    public required PipeDiameterRequirement SewerPipeDiameterRequirement { get; init; }

    /// <summary>
    /// קוטר-מ"מ יחיד המייצג את הדרישה (הקוטר המדויק, או הקצה התחתון של
    /// טווח) - נשמר לתאימות-לאחור עם צרכני PIPE Step 3
    /// (<c>DrawPipesCommand</c> → <c>PipeRouteParameters.DiameterMm</c>,
    /// שדורש <c>double</c> יחיד לגיאומטריית ה-<c>DirectShape</c>). הגודל
    /// ה**אמיתי** שייבחר מהמודל מגיע מ-<c>PipeModelReadiness.SelectedDiameterMm</c>
    /// (Step 4/5), לא מכאן.
    /// </summary>
    public double SewerPipeDiameterMm => SewerPipeDiameterRequirement.NominalMm;

    /// <summary>
    /// שיפוע ברירת-המחדל באחוזים (תמיד חיובי וקטן מ-100 - נאכף ב-<see cref="Load()"/>).
    /// כיום 1.75 (אמצע הטווח המאושר 1.5%-2.0%). מוזן ל-<c>PipeRouteCalculator</c>
    /// דרך <c>PipeRouteParameters</c> (PIPE Step 3).
    /// </summary>
    public required double DefaultSlopePercent { get; init; }

    /// <summary>
    /// קוראת ומאמתת את הגדרות-הצנרת מקובץ-ההגדרות המשרדי
    /// (<see cref="OfficeConfig"/>).
    /// </summary>
    /// <exception cref="OfficeConfigException">
    /// אם ערך כלשהו חסר, אינו מספר/טווח תקין, או אינו בטווח החוקי
    /// (קוטר גדול מ-0; שיפוע בין 0 ל-100; בטווח-קוטר - min קטן-או-שווה
    /// ל-max). ההודעה מציינת את שם-ההגדרה, את הבעיה, את נתיב-הקובץ ואת
    /// שורת-התיקון לדוגמה.
    /// </exception>
    public static PipingOfficeSettings Load() => Load(OfficeConfig.GetSingleValue);

    /// <summary>
    /// כמו <see cref="Load()"/>, אך עם מקור-ערכים מוזרק
    /// (<paramref name="readSetting"/>: מפתח → ערך גולמי או <c>null</c>) -
    /// נקודת-הפרדה לבדיקה, בלי תלות בקובץ אמיתי על הדיסק.
    /// </summary>
    internal static PipingOfficeSettings Load(Func<string, string?> readSetting)
    {
        ArgumentNullException.ThrowIfNull(readSetting);

        return new PipingOfficeSettings
        {
            SewerPipeDiameterRequirement = ParseDiameterRequirement(readSetting, SewerPipeDiameterMmKey),
            DefaultSlopePercent = ParseSlopePercent(readSetting, DefaultSlopePercentKey),
        };
    }

    /// <summary>מחרוזת-דוגמה לשורת-התיקון בהודעות-שגיאה של מפתח-הקוטר.</summary>
    private const string DiameterExample = "110  (או טווח: 100-110)";

    private static PipeDiameterRequirement ParseDiameterRequirement(Func<string, string?> readSetting, string key)
    {
        string? raw = readSetting(key);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new OfficeConfigException(FormatProblem(
                key, "דרישת קוטר צינור הביוב חסרה או ריקה בקובץ ההגדרות", DiameterExample));
        }

        try
        {
            return PipeDiameterRequirement.Parse(raw);
        }
        catch (FormatException ex)
        {
            throw new OfficeConfigException(FormatProblem(key, ex.Message.TrimEnd('.'), DiameterExample));
        }
    }

    private static double ParseSlopePercent(Func<string, string?> readSetting, string key)
    {
        double value = ParseNumber(readSetting, key, humanName: "שיפוע ברירת המחדל", exampleValue: "1.75");

        if (value <= 0.0 || value >= SlopeSanityCeilingPercent)
        {
            throw new OfficeConfigException(FormatProblem(
                key,
                $"הערך '{Format(value)}' אינו שיפוע תקין - חייב להיות מספר חיובי באחוזים, " +
                $"קטן מ-{SlopeSanityCeilingPercent.ToString("F0", CultureInfo.InvariantCulture)}",
                "1.75"));
        }

        return value;
    }

    private static double ParseNumber(
        Func<string, string?> readSetting, string key, string humanName, string exampleValue)
    {
        string? raw = readSetting(key);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new OfficeConfigException(FormatProblem(
                key, $"{humanName} חסר או ריק בקובץ ההגדרות", exampleValue));
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value))
        {
            throw new OfficeConfigException(FormatProblem(
                key,
                $"הערך '{raw}' אינו מספר תקין (יש להשתמש בנקודה עשרונית, למשל {exampleValue})",
                exampleValue));
        }

        return value;
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatProblem(string key, string problem, string exampleValue)
    {
        string path = OfficeConfig.ResolvedConfigPath is { Length: > 0 } resolved
            ? resolved
            : OfficeConfig.ConfigFileName;

        return $"בעיה בהגדרת המשרד '{key}' בקובץ ההגדרות ({path}): {problem}. " +
            $"פתח את הקובץ בעורך טקסט ותקן את השורה, למשל:  {key} = {exampleValue}";
    }
}
