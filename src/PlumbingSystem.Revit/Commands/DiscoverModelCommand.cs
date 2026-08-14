using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת אבחון **זמנית**, לא חלק מהפיצ'ר הסופי. מטרתה היחידה היא להפיק
/// דוח גולמי של כל אלמנטי OST_PlumbingFixtures שקיימים בפועל - גם
/// במסמך הראשי וגם בקבצים מקושרים (RevitLinkInstance), כולל ה-Room
/// (אם יש) בנקודת המיקום של כל אחד - בלי שום סינון לפי שם משפחה או
/// קומה, ובלי שום לוגיקת סיווג. ניסיון קודם סינן לפי
/// FamilyName == "If_toilet_wall_hung_6505" (עם I גדולה - טעות הקלדה
/// קודמת כתבה אותו עם l קטנה) והחזיר 0 תוצאות; הוחלט להסיר את הסינון
/// לגמרי במקום לתקן ולנחש שוב. הדוח הזה קיים כדי לענות על השאלה "מה
/// יש בפועל ואיפה" לפני שממשיכים. כשהתשובה כבר ידועה ונבנית לוגיקה
/// אמיתית, יש למחוק את הפקודה הזו (ואת הכפתור שלה ב-App.cs).
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class DiscoverModelCommand : IExternalCommand
{
    /// <summary>
    /// אוספת את כל אלמנטי OST_PlumbingFixtures במסמך הראשי (בלי סינון
    /// לפי שם משפחה או קומה), כולל בדיקת Room בנקודת המיקום של כל אחד,
    /// בודקת אילו RevitLinkInstance קיימים ומה סטטוס הטעינה שלהם,
    /// ועבור קישורים טעונים - אוספת (ובודקת Room) גם את אלמנטי
    /// OST_PlumbingFixtures שבתוכם. הכל מתויג בבירור (Host מול Link)
    /// ונכתב לקובץ טקסט שנפתח אוטומטית (עדיף על TaskDialog כשיש
    /// עשרות/מאות שורות נתונים).
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">לא בשימוש - הפקודה לא אמורה להיכשל בתרחיש רגיל.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> לאחר כתיבת הדוח ופתיחתו.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Model Discovery Report (temporary diagnostic) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Host document: '{doc.Title}'");

        // 1. כל אלמנטי OST_PlumbingFixtures במסמך הראשי, בלי סינון שם משפחה.
        List<Element> hostFixtures = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
            .WhereElementIsNotElementType()
            .ToList();

        AppendFixturesSection(sb, doc, "1. Plumbing fixtures in the HOST document", hostFixtures);

        // 2. RevitLinkInstance-ים במסמך הראשי + סטטוס טעינה - כדי לשלול
        //    שהגיאומטריה בפועל יושבת בקובץ מקושר ולא במסמך הראשי.
        List<RevitLinkInstance> linkInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .ToList();

        sb.AppendLine();
        sb.AppendLine("--- 2. RevitLinkInstance elements in the host document ---");
        sb.AppendLine($"Found: {linkInstances.Count}");
        foreach (RevitLinkInstance linkInstance in linkInstances)
        {
            RevitLinkType? linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
            LinkedFileStatus status = linkType?.GetLinkedFileStatus() ?? LinkedFileStatus.Invalid;
            Document? linkDoc = linkInstance.GetLinkDocument();
            string title = linkDoc?.Title ?? linkType?.Name ?? "(unknown link)";

            sb.AppendLine($"  '{title}': status={status}");
        }

        // 3. עבור קישורים טעונים בלבד - אלמנטי OST_PlumbingFixtures בתוכם.
        sb.AppendLine();
        sb.AppendLine("--- 3. Plumbing fixtures inside LOADED linked documents ---");

        List<Document> loadedLinkDocs = linkInstances
            .Select(li => li.GetLinkDocument())
            .Where(linkDoc => linkDoc is not null)
            .Select(linkDoc => linkDoc!)
            .ToList();

        if (loadedLinkDocs.Count == 0)
        {
            sb.AppendLine("  (no loaded links found)");
        }
        else
        {
            foreach (Document linkDoc in loadedLinkDocs)
            {
                List<Element> linkFixtures = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .ToList();

                AppendFixturesSection(sb, linkDoc, $"LINK '{linkDoc.Title}'", linkFixtures);
            }
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_ModelDiscovery_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    /// <summary>
    /// מוסיפה לדוח סעיף אחיד לרשימת fixtures (מהמסמך הראשי או מקישור):
    /// ספירה כוללת, קיבוץ לפי Family+Type, ואז פירוט לכל אלמנט
    /// (Family, Type, Level, Location, Room). <paramref name="doc"/>
    /// חייב להיות המסמך שאליו <paramref name="fixtures"/> שייכים (המסמך
    /// הראשי או ה-LinkDocument), כי ElementType/Level/Room נטענים דרכו.
    /// </summary>
    private static void AppendFixturesSection(
        StringBuilder sb,
        Document doc,
        string sectionTitle,
        List<Element> fixtures)
    {
        sb.AppendLine();
        sb.AppendLine($"--- {sectionTitle} ---");
        sb.AppendLine($"Found: {fixtures.Count} total");

        var grouped = fixtures
            .Select(fixture => GetFamilyAndTypeName(doc, fixture))
            .GroupBy(nameKey => nameKey)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.Type, StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("Grouped by Family + Type:");
        foreach (var group in grouped)
        {
            sb.AppendLine($"  '{group.Key.Family}: {group.Key.Type}': {group.Count()} units");
        }

        sb.AppendLine("Details:");
        foreach (Element fixture in fixtures)
        {
            (string family, string type) = GetFamilyAndTypeName(doc, fixture);
            Level? level = doc.GetElement(fixture.LevelId) as Level;
            XYZ? point = (fixture.Location as LocationPoint)?.Point;
            string locationText = point is null ? "(no LocationPoint)" : FormatPoint(point);
            string roomText = point is null ? "(no LocationPoint - cannot check Room)" : GetRoomDescription(doc, point);

            sb.AppendLine(
                $"  ElementId={fixture.Id.Value}  Family='{family}'  Type='{type}'  " +
                $"Level='{level?.Name ?? "(no Level)"}'  Location={locationText}  Room={roomText}");
        }
    }

    /// <summary>
    /// מוצאת את ה-Room (לא Area) בנקודה נתונה, בכל הקומות (בלי סינון
    /// לקומה ספציפית - הוחלט להריץ על כל הקומות במקום לנחש את שם ה-
    /// Level המדויק של קומה מסוימת, אחרי שניחוש שם קודם (FamilyName)
    /// כבר הוביל לתוצאה שגויה). <paramref name="doc"/> הוא המסמך
    /// שבו מחפשים את ה-Room - המסמך הראשי או ה-LinkDocument, בהתאם
    /// למסמך שבו נמצא ה-fixture עצמו.
    /// </summary>
    private static string GetRoomDescription(Document doc, XYZ point)
    {
        Room? room = doc.GetRoomAtPoint(point);
        return room is null
            ? "No Room found"
            : $"Name='{room.Name}', Number='{room.Number}'";
    }

    /// <summary>
    /// שולפת FamilyName+TypeName של אלמנט דרך ה-<see cref="Autodesk.Revit.DB.ElementType"/>
    /// שלו, בלי להניח שהאלמנט הוא דווקא <see cref="FamilyInstance"/> -
    /// כך זה עובד גם אם יתברר שיש ב-OST_PlumbingFixtures סוגי אלמנטים
    /// אחרים.
    /// </summary>
    private static (string Family, string Type) GetFamilyAndTypeName(Document doc, Element element)
    {
        ElementType? elementType = doc.GetElement(element.GetTypeId()) as ElementType;
        string family = elementType?.FamilyName ?? "(no family)";
        string type = elementType?.Name ?? "(no type)";
        return (family, type);
    }

    private static string FormatPoint(XYZ point) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "({0:F3}, {1:F3}, {2:F3})",
            point.X,
            point.Y,
            point.Z);
}
