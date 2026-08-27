using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Inspector;

/// <summary>
/// רושמת את "Connection Inspector" כפאנל-מעוגן אמיתי (לא חלון-WPF צף) -
/// נרשמת פעם אחת ב-<see cref="App.OnStartup"/>, לפני שנפתח מסמך כלשהו.
/// Revit מוסיף אותו אוטומטית לרשימת ה-Panels תחת View → User Interface -
/// אין צורך בכפתור-ריבון נפרד.
/// </summary>
public sealed class ConnectionInspectorPaneProvider : IDockablePaneProvider
{
    /// <summary>
    /// GUID קבוע-לתמיד - Revit עשוי לשמור state (מיקום/גודל/נראות) לפי
    /// ה-Guid הזה בין הרצות. **אסור לשנות** אחרי ההטמעה הראשונה - שינוי
    /// שלו ייצור פאנל "חדש" מבחינת Revit, לא ימשיך את ה-state הקיים.
    /// </summary>
    public static readonly DockablePaneId PaneId = new(new Guid("6f3a1e2c-9b4d-4a7a-8e2a-2a0c8f6a9e11"));

    /// <summary>ה-ViewModel המשותף - נגיש כדי ש-<see cref="App"/> יוכל לעדכן אותו מתוך <c>SelectionChanged</c>.</summary>
    public ConnectionInspectorViewModel ViewModel { get; } = new();

    private readonly ConnectionInspectorView _view;

    /// <summary>יוצרת את ה-View והחיווט הראשוני שלו ל-ViewModel המשותף - נקראת פעם אחת, ב-App.OnStartup.</summary>
    public ConnectionInspectorPaneProvider()
    {
        _view = new ConnectionInspectorView(ViewModel);
    }

    /// <inheritdoc/>
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = _view;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
        };
    }
}
