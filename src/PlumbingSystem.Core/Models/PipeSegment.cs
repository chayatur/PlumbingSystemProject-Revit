namespace PlumbingSystem.Core.Models;

/// <summary>
/// מייצגת מקטע צינור בודד - בין אסלה לקולטן, או בין קולטן לקולטן
/// הכללי. שדות המידות (<see cref="DiameterMm"/>, <see cref="SlopePercent"/>)
/// לא עוברים ולידציה הנדסית כאן בכוונה (למשל טווח שיפוע 1.5%-2.0%
/// לפי חוק 2, או קוטר סטנדרטי כמו 110 מ"מ למקטע אסלה) - זו אחריות של
/// שלב ולידציה נפרד, כדי שמודל הנתונים עצמו יישאר פשוט וניתן לבנייה
/// גם עם ערכים שעדיין לא אושרו הנדסית (למשל בזמן ניסוי אלגוריתם מיקום).
/// </summary>
public sealed record PipeSegment
{
    /// <summary>מזהה ייחודי של מקטע הצינור.</summary>
    public string Id { get; init; }

    /// <summary>נקודת ההתחלה של המקטע (בד"כ אסלה או קולטן).</summary>
    public Point3D StartPoint { get; init; }

    /// <summary>נקודת הסיום של המקטע (בד"כ קולטן).</summary>
    public Point3D EndPoint { get; init; }

    /// <summary>קוטר הצינור במילימטרים (למקטע אסלה: 110).</summary>
    public double DiameterMm { get; init; }

    /// <summary>שיפוע הצינור באחוזים (טווח תקף לפי חוק 2: 1.5-2.0).</summary>
    public double SlopePercent { get; init; }

    /// <summary>
    /// יוצר מקטע צינור חדש. זורק <see cref="ArgumentException"/> אם
    /// <paramref name="id"/> ריק. אין כאן בדיקה על <paramref name="diameterMm"/>
    /// או <paramref name="slopePercent"/> - אלה ערכים הנדסיים שנבדקים
    /// בשלב ולידציה נפרד, לא בבניית האובייקט.
    /// </summary>
    public PipeSegment(string id, Point3D startPoint, Point3D endPoint, double diameterMm, double slopePercent)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id של מקטע צינור לא יכול להיות ריק.", nameof(id));
        }

        Id = id;
        StartPoint = startPoint;
        EndPoint = endPoint;
        DiameterMm = diameterMm;
        SlopePercent = slopePercent;
    }
}
