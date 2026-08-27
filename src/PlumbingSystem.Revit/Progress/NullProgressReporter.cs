namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// מימוש ריק (no-op) של <see cref="IProgressReporter"/> - כדי שכל קוד
/// שמקבל <see cref="IProgressReporter"/> יוכל לקבל ברירת-מחדל בטוחה
/// (למשל אם יצירת החלון האמיתי נכשלה) בלי בדיקות-null בכל מקום-קריאה.
/// לעולם לא זורקת, לעולם לא עושה כלום - "כשל-UI לעולם לא יכול להפיל
/// פעולה הנדסית" מתחיל כבר כאן, לפני שמגיעים בכלל למימוש-החלון.
/// </summary>
public sealed class NullProgressReporter : IProgressReporter
{
    /// <summary>המופע היחיד (singleton) - אין טעם ביותר מאחד, כי הוא לא מחזיק שום state.</summary>
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter()
    {
    }

    /// <inheritdoc/>
    public void Report(ProgressReport update)
    {
    }

    /// <inheritdoc/>
    public void Complete(string finalMessage)
    {
    }
}
