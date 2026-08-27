using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using PlumbingSystem.Revit.Commands;
using PlumbingSystem.Revit.Inspector;

namespace PlumbingSystem.Revit;

/// <summary>
/// נקודת הכניסה של ה-Add-in ל-Revit. אחראית על רישום ממשק המשתמש בריבון
/// (טאב, פאנל, כפתורים) בעת עליית Revit, ועל שחרור משאבים בעת סגירתו.
/// זו המחלקה שה-manifest (PlumbingSystem.addin) מצביע אליה דרך FullClassName.
/// </summary>
public class App : IExternalApplication
{
    private const string TabName = "Startarc";
    private const string PanelName = "אינסטלציה";

    // נוצר פעם אחת ב-OnStartup, לפני שנפתח מסמך כלשהו - ראו
    // ConnectionInspectorPaneProvider ו-docs/connection-inspector.md.
    private readonly ConnectionInspectorPaneProvider _connectionInspectorPaneProvider = new();

    /// <summary>
    /// נקרא פעם אחת בעת עליית Revit. יוצר את לשונית "Startarc", בתוכה פאנל
    /// "אינסטלציה", ובתוכו כפתור "בדיקת חיבור" שמריץ את
    /// <see cref="ReadElementsCommand"/> (בדיקת שפיות שמוודאת שה-Add-in
    /// נטען, ה-manifest תקין וה-References ל-Revit API עובדים), וכפתור
    /// "אבחון מודל" שמריץ את <see cref="DiscoverModelCommand"/> - כפתור
    /// אבחון **זמני** שיוסר כשלוגיקת הסיווג האמיתית תיבנה (ראו התיעוד
    /// על <see cref="DiscoverModelCommand"/> עצמה), כפתור "בנה מודל
    /// דומיין" שמריץ את <see cref="BuildDomainModelCommand"/>, כפתור
    /// "בנה קולטנים" שמריץ את <see cref="BuildCollectorsCommand"/>
    /// (דוח בלבד, ReadOnly), כפתור "מקם קולטנים ב-Revit" שמריץ את
    /// <see cref="PlaceCollectorsCommand"/> (כותב אלמנטים בפועל למודל),
    /// כפתור "צייר צינורות" שמריץ את <see cref="DrawPipesCommand"/>
    /// (שלב 7 - מקטעי צינור בין אסלה לקולטן), כפתור "אבחון היסט-קולטן"
    /// שמריץ את <see cref="CollectorSetbackDiagnosticCommand"/> (אבחון
    /// **זמני** נוסף, ReadOnly - ראו התיעוד עליה - להסרה כשהחקירה תסתיים),
    /// וכפתור "דוח לקוח (HTML)" שמריץ את <see cref="GenerateClientReportCommand"/>
    /// (קורא את דוח-ה-Pipes האחרון ובונה ממנו HTML מקצועי ללקוח/הנהלה -
    /// שכבת-תצוגה בלבד, בלי לגעת בלוגיקה ההנדסית).
    /// </summary>
    /// <param name="application">אובייקט הבקרה של Revit UI שדרכו נרשמים רכיבי הריבון.</param>
    /// <returns><see cref="Result.Succeeded"/> אם רישום הריבון הצליח.</returns>
    public Result OnStartup(UIControlledApplication application)
    {
        application.CreateRibbonTab(TabName);

        RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);

        // נדרש הנתיב המלא ל-DLL הנוכחי כדי ש-Revit ידע מאיפה לטעון את הפקודה בזמן לחיצה.
        string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        var readElementsButtonData = new PushButtonData(
            "ReadElementsCommand",
            "בדיקת חיבור",
            assemblyPath,
            typeof(ReadElementsCommand).FullName);

        var discoverModelButtonData = new PushButtonData(
            "DiscoverModelCommand",
            "אבחון מודל",
            assemblyPath,
            typeof(DiscoverModelCommand).FullName);

        var buildDomainModelButtonData = new PushButtonData(
            "BuildDomainModelCommand",
            "בנה מודל דומיין",
            assemblyPath,
            typeof(BuildDomainModelCommand).FullName);

        var buildCollectorsButtonData = new PushButtonData(
            "BuildCollectorsCommand",
            "בנה קולטנים",
            assemblyPath,
            typeof(BuildCollectorsCommand).FullName);

        var placeCollectorsButtonData = new PushButtonData(
            "PlaceCollectorsCommand",
            "מקם קולטנים ב-Revit",
            assemblyPath,
            typeof(PlaceCollectorsCommand).FullName);

        var drawPipesButtonData = new PushButtonData(
            "DrawPipesCommand",
            "צייר צינורות",
            assemblyPath,
            typeof(DrawPipesCommand).FullName);

        // כפתור אבחון **זמני**, כמו DiscoverModelCommand - ראו התיעוד על
        // CollectorSetbackDiagnosticCommand. יש להסיר יחד עם המחלקה עצמה
        // כשהחקירה תסתיים (בין אם מובילה לשינוי-קוד בפועל ובין אם לא).
        var collectorSetbackDiagnosticButtonData = new PushButtonData(
            "CollectorSetbackDiagnosticCommand",
            "אבחון היסט-קולטן",
            assemblyPath,
            typeof(CollectorSetbackDiagnosticCommand).FullName);

        // כפתור נפרד (לא פרמטר על "צייר צינורות") - קהל-יעד שונה
        // (לקוח/הנהלה, לא מהנדס) ופלט שונה (HTML, לא TXT) - ראו התיעוד
        // על GenerateClientReportCommand. קורא-בלבד את קובץ ה-Pipes
        // האחרון שכבר נוצר - אין צורך להריץ "צייר צינורות" מחדש כדי
        // להריץ את זה שוב.
        var generateClientReportButtonData = new PushButtonData(
            "GenerateClientReportCommand",
            "דוח לקוח (HTML)",
            assemblyPath,
            typeof(GenerateClientReportCommand).FullName);

        panel.AddItem(readElementsButtonData);
        panel.AddItem(discoverModelButtonData);
        panel.AddItem(buildDomainModelButtonData);
        panel.AddItem(buildCollectorsButtonData);
        panel.AddItem(placeCollectorsButtonData);
        panel.AddItem(drawPipesButtonData);
        panel.AddItem(collectorSetbackDiagnosticButtonData);
        panel.AddItem(generateClientReportButtonData);

        // "Connection Inspector" - פאנל-מעוגן (לא כפתור-ריבון): Revit
        // מוסיף אותו אוטומטית ל-View → User Interface. חייב להירשם כאן,
        // לפני שנפתח מסמך כלשהו - ראו docs/connection-inspector.md.
        application.RegisterDockablePane(
            ConnectionInspectorPaneProvider.PaneId,
            "Connection Inspector",
            _connectionInspectorPaneProvider);

        application.SelectionChanged += OnSelectionChanged;

        return Result.Succeeded;
    }

    /// <summary>
    /// מתעדכן בכל שינוי-בחירה בכל מסמך פתוח - קורא-בלבד
    /// (<see cref="ElementRelationshipLookup"/>, שכבר persisted במודל,
    /// לא מחשבת שום דבר), לא נוגע במודל. עטוף כולו ב-try/catch - כשל
    /// בפאנל-האינפורמציה לעולם לא יכול להפיל שום דבר אחר ב-Revit.
    /// </summary>
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            ConnectionInspectorViewModel viewModel = _connectionInspectorPaneProvider.ViewModel;

            // האירוע הזה נגרם על ידי Highlight() עצמה (SetElementIds
            // משנה את הבחירה, מה שמעורר SelectionChanged נוסף) - לא
            // בחירה אמיתית של המשתמש/ת. מתעלמים לגמרי, כולל לא-מעדכנים
            // את CurrentUiDocument - ראו docs/connection-inspector.md.
            if (viewModel.ConsumeSuppressNextSelectionChanged())
            {
                return;
            }

            UIDocument? uidoc = (sender as UIApplication)?.ActiveUIDocument;
            viewModel.CurrentUiDocument = uidoc;

            ICollection<ElementId> selected = e.GetSelectedElements();
            if (selected.Count != 1 || uidoc is null)
            {
                viewModel.ShowNoSelection();
                return;
            }

            ElementRelationshipLookup.RelationshipInfo? info =
                ElementRelationshipLookup.TryDescribe(uidoc.Document, selected.First());

            if (info is null)
            {
                viewModel.ShowNotRelevant();
            }
            else
            {
                viewModel.ShowRelationship(info);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// נקרא פעם אחת בעת סגירת Revit - מסיר את מנוי ה-SelectionChanged
    /// שנרשם ב-OnStartup (ראו שם).
    /// </summary>
    /// <param name="application">אובייקט הבקרה של Revit UI.</param>
    /// <returns><see cref="Result.Succeeded"/>.</returns>
    public Result OnShutdown(UIControlledApplication application)
    {
        application.SelectionChanged -= OnSelectionChanged;
        return Result.Succeeded;
    }
}
