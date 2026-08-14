namespace PlumbingSystem.Core.Models;

/// <summary>
/// מייצגת דירה בודדת ואת האסלות שבתוכה. <c>record</c> מאותה סיבה כמו
/// שאר מודלי ה-domain כאן: בעיקר נתונים, שוויון מבני שימושי לבדיקות.
/// </summary>
public sealed record Apartment
{
    /// <summary>מזהה ייחודי של הדירה.</summary>
    public string Id { get; init; }

    /// <summary>מספר הקומה שבה נמצאת הדירה.</summary>
    public int FloorNumber { get; init; }

    /// <summary>
    /// כל האסלות שבתוך הדירה. <c>List</c> (לא <c>IReadOnlyList</c>)
    /// לפי בקשה מפורשת - נוח להוסיף/להסיר אסלות תוך כדי בניית המודל,
    /// לפני שהוא "סופי".
    /// </summary>
    public List<ToiletFixture> Fixtures { get; init; }

    /// <summary>
    /// יוצר דירה חדשה. זורק <see cref="ArgumentException"/> אם
    /// <paramref name="id"/> ריק. <paramref name="fixtures"/> אופציונלי -
    /// אם לא סופק, הדירה מתחילה עם רשימת אסלות ריקה.
    /// </summary>
    public Apartment(string id, int floorNumber, List<ToiletFixture>? fixtures = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id של דירה לא יכול להיות ריק.", nameof(id));
        }

        Id = id;
        FloorNumber = floorNumber;
        Fixtures = fixtures ?? new List<ToiletFixture>();
    }
}
