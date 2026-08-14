using Autodesk.Revit.DB;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Revit;

/// <summary>
/// ממירה בין המערכת הפנימית של Revit (**רגל - Feet, תמיד**, בלי קשר
/// ליחידות התצוגה של הפרויקט - זו עובדה קבועה של Revit API) לבין
/// המטרים שכל <see cref="Point3D"/> ב-PlumbingSystem.Core מניח שהוא
/// מיוצג בהם (למשל <c>CollectorLocator.MaxDistanceMeters</c>,
/// <c>WallEdgeSnapper.MaxSearchRadiusMeters</c>).
/// </summary>
/// <remarks>
/// **באג שתוקן**: עד לשלב הזה, ההמרות בין <see cref="XYZ"/> ל-
/// <see cref="Point3D"/> (ב-<c>RevitModelReader</c> וב-
/// <c>CollectorPlacementService</c>) העתיקו את הקואורדינטות
/// **כמו שהן**, בלי שום המרת יחידות - כלומר כל מרחק שחושב מנתוני
/// Revit אמיתיים היה בפועל ברגל, לא במטר, למרות שכל הקבועים
/// (4.0, 3.0, 2.0 וכו') תועדו ונקראו "מטרים". המשמעות: כל בדיקת-סף
/// מרחק (למשל "מקסימום 4.0 מ' מקולטן") נאכפה בפועל כ-4.0 **רגל**
/// (כ-1.22 מ') - פי ~3.28 מחמיר מהמיועד. זה לא השפיע על **מיקום**
/// אלמנטים (זה נשאר עקבי - רגל-פנימה, רגל-החוצה, בלי המרה בשום כיוון),
/// רק על משמעות-הסף של השוואות מרחק. המחלקה הזו היא נקודת ההמרה
/// היחידה - כל קוד שממיר XYZ&lt;-&gt;Point3D צריך לעבור דרכה.
/// </remarks>
internal static class RevitUnitConversion
{
    /// <summary>ממירה XYZ של Revit (רגל) ל-Point3D של Core (מטרים).</summary>
    public static Point3D ToCorePoint(XYZ point) => new(
        UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Meters),
        UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Meters),
        UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Meters));

    /// <summary>ממירה Point3D של Core (מטרים) ל-XYZ של Revit (רגל).</summary>
    public static XYZ ToRevitPoint(Point3D point) => new(
        UnitUtils.ConvertToInternalUnits(point.X, UnitTypeId.Meters),
        UnitUtils.ConvertToInternalUnits(point.Y, UnitTypeId.Meters),
        UnitUtils.ConvertToInternalUnits(point.Z, UnitTypeId.Meters));
}
