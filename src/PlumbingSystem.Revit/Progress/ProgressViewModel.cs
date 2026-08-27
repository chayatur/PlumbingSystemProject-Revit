using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// ה-ViewModel של <see cref="ProgressWindow"/> - מחזיק את המצב-הנוכחי
/// ואת רשימת-השורות הגוללת, ומיישם <see cref="INotifyPropertyChanged"/>
/// כדי שה-Binding ב-XAML יתעדכן. לא תלוי-Revit ולא תלוי-פקודה ספציפית -
/// כל הידע ההנדסי מגיע כבר-מוכן דרך <see cref="Apply"/>.
/// </summary>
public sealed class ProgressViewModel : INotifyPropertyChanged
{
    private string _operationName = string.Empty;
    private string _floor = string.Empty;
    private string _apartment = string.Empty;
    private string _currentItem = string.Empty;
    private int _progressCurrent;
    private int _progressTotal;
    private int _successCount;
    private int _manualReviewCount;
    private string _statusMessage = string.Empty;
    private bool _isComplete;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>שורות-ה-log הגוללות - נוספת שורה אחת לכל עדכון שמכיל <see cref="ProgressReport.StatusMessage"/>.</summary>
    public ObservableCollection<ProgressRow> Rows { get; } = new();

    /// <summary>שם-הפעולה שמוצג בכותרת החלון (למשל "Draw Pipes").</summary>
    public string OperationName { get => _operationName; set => SetField(ref _operationName, value); }

    /// <summary>הקומה הנוכחית-לתצוגה - טקסט חופשי, לא בהכרח מספר (תלוי-קורא).</summary>
    public string Floor { get => _floor; set => SetField(ref _floor, value); }

    /// <summary>מזהה-הדירה הנוכחית-לתצוגה.</summary>
    public string Apartment { get => _apartment; set => SetField(ref _apartment, value); }

    /// <summary>מזהה-הפריט (למשל אסלה/קולטן) שהעדכון האחרון מתייחס אליו.</summary>
    public string CurrentItem { get => _currentItem; set => SetField(ref _currentItem, value); }

    /// <summary>כמה פריטים כבר עובדו - למד-ההתקדמות.</summary>
    public int ProgressCurrent { get => _progressCurrent; set => SetField(ref _progressCurrent, value); }

    /// <summary>סה"כ פריטים לעיבוד - למד-ההתקדמות.</summary>
    public int ProgressTotal { get => _progressTotal; set => SetField(ref _progressTotal, value); }

    /// <summary>מונה-הצלחות חי - המשמעות ההנדסית נקבעת כולה על ידי הקורא, ראו <see cref="ProgressReport"/>.</summary>
    public int SuccessCount { get => _successCount; set => SetField(ref _successCount, value); }

    /// <summary>מונה דורש-בדיקה חי - המשמעות ההנדסית נקבעת כולה על ידי הקורא, ראו <see cref="ProgressReport"/>.</summary>
    public int ManualReviewCount { get => _manualReviewCount; set => SetField(ref _manualReviewCount, value); }

    /// <summary>הודעת-הסטטוס האחרונה - גם מוצגת בשורה התחתונה וגם נכנסת לשורה החדשה ב-<see cref="Rows"/>.</summary>
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    /// <summary>מתעדכן ל-true רק על ידי <see cref="ProgressWindowReporter.Complete"/> - קובע את נוסח-הכותרת הסופי ב-XAML.</summary>
    public bool IsComplete { get => _isComplete; set => SetField(ref _isComplete, value); }

    /// <summary>
    /// מיישמת עדכון-התקדמות בודד: כל שדה לא-null ב-<paramref name="update"/>
    /// דורס את הערך הקיים; שדה null משאיר את הקיים כמו שהוא (ראו התיעוד
    /// על <see cref="ProgressReport"/>). אם יש <see cref="ProgressReport.StatusMessage"/> -
    /// גם נוספת שורה חדשה ל-<see cref="Rows"/> (מצב-הדירה/הפריט הנוכחיים
    /// **אחרי** שהם כבר עודכנו למעלה, כך שהשורה משקפת את ההקשר הנכון).
    /// </summary>
    public void Apply(ProgressReport update)
    {
        if (update.OperationName is not null)
        {
            OperationName = update.OperationName;
        }

        if (update.Floor is not null)
        {
            Floor = update.Floor;
        }

        if (update.Apartment is not null)
        {
            Apartment = update.Apartment;
        }

        if (update.CurrentItem is not null)
        {
            CurrentItem = update.CurrentItem;
        }

        if (update.ProgressCurrent is int current)
        {
            ProgressCurrent = current;
        }

        if (update.ProgressTotal is int total)
        {
            ProgressTotal = total;
        }

        if (update.SuccessCount is int successCount)
        {
            SuccessCount = successCount;
        }

        if (update.ManualReviewCount is int manualReviewCount)
        {
            ManualReviewCount = manualReviewCount;
        }

        if (update.StatusMessage is not null)
        {
            StatusMessage = update.StatusMessage;
            Rows.Add(new ProgressRow(Apartment, CurrentItem, update.StatusMessage));
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
