using System.Collections.Specialized;
using System.Windows;

namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// חלון-ההתקדמות-החי עצמו - passive לחלוטין: לא נוגע במסמך Revit,
/// לא קורא ל-API של Revit בכלל, רק מציג את מה ש-<see cref="ProgressViewModel"/>
/// (דרך Binding) אומר לו. גלילה-אוטומטית לשורה האחרונה כשמתווספת
/// שורה חדשה, וכפתור-סגירה ידני שעובד תמיד (גם באמצע-ריצה - סגירת
/// החלון הזה לא משפיעה על ה-Transaction האמיתי, ראו docs/progress-infrastructure.md).
/// </summary>
public partial class ProgressWindow : Window
{
    /// <summary>יוצרת את החלון ומחברת אותו ל-<paramref name="viewModel"/> הנתון (Binding + מעקב-גלילה).</summary>
    public ProgressWindow(ProgressViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Rows.CollectionChanged += Rows_CollectionChanged;
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (RowsList.Items.Count > 0)
        {
            RowsList.ScrollIntoView(RowsList.Items[^1]);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
