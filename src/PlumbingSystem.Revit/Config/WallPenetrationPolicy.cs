using Autodesk.Revit.DB;

namespace PlumbingSystem.Revit.Config;

/// <summary>
/// נקודת-ההגדרה **היחידה** בקוד לשאלה "האם מותר לצינור-ביוב לחדור לתוך
/// גוף סוג-הקיר הזה, בדרך אל הקולטן". משמשת את
/// <c>DrawPipesCommand.FindCollectorWallPenetration</c> כתנאי **נוסף**
/// (AND) על הכלל הגיאומטרי הקיים (קיר-מכיל-קולטן + מקטע-אחרון-שמסתיים-
/// בקולטן + מרחק-חדירה חסום). היא **אינה** משנה את הכלל הגיאומטרי - רק
/// מסננת מראש אילו סוגי-קיר בכלל מועמדים לחדירה. קיר שההגדרה כאן
/// אוסרת עליו נשאר <c>obstruction</c> רגיל (→ detour → Manual Engineering),
/// בדיוק כמו קיר שאינו מכיל את הקולטן.
/// </summary>
/// <remarks>
/// **הכלל העסקי** (הוגדר על ידי המנהלת, לא סיווג-אוטומטי של התוכנה):
/// <list type="bullet">
///   <item>קיר בטון → **אסור** לנתיב לחצות.</item>
///   <item>כל קיר שאינו בטון → **מותר** לנתיב לחצות.</item>
/// </list>
///
/// **זיהוי "קיר בטון" - גנרי, דרך קובץ-הגדרות משרדי**: התוכנה מגדירה
/// מפתח סטנדרטי (<see cref="OfficeConfig.ConcreteWallKey"/> = <c>"Concrete wall"</c>),
/// והמשרד ממפה אותו לשם/שמות ה-<c>WallType</c> שקיימים במודל שלו
/// (למשל <c>Concrete wall = קיר בטון 20</c>). <see cref="IsPenetrationAllowed"/>
/// משווה את שם-ה-<c>WallType</c> מול הרשימה מ-<see cref="OfficeConfig"/>.
/// **אין** בלוגיקה שום שם-<c>WallType</c> קשיח, שום <c>ElementId</c>,
/// שום heuristic על <c>MaterialClass</c>/<c>Material.Name</c>, ושום
/// Allowlist - רק "האם שם-הסוג ממופה למפתח 'Concrete wall'".
///
/// **בהפצה**: <see cref="IsPenetrationAllowed"/> היא ה-seam היחיד של
/// לוגיקת-הניתוב. מקור-ההגדרה (<see cref="OfficeConfig"/>) אפשר להחליף
/// בעתיד (למשל Shared Parameter מסוג Type על קטגוריית Walls - ראו
/// docs/pipe-rca-chain.md חלק ט') בלי לשנות את חתימת המתודה או את
/// <c>DrawPipesCommand</c>.
/// </remarks>
public static class WallPenetrationPolicy
{
    /// <summary>
    /// <c>true</c> אם מותר לצינור לחדור לגוף סוג-הקיר של
    /// <paramref name="wall"/> - כלומר שם-ה-<c>WallType</c> שלו **אינו**
    /// ממופה למפתח הסטנדרטי <see cref="OfficeConfig.ConcreteWallKey"/>
    /// בקובץ-ההגדרות המשרדי. <c>false</c> אם הוא ממופה (= קיר בטון).
    /// אין כאן שום בדיקת חומר/מבנה - רק התאמת-שם מול קובץ-ההגדרות.
    /// </summary>
    /// <param name="wall">הקיר שסוג-ה-Type שלו נבדק מול קובץ-ההגדרות.</param>
    /// <param name="reason">הסבר קריא לתוצאה (שם-הסוג, והאם ממופה כ"בטון") - לדיווח-אבחון.</param>
    public static bool IsPenetrationAllowed(Wall wall, out string reason)
    {
        string? typeName = wall?.WallType?.Name?.Trim();

        if (string.IsNullOrEmpty(typeName))
        {
            // שם-הסוג לא נקרא - מקרה-קצה. לפי הכלל העסקי (כל קיר שאינו
            // בטון → מותר), ואי-אפשר לאשר שהוא בטון → מאשרים, ומציינים ב-reason.
            reason = "WallType name unavailable - allowed by default (business rule: allow unless concrete)";
            return true;
        }

        IReadOnlyList<string> concreteWallTypeNames = OfficeConfig.GetValues(OfficeConfig.ConcreteWallKey);
        bool isConcrete = concreteWallTypeNames.Any(name => name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

        if (isConcrete)
        {
            reason = $"WallType \"{typeName}\" is mapped to the standard '{OfficeConfig.ConcreteWallKey}' key in the office config (concrete) - penetration NOT allowed";
            return false;
        }

        reason = concreteWallTypeNames.Count == 0
            ? $"WallType \"{typeName}\" - no '{OfficeConfig.ConcreteWallKey}' mapping in the office config - allowed (no wall type is known to be concrete)"
            : $"WallType \"{typeName}\" is not mapped to '{OfficeConfig.ConcreteWallKey}' - not concrete, allowed";
        return true;
    }
}
