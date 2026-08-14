namespace PlumbingSystem.Core.Models;

/// <summary>
/// מייצגת אסלה בודדת - נקודת המוצא של כל מקטע צנרת ביוב במודל. זהו
/// <c>record</c> (לא <c>class</c>) כי מדובר בעיקר בנתונים בלי state
/// משתנה: שוויון מבני (שתי אסלות עם אותם ערכים שוות) שימושי לבדיקות
/// והשוואות, וה-<c>ToString()</c> האוטומטי של records מציג את כל
/// השדות בפורמט קריא בלי מאמץ.
/// </summary>
/// <remarks>
/// כל המאפיינים הם <c>init</c> (לא <c>get</c> בלבד) כדי לאפשר יצירת
/// עותקים משונים בעזרת <c>with</c> (למשל בהמשך, כשמשייכים ElementId
/// אמיתי מ-Revit). המשמעות: הוולידציה שבקונסטרוקטור לא רצה שוב אם
/// יוצרים עותק דרך <c>with</c> - זו החלטה מכוונת (עדיפות לנוחות
/// <c>with</c> על פני אכיפת ולידציה בכל עותק), לא פספוס.
/// </remarks>
public sealed record ToiletFixture
{
    /// <summary>מזהה ייחודי של האסלה - יתאים בהמשך ל-Revit ElementId.</summary>
    public string Id { get; init; }

    /// <summary>מיקום האסלה במרחב.</summary>
    public Point3D Location { get; init; }

    /// <summary>מזהה הדירה שאליה שייכת האסלה.</summary>
    public string ApartmentId { get; init; }

    /// <summary>
    /// האם זו אסלה בשירותי אורחים (ולא באמבטיה/מקלחת). רלוונטי כי לפי
    /// הכללים ההנדסיים קו הקולטן היוצא מהדירה מתחיל דווקא מנקודה זו.
    /// </summary>
    public bool IsGuestBathroom { get; init; }

    /// <summary>
    /// יוצר אסלה חדשה. זורק <see cref="ArgumentException"/> אם
    /// <paramref name="id"/> או <paramref name="apartmentId"/> ריקים -
    /// זו ולידציה בסיסית של שלמות הנתונים בלבד, לא ולידציה הנדסית.
    /// </summary>
    public ToiletFixture(string id, Point3D location, string apartmentId, bool isGuestBathroom)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id של אסלה לא יכול להיות ריק.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(apartmentId))
        {
            throw new ArgumentException("ApartmentId לא יכול להיות ריק.", nameof(apartmentId));
        }

        Id = id;
        Location = location;
        ApartmentId = apartmentId;
        IsGuestBathroom = isGuestBathroom;
    }
}
