using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Core.Geometry;

/// <summary>
/// חישובים גיאומטריים טהורים (בלי שום תלות ב-Revit API) שמשמשים את
/// לוגיקת הדומיין - למשל זיהוי "שירותים בודדים" לפי מרחק מהאלמנטים
/// הרטובים בדירה.
/// </summary>
public static class GeometryUtils
{
    /// <summary>
    /// מרחק אוקלידי דו-ממדי בין שתי נקודות, לפי X,Y **בלבד** - ה-Z
    /// (גובה) לא נכנס לחישוב בכוונה, כי ההשוואה בין אלמנטים "רטובים"
    /// לאסלות היא השוואת מיקום במפת הקומה, לא הפרשי גובה (למשל בין
    /// אלמנט על הרצפה לאלמנט קבוע בקיר בגובה שונה).
    /// </summary>
    public static double Distance2D(Point3D a, Point3D b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
