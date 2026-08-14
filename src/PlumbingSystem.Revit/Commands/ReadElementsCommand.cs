using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת בדיקת חיבור בסיסית ("Hello World") בין ה-Add-in ל-Revit. אם לחיצה
/// על הכפתור "בדיקת חיבור" בריבון מצליחה להריץ אותה ולהציג את הדיאלוג, זה
/// מוכיח שה-Add-in נטען כראוי, ה-manifest תקין, וה-References ל-Revit API
/// עובדים - בלי לגעת עדיין במודל או בנתונים אמיתיים.
/// </summary>
/// <remarks>
/// <see cref="TransactionMode.Manual"/> נבחר (ולא <c>Automatic</c>) כי הפקודה
/// עדיין לא משנה את מודל ה-Revit ולכן אינה פותחת Transaction בכלל; עם זאת,
/// <see cref="IExternalCommand"/> מחייב לציין Attribute של TransactionMode
/// גם כשלא נעשה בו שימוש בפועל.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class ReadElementsCommand : IExternalCommand
{
    /// <summary>
    /// מציגה TaskDialog עם ההודעה "עובד", כדי לאשר ויזואלית שהחיבור בין
    /// הכפתור, ה-Add-in ו-Revit API תקין.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל ה-UIApplication הפעיל.</param>
    /// <param name="message">הודעת שגיאה שניתן למלא במקרה של <see cref="Result.Failed"/>.</param>
    /// <param name="elements">קבוצת אלמנטים לסימון שגיאה, רלוונטי רק במקרה כשל.</param>
    /// <returns><see cref="Result.Succeeded"/> לאחר הצגת הדיאלוג בהצלחה.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show("PlumbingSystem", "עובד");
        return Result.Succeeded;
    }
}
