namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// הממשק הגנרי היחיד שפקודה-ארוכה-משך צריכה להכיר כדי לדווח התקדמות-
/// חיה. לא תלוי-Revit, לא תלוי-Draw-Pipes - כל פקודה עתידית (Place
/// Collectors, Electrical וכו') יכולה לקבל <see cref="IProgressReporter"/>
/// ולהשתמש בו בלי לדעת שום דבר על WPF/חלונות/Dispatcher. ראו
/// docs/progress-infrastructure.md.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// מדווחת עדכון-מצב אחד. חייבת להיקרא **רק** אחרי שהתוצאה האמיתית
    /// כבר ידועה (לא ניחוש/צפי) - ראו docs/progress-infrastructure.md
    /// להסבר למה זה תמיד אפשרי בפועל במימוש הנוכחי. מימושים אמיתיים
    /// (כמו <see cref="ProgressWindowReporter"/>) בולעים כל שגיאה
    /// פנימית בעצמם כהגנת-משנה - אבל הקורא **עדיין** אחראי לעטוף כל
    /// קריאה ב-try/catch משלו (ראו DrawPipesCommand.TryReport) - כשל-UI
    /// לעולם לא יכול להפיל פעולה הנדסית אמיתית.
    /// </summary>
    void Report(ProgressReport update);

    /// <summary>
    /// מסמנת שהפעולה הסתיימה (הצלחה או כישלון כאחד - ההודעה עצמה
    /// קובעת). נקראת בדיוק פעם אחת, בסוף. מימושים אמיתיים אחראים על
    /// טיפול-מסודר בחלון בעקבות זה (ראו <see cref="ProgressWindowReporter"/>) -
    /// לא להשאיר אותו פתוח/תקוע ללא הגבלת-זמן.
    /// </summary>
    void Complete(string finalMessage);
}
