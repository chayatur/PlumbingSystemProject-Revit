using System.Windows.Threading;

namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// טכניקת ה-"DoEvents" הסטנדרטית ל-WPF - <c>Dispatcher.PushFrame</c>
/// עם frame בודד שיוצא-מעצמו אחרי ה-callback הבא בתור. נחוצה כי
/// <c>IExternalCommand.Execute</c> רץ סינכרונית על ה-thread הראשי של
/// Revit: בלי הדחיפה הידנית הזו, ה-Dispatcher של WPF (האחראי על
/// ציור/רענון) לא מקבל שום הזדמנות לרוץ תוך-כדי שהלולאה ההנדסית
/// עסוקה - והחלון היה נראה קפוא/לבן עד סוף הריצה כולה, בדיוק מה
/// שהתשתית הזו נועדה למנוע. ראו docs/progress-infrastructure.md.
/// </summary>
internal static class DispatcherPump
{
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(ExitFrame),
            frame);
        Dispatcher.PushFrame(frame);
    }

    private static object? ExitFrame(object frame)
    {
        ((DispatcherFrame)frame).Continue = false;
        return null;
    }
}
