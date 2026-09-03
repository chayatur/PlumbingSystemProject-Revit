using System.Reflection;
using System.Text;

namespace PlumbingSystem.Revit.Config;

/// <summary>
/// קורא את קובץ-ההגדרות המשרדי (<c>PlumbingSystem.OfficeConfig.txt</c>,
/// לצד ה-DLL) שממפה **מפתחות סטנדרטיים שהתוכנה מגדירה** לערכים הספציפיים
/// למודל ה-Revit של המשרד/הפרויקט. כך לוגיקת-הניתוב אינה מכילה שמות-
/// אלמנטים תלויי-פרויקט - היא שואלת את המפתח הסטנדרטי (למשל
/// <see cref="ConcreteWallKey"/>), והמשרד מתאים את הערך בקובץ.
/// </summary>
/// <remarks>
/// **אותה פילוסופיה כמו <c>Is_Toilet</c>** (ראו <c>RevitModelReader.IsToiletFixture</c>):
/// שם התוכנה קובעת שם-פרמטר סטנדרטי (<c>"Is_Toilet"</c>) והמשרד מתאים
/// אותו למודל ב-Revit (Shared Parameter על Type); כאן התוכנה קובעת
/// מפתח (<c>"Concrete wall"</c>) והמשרד מתאים אותו בקובץ-הגדרות.
///
/// **פורמט הקובץ**: שורות <c>Key = Value</c> (Value יכול להיות רשימה
/// מופרדת-פסיקים); שורות שמתחילות ב-<c>#</c> הן הערות; שורות ריקות
/// מדולגות; קידוד UTF-8.
///
/// **קובץ/מפתח/ערך חסרים**: <see cref="GetValues"/> מחזירה רשימה ריקה,
/// וכל קורא מחליט מה זה אומר עבורו. לדוגמה
/// <see cref="WallPenetrationPolicy"/>: "אף WallType לא ידוע כבטון" →
/// הכל מותר לחצייה (הכלל העסקי: "כל קיר שאינו בטון מותר").
///
/// הקובץ נקרא **פעם אחת** (Lazy) ונשמר במטמון לכל אורך חיי ה-process
/// של Revit.
/// </remarks>
public static class OfficeConfig
{
    /// <summary>
    /// מפתח סטנדרטי: שם/שמות ה-<c>WallType</c> שהם קיר-בטון (שנתיב-צינור
    /// **אסור** לחצות). המשרד ממפה אותו בקובץ-ההגדרות לערך שקיים במודל
    /// שלו (למשל <c>Concrete wall = קיר בטון 20</c>). כל <c>WallType</c>
    /// שאינו ממופה כאן → אינו בטון → מותר לחצייה.
    /// </summary>
    public const string ConcreteWallKey = "Concrete wall";

    /// <summary>שם קובץ-ההגדרות, לצד ה-DLL של התוסף.</summary>
    public const string ConfigFileName = "PlumbingSystem.OfficeConfig.txt";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> Entries = new(LoadEntries);

    /// <summary>
    /// הערכים שהמשרד מיפה למפתח הסטנדרטי <paramref name="standardKey"/>
    /// (התאמת-מפתח case-insensitive; כל ערך עבר <c>Trim</c>). רשימה ריקה
    /// אם הקובץ / המפתח / הערך חסרים.
    /// </summary>
    public static IReadOnlyList<string> GetValues(string standardKey)
    {
        ArgumentNullException.ThrowIfNull(standardKey);

        return Entries.Value.TryGetValue(standardKey.Trim(), out IReadOnlyList<string>? values)
            ? values
            : Array.Empty<string>();
    }

    /// <summary>
    /// הנתיב המלא לקובץ-ההגדרות שנעשה בו שימוש (או <c>null</c> אם לא
    /// ניתן לאתר אותו) - לצורך דיווח/אבחון בלבד.
    /// </summary>
    public static string? ResolvedConfigPath => ResolveConfigPath();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadEntries()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        string? path = ResolveConfigPath();
        if (path is null || !File.Exists(path))
        {
            return result;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception)
        {
            return result;
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = line[..equalsIndex].Trim();
            string[] values = line[(equalsIndex + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (key.Length > 0 && values.Length > 0)
            {
                result[key] = values;
            }
        }

        return result;
    }

    private static string? ResolveConfigPath()
    {
        try
        {
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
            string? assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                return Path.Combine(assemblyDir, ConfigFileName);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            return Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
