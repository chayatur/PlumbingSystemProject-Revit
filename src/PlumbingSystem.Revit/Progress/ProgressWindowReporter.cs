using System.Threading;

namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// המימוש האמיתי של <see cref="IProgressReporter"/> - מחזיק חלון-WPF
/// לא-מודלי (<see cref="ProgressWindow"/>) ומכריח רענון-מיידי אחרי כל
/// עדכון (<see cref="DispatcherPump.DoEvents"/>), כדי שהחלון יתעדכן
/// **תוך-כדי** הלולאה ההנדסית הסינכרונית, לא רק אחרי שהיא מסתיימת.
/// </summary>
/// <remarks>
/// **הגנת-משנה**: כל method כאן עטופה ב-try/catch משלה - גם אם הקורא
/// (למשל DrawPipesCommand.TryReport) שוכח לעטוף, כשל-UI לא-יתפשט
/// החוצה. זו הגנה **נוספת** על-גבי העטיפה שהקורא כבר עושה, לא תחליף
/// לה - ראו docs/progress-infrastructure.md.
/// </remarks>
public sealed class ProgressWindowReporter : IProgressReporter
{
    /// <summary>
    /// כמה זמן החלון נשאר גלוי עם ההודעה-הסופית לפני שהוא נסגר-אוטומטית -
    /// ראו docs/progress-infrastructure.md סעיף 5.3 לדיון-ההחלטה המלא,
    /// כולל התיקון מ-2026-08-27 (הגרסה הראשונה הסתמכה על
    /// <c>DispatcherTimer</c> שלא בהכרח ממשיך "לתקתק" בזמן שקוד סינכרוני
    /// אחר - כתיבת-קובץ, TaskDialog מודלי - רץ אחרי <see cref="Complete"/>;
    /// עכשיו ההשהיה **חוסמת בפועל**, לפני שהקורא ממשיך הלאה, כך שהיא
    /// מובטחת בכל מקרה).
    /// </summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(4.5);

    private readonly ProgressViewModel _viewModel = new();
    private readonly ProgressWindow _window;

    /// <summary>
    /// יוצרת ומציגה את החלון מיד (לא-מודלי - <c>Show()</c>, לא
    /// <c>ShowDialog()</c>, כדי שהקוד הקורא ימשיך לרוץ). <paramref name="totalItems"/>
    /// כבר אמור להיות ידוע בפועל (לא הערכה) בנקודת-הקריאה - ראו
    /// דוגמת-החיבור ב-DrawPipesCommand (נספר מ-<c>apartments</c> שכבר
    /// נקראו בהצלחה מ-Revit).
    /// </summary>
    public ProgressWindowReporter(string operationName, int totalItems)
    {
        _viewModel.OperationName = operationName;
        _viewModel.ProgressTotal = totalItems;
        _window = new ProgressWindow(_viewModel);
        _window.Show();
        DispatcherPump.DoEvents();
    }

    /// <inheritdoc/>
    public void Report(ProgressReport update)
    {
        try
        {
            _viewModel.Apply(update);
            DispatcherPump.DoEvents();
        }
        catch
        {
            // כשל-UI לעולם לא יכול להפיל את הפעולה ההנדסית - ראו remarks למעלה.
        }
    }

    /// <summary>
    /// מסמנת סיום, מציגה את ההודעה הסופית, ואז **חוסמת בפועל** (עם
    /// pump חוזר, כדי שהחלון עדיין יגיב/יתעדכן כרגיל תוך-כדי) למשך
    /// <see cref="CloseDelay"/> - כדי שהתוצאה הסופית תיראה בוודאות
    /// לפני שהחלון נסגר, בלי תלות במה שהקורא (למשל DrawPipesCommand)
    /// עושה **אחרי** שהיא חוזרת (כתיבת-קובץ, TaskDialog מודלי וכו') -
    /// ראו docs/progress-infrastructure.md. סגירה ידנית (כפתור Close,
    /// או ה-X של החלון) עדיין עובדת בכל רגע, כולל תוך-כדי ההשהיה הזו.
    /// </summary>
    public void Complete(string finalMessage)
    {
        try
        {
            _viewModel.StatusMessage = finalMessage;
            _viewModel.IsComplete = true;

            DateTime deadline = DateTime.UtcNow + CloseDelay;
            while (DateTime.UtcNow < deadline)
            {
                DispatcherPump.DoEvents();
                Thread.Sleep(50);
            }

            _window.Close();
        }
        catch
        {
            // כשל-UI לעולם לא יכול להפיל את הפעולה ההנדסית - ראו remarks למעלה.
            // כולל המקרה שהמשתמש/ת כבר סגר/ה את החלון ידנית תוך-כדי ההשהיה -
            // Close() על חלון שכבר נסגר לא-קריטי, נבלע כאן כמו כל כשל אחר.
        }
    }
}
