using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Inspector;

/// <summary>
/// ה-ViewModel של <see cref="ConnectionInspectorView"/>. **מציגה בלבד** -
/// אין כאן שום חישוב הנדסי, רק תרגום של <see cref="ElementRelationshipLookup.RelationshipInfo"/>
/// (שכבר פוענח, לא חושב) למשפטים קריאים ולרשימת-אלמנטים ל-Highlight.
/// </summary>
public sealed class ConnectionInspectorViewModel : INotifyPropertyChanged
{
    private string _headline = "בחר/י אסלה, קולטן, או צינור במודל כדי לראות את החיבור שלהם.";
    private string _details = string.Empty;
    private bool _hasRelationship;
    private List<ElementId> _relatedElementIds = new();

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>שורת-הכותרת המוצגת בפאנל - סיכום קצר של הקשר שנמצא (או "לא-רלוונטי"/"אין בחירה").</summary>
    public string Headline { get => _headline; set => SetField(ref _headline, value); }

    /// <summary>שורת-פירוט משנית - מצב הצינור/סטטיסטיקת-הקולטן.</summary>
    public string Details { get => _details; set => SetField(ref _details, value); }

    /// <summary>
    /// רשימה מפורטת, שורה-לכל-אסלה - מתמלאת **רק** כשנבחר קולטן (יכול
    /// להיות ריק בשאר המקרים, ה-View פשוט לא מציג כלום אז).
    /// </summary>
    public ObservableCollection<string> ConnectedItems { get; } = new();

    /// <summary>קובעת אם כפתור-ה-Highlight זמין - יש רשימת-אלמנטים תקפה להדגיש.</summary>
    public bool HasRelationship { get => _hasRelationship; set => SetField(ref _hasRelationship, value); }

    /// <summary>
    /// ה-UIDocument הפעיל - מתעדכן בכל אירוע-בחירה (ראו
    /// <c>App.OnSelectionChanged</c>), כדי ש-<see cref="Highlight"/> תדע
    /// על איזה מסמך לפעול בזמן שהיא נקראת (מהקליק על הכפתור, לא מתוך
    /// אירוע-הבחירה עצמו).
    /// </summary>
    public UIDocument? CurrentUiDocument { get; set; }

    /// <summary>אין אלמנט נבחר בכלל (בחירה ריקה, או יותר מאלמנט אחד).</summary>
    public void ShowNoSelection()
    {
        HasRelationship = false;
        Headline = "בחר/י אסלה, קולטן, או צינור במודל.";
        Details = string.Empty;
        ConnectedItems.Clear();
        _relatedElementIds = new List<ElementId>();
    }

    /// <summary>האלמנט הנבחר אינו קולטן/צינור/אסלה-שכבר-עובדה - לא משנה שום דבר במודל.</summary>
    public void ShowNotRelevant()
    {
        HasRelationship = false;
        Headline = "האלמנט הנבחר אינו חלק ממערכת האינסטלציה שכבר עובדה.";
        Details = "ייתכן שזו אסלה שטרם הורצה עליה \"צייר צינורות\".";
        ConnectedItems.Clear();
        _relatedElementIds = new List<ElementId>();
    }

    /// <summary>
    /// מציגה קשר שכבר פוענח - לא מחשבת שום דבר בעצמה, רק מתרגמת
    /// למשפטים ובונה את רשימת-ה-Highlight. הנוסחה זהה לשלושת סוגי-
    /// הבחירה (Fixture/Collector/Pipe) - ראו docs/connection-inspector.md.
    /// </summary>
    public void ShowRelationship(ElementRelationshipLookup.RelationshipInfo info)
    {
        HasRelationship = true;
        ConnectedItems.Clear();

        var relatedIds = new HashSet<ElementId> { info.SelectedElementId };
        if (info.CollectorElementId is ElementId collectorElementId)
        {
            relatedIds.Add(collectorElementId);
        }

        foreach (ElementRelationshipLookup.ConnectedPipe pipe in info.Pipes)
        {
            relatedIds.Add(pipe.PipeElementId);
            if (pipe.FixtureElementId is ElementId fixtureElementId)
            {
                relatedIds.Add(fixtureElementId);
            }
        }

        _relatedElementIds = relatedIds.ToList();

        string collectorDisplay = info.CollectorElementId is ElementId cid
            ? $"{info.CollectorLabel} (ElementId {cid.Value})"
            : info.CollectorLabel ?? "(לא נמצא)";

        switch (info.Kind)
        {
            case ElementRelationshipLookup.SelectedKind.Fixture:
                ShowFixture(info, collectorDisplay);
                break;

            case ElementRelationshipLookup.SelectedKind.Pipe:
                ShowPipe(info, collectorDisplay);
                break;

            case ElementRelationshipLookup.SelectedKind.Collector:
                ShowCollector(info, collectorDisplay);
                break;
        }
    }

    private void ShowFixture(ElementRelationshipLookup.RelationshipInfo info, string collectorDisplay)
    {
        ElementRelationshipLookup.ConnectedPipe pipe = info.Pipes[0];
        Headline = $"אסלה {info.SelectedElementId.Value} מחוברת לקולטן {collectorDisplay}";
        Details =
            $"דירה: {pipe.ApartmentLabel ?? "(לא ידוע)"}\n" +
            $"Route ID: {pipe.RouteId}\n" +
            $"סטטוס: {StatusText(pipe.RequiresManualEngineering)}";
    }

    private void ShowPipe(ElementRelationshipLookup.RelationshipInfo info, string collectorDisplay)
    {
        ElementRelationshipLookup.ConnectedPipe pipe = info.Pipes[0];
        string fixtureDisplay = pipe.FixtureElementId is ElementId fixtureId
            ? $"{fixtureId.Value} (דירה {pipe.ApartmentLabel ?? "לא ידוע"})"
            : $"{pipe.FixtureIdLabel} (לא נמצאה במודל)";

        Headline = $"צינור {pipe.RouteId}";
        Details =
            $"מ-אסלה: {fixtureDisplay}\n" +
            $"אל קולטן: {collectorDisplay}\n" +
            $"סטטוס: {StatusText(pipe.RequiresManualEngineering)}";
    }

    private void ShowCollector(ElementRelationshipLookup.RelationshipInfo info, string collectorDisplay)
    {
        int manualCount = info.Pipes.Count(p => p.RequiresManualEngineering);
        Headline = $"קולטן {collectorDisplay} - {info.Pipes.Count} אסל(ות) מחוברות";
        Details = manualCount > 0
            ? $"{info.Pipes.Count - manualCount} תקינות, {manualCount} דורשות תכנון ידני."
            : $"כל ה-{info.Pipes.Count} מנותבות בהצלחה.";

        foreach (ElementRelationshipLookup.ConnectedPipe pipe in info.Pipes)
        {
            string fixtureLabel = pipe.FixtureElementId is ElementId fixtureId
                ? fixtureId.Value.ToString()
                : $"{pipe.FixtureIdLabel} (לא נמצאה)";
            string apartment = pipe.ApartmentLabel ?? "לא ידוע";

            ConnectedItems.Add($"אסלה {fixtureLabel}  (דירה {apartment})  -  {StatusText(pipe.RequiresManualEngineering)}");
        }
    }

    private static string StatusText(bool requiresManualEngineering) =>
        requiresManualEngineering ? "דורש תכנון ידני" : "נותב אוטומטית בהצלחה";

    // מסומן ל-true ממש-לפני ש-Highlight() משנה את הבחירה-בפועל דרך
    // SetElementIds - כדי ש-App.OnSelectionChanged ידע להתעלם מה-
    // SelectionChanged הבא (הוא נגרם על ידי הקוד שלנו, לא על ידי
    // בחירה אמיתית של המשתמש/ת) ולא יאפס את הפאנל. נצרך (Consume,
    // חוזר ל-false) בקריאה הראשונה ל-ConsumeSuppressNextSelectionChanged -
    // כדי שרק ה-SelectionChanged **הבא-מיד** יידחה, לא כל אחד-בעתיד.
    private bool _suppressNextSelectionChanged;

    /// <summary>
    /// מדגישה (Highlight) את כל האלמנטים הקשורים - קריאה ל-
    /// <c>Selection.SetElementIds</c>, פעולת-UI טהורה (בלי Transaction,
    /// בלי שינוי במודל). כשל כלשהו נבלע - כשל-UI לעולם לא יכול להפיל
    /// שום דבר אחר. משנה את הבחירה-בפועל ב-Revit - זה עצמו מעורר
    /// <c>SelectionChanged</c> נוסף; ראו <see cref="_suppressNextSelectionChanged"/>
    /// ו-docs/connection-inspector.md להסבר המלא על הבעיה שזה פותר.
    /// </summary>
    public void Highlight()
    {
        if (CurrentUiDocument is null || _relatedElementIds.Count == 0)
        {
            return;
        }

        try
        {
            _suppressNextSelectionChanged = true;
            CurrentUiDocument.Selection.SetElementIds(_relatedElementIds);
        }
        catch
        {
            // הבחירה לא השתנתה בפועל - אין SelectionChanged לצפות-לו/לדכא.
            _suppressNextSelectionChanged = false;
        }
    }

    /// <summary>
    /// נקראת מ-<c>App.OnSelectionChanged</c> בתחילת כל אירוע - אם
    /// מחזירה <c>true</c>, זה ה-SelectionChanged שנגרם על ידי
    /// <see cref="Highlight"/> עצמה (לא בחירה אמיתית של המשתמש/ת) -
    /// הקורא אמור להתעלם ממנו לגמרי, בלי לגעת בשום מצב בפאנל. "צורכת"
    /// (consume) את הדגל - קריאה שנייה מיד אחר-כך תחזיר <c>false</c>.
    /// </summary>
    public bool ConsumeSuppressNextSelectionChanged()
    {
        if (!_suppressNextSelectionChanged)
        {
            return false;
        }

        _suppressNextSelectionChanged = false;
        return true;
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
