namespace PlumbingSystem.Core.Domain;

/// <summary>
/// הפרמטרים ההנדסיים ה"חיצוניים" של חישוב מסלול-הצינור: קוטר המקטע
/// (מ"מ) ושיפוע ברירת-המחדל (אחוזים). עד PIPE Step 3 אלה היו
/// <c>const</c> בתוך <see cref="PipeRouteCalculator"/>
/// (<see cref="PipeRouteCalculator.PipeDiameterMm"/> = 110,
/// <see cref="PipeRouteCalculator.DefaultSlopePercent"/> = 1.75); כעת הם
/// מגיעים **מבחוץ** - ה-overload-ים של <c>PipeRouteCalculator.Calculate</c> /
/// <c>CalculateDetour</c> / <c>CalculateStaggeredDetour</c> שמקבלים
/// <see cref="PipeRouteParameters"/> כפרמטר.
/// </summary>
/// <remarks>
/// **Core לא יודע מאיפה הערכים באים.** בפועל שכבת-Revit
/// (<c>DrawPipesCommand</c>) טוענת אותם מקובץ-ההגדרות המשרדי דרך
/// <c>PlumbingSystem.Revit.Config.PipingOfficeSettings</c> - אבל
/// <see cref="PipeRouteParameters"/> הוא <c>double</c>-ים פשוטים בלבד,
/// בלי שום תלות ב-RevitAPI / קובץ / נתיב / OfficeConfig.
///
/// **הטווח ההנדסי (1.5%-2.0%, חוק 2) עדיין נאכף ב-<c>PipeRouteCalculator.Calculate</c>**
/// (על השיפוע ה**מחושב** בפועל) - לא כאן. כאן רק בדיקת-שפיות בסיסית
/// (חיובי וסופי) שמגינה על Core מפני קלט מנוון גם אם הקורא לא סינן.
/// </remarks>
public sealed record PipeRouteParameters
{
    private readonly double _diameterMm;
    private readonly double _slopePercent;

    /// <summary>קוטר המקטע במ"מ. חייב להיות חיובי וסופי.</summary>
    public required double DiameterMm
    {
        get => _diameterMm;
        init => _diameterMm = Validated(value, nameof(DiameterMm));
    }

    /// <summary>
    /// שיפוע ברירת-המחדל באחוזים. חייב להיות חיובי וסופי; הטווח ההנדסי
    /// המדויק (1.5%-2.0%) נבדק בנפרד על השיפוע ה**מחושב** ב-<c>PipeRouteCalculator.Calculate</c>.
    /// </summary>
    public required double SlopePercent
    {
        get => _slopePercent;
        init => _slopePercent = Validated(value, nameof(SlopePercent));
    }

    /// <summary>
    /// הערכים ההיסטוריים שהיו <c>const</c> לפני PIPE Step 3
    /// (<see cref="PipeRouteCalculator.PipeDiameterMm"/> = 110 מ"מ,
    /// <see cref="PipeRouteCalculator.DefaultSlopePercent"/> = 1.75%).
    /// משמשים את ה-overload-ים חסרי-הפרמטר (בדיקות-יחידה, ופקודות-אבחון
    /// זמניות) כדי לא לשבור אותם - **לא** נתיב-ההרצה של יצירת-הצינורות
    /// בפועל, שעובר תמיד דרך קובץ-ההגדרות המשרדי.
    /// </summary>
    public static PipeRouteParameters Default { get; } = new()
    {
        DiameterMm = PipeRouteCalculator.PipeDiameterMm,
        SlopePercent = PipeRouteCalculator.DefaultSlopePercent,
    };

    private static double Validated(double value, string propertyName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                propertyName, value, $"{propertyName} של מסלול-צינור חייב להיות מספר חיובי וסופי.");
        }

        return value;
    }
}
