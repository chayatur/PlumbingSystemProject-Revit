namespace PlumbingSystem.Revit.Config;

/// <summary>
/// נזרקת כאשר קובץ-ההגדרות המשרדי (<see cref="OfficeConfig.ConfigFileName"/>)
/// מכיל ערך **חסר או לא-תקין** עבור הגדרה שהתוכנה מחייבת (למשל קוטר
/// צינור הביוב או שיפוע ברירת-המחדל - ראו <see cref="PipingOfficeSettings"/>).
/// </summary>
/// <remarks>
/// **אין <c>fallback</c> שקט**: ההודעה מנוסחת כדי שמנהל-BIM / המשרד
/// יוכל לתקן את הקובץ בעצמו בעורך-טקסט, בלי Visual Studio / קוד-מקור /
/// rebuild - היא מציינת את שם-ההגדרה, את הבעיה, ואת נתיב-הקובץ בפועל.
/// עדיף כשל ברור מאשר קוטר/שיפוע שגוי-בשקט (אותו עיקרון-זהירות כמו
/// שאר הפרויקט).
/// </remarks>
public sealed class OfficeConfigException : Exception
{
    /// <summary>יוצרת חריגה עם הודעה מוכנה-להצגה למשתמש-הקצה במשרד.</summary>
    /// <param name="message">תיאור הבעיה + מה לתקן בקובץ ההגדרות.</param>
    public OfficeConfigException(string message)
        : base(message)
    {
    }
}
