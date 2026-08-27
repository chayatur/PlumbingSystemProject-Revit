using System.Windows;
using System.Windows.Controls;

namespace PlumbingSystem.Revit.Inspector;

/// <summary>
/// ה-<c>UserControl</c> (לא <c>Window</c> - <see cref="Autodesk.Revit.UI.DockablePaneProviderData.FrameworkElement"/>
/// דורש <c>FrameworkElement</c> רגיל, לא חלון עצמאי) שמוצג בתוך הפאנל
/// המעוגן. Passive לחלוטין - כל הלוגיקה ב-ViewModel.
/// </summary>
public partial class ConnectionInspectorView : UserControl
{
    /// <summary>יוצרת את ה-View ומחברת אותו ל-<paramref name="viewModel"/> הנתון (Binding).</summary>
    public ConnectionInspectorView(ConnectionInspectorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void HighlightButton_Click(object sender, RoutedEventArgs e)
    {
        (DataContext as ConnectionInspectorViewModel)?.Highlight();
    }
}
