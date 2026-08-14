namespace PlumbingSystem.Core.Models;

/// <summary>
/// מייצגת קולטן - נקודת איסוף שאליה מתחברים מקטעי צנרת מדירה אחת או
/// יותר לפני שהם ממשיכים לקולטן הכללי/לביוב הציבורי. אין כלל קשיח על
/// כמה דירות מתחברות לקולטן אחד - זו החלטה שנגזרת מהמיקום ההנדסי
/// האופטימלי (שלב נפרד), ולכן <see cref="ConnectedApartmentIds"/> הוא
/// רשימה ולא שדה יחיד.
/// </summary>
public sealed record CollectorPoint
{
    /// <summary>מזהה ייחודי של הקולטן.</summary>
    public string Id { get; init; }

    /// <summary>מיקום הקולטן במרחב.</summary>
    public Point3D Location { get; init; }

    /// <summary>
    /// מזהי כל הדירות שמתחברות לקולטן הזה - דירה אחת (קולטן דירתי) או
    /// כמה דירות (קולטן משותף לקומה), לפי מה שיוצא הכי מוצלח הנדסית.
    /// </summary>
    public List<string> ConnectedApartmentIds { get; init; }

    /// <summary>מזהי כל האסלות שמתנקזות דרך הקולטן הזה.</summary>
    public List<string> ConnectedFixtureIds { get; init; }

    /// <summary>
    /// יוצר קולטן חדש. זורק <see cref="ArgumentException"/> אם
    /// <paramref name="id"/> ריק, או אם <paramref name="connectedApartmentIds"/>
    /// ריק/null - קולטן שלא מחובר לאף דירה הוא שגיאת נתונים בסיסית
    /// (לא מצב הנדסי אפשרי), ולכן נבדק כאן ולא נדחה לשלב הולידציה
    /// ההנדסית.
    /// </summary>
    public CollectorPoint(
        string id,
        Point3D location,
        List<string> connectedApartmentIds,
        List<string>? connectedFixtureIds = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id של קולטן לא יכול להיות ריק.", nameof(id));
        }

        if (connectedApartmentIds is null || connectedApartmentIds.Count == 0)
        {
            throw new ArgumentException(
                "קולטן חייב להיות מחובר לפחות לדירה אחת.",
                nameof(connectedApartmentIds));
        }

        Id = id;
        Location = location;
        ConnectedApartmentIds = connectedApartmentIds;
        ConnectedFixtureIds = connectedFixtureIds ?? new List<string>();
    }
}
