using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using PlumbingSystem.Revit.Config;

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

        AppendToiletSummaryPerLevel(sb, doc, hostFixtures);
        AppendRoomsPerLevel(sb, doc);
        AppendFixturesSection(sb, doc, "3. Plumbing fixtures in the HOST document (full detail)", hostFixtures);

        // 4. RevitLinkInstance-ים במסמך הראשי + סטטוס טעינה - כדי לשלול
        //    שהגיאומטריה בפועל יושבת בקובץ מקושר ולא במסמך הראשי.
        List<RevitLinkInstance> linkInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .ToList();

        sb.AppendLine();
        sb.AppendLine("--- 4. RevitLinkInstance elements in the host document ---");
        sb.AppendLine($"Found: {linkInstances.Count}");
        foreach (RevitLinkInstance linkInstance in linkInstances)
        {
            RevitLinkType? linkType = doc.GetElement(linkInstance.GetTypeId()) as RevitLinkType;
            LinkedFileStatus status = linkType?.GetLinkedFileStatus() ?? LinkedFileStatus.Invalid;
            Document? linkDoc = linkInstance.GetLinkDocument();
            string title = linkDoc?.Title ?? linkType?.Name ?? "(unknown link)";

            sb.AppendLine($"  '{title}': status={status}");
        }

        // 5. עבור קישורים טעונים בלבד - אלמנטי OST_PlumbingFixtures בתוכם.
        sb.AppendLine();
        sb.AppendLine("--- 5. Plumbing fixtures inside LOADED linked documents ---");

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
    /// סעיף 1: טבלה מסכמת לכל Level - כמה אלמנטים מזוהים כ"אסלה" לפי
    /// הזיהוי-הנוכחי (<see cref="FixtureFamilyNames.ToiletFamilyNames"/> -
    /// אותה השוואת-שם-מדויקת כמו <see cref="RevitModelReader.IsToiletFixture"/>,
    /// בלי הבדיקה של Type Parameter <c>Is_Toilet</c> כי זו מטרתה של
    /// הפקודה הזו - נתון גולמי, לא תלוי-בלוגיקת-הזיהוי-המלאה), וכמה
    /// מהן יש-Room בנקודת המיקום שלהן ("No Room found" סופר בנפרד) -
    /// כדי לענות ישירות על "כמה אסלות בכל קומה, וכמה מהן חסומות בגלל
    /// Room חסר" בלי צורך לספור ידנית מתוך הפירוט המלא (סעיף 3).
    /// </summary>
    private static void AppendToiletSummaryPerLevel(StringBuilder sb, Document doc, List<Element> hostFixtures)
    {
        sb.AppendLine();
        sb.AppendLine("--- 1. Toilet summary per Level (current identification: Family.Name match) ---");

        var toiletRows = hostFixtures
            .Where(fixture => GetFamilyAndTypeName(doc, fixture).Family is string family
                && FixtureFamilyNames.ToiletFamilyNames.Contains(family))
            .Select(fixture =>
            {
                Level? level = doc.GetElement(fixture.LevelId) as Level;
                XYZ? point = (fixture.Location as LocationPoint)?.Point;
                bool hasRoom = point is not null && doc.GetRoomAtPoint(point) is not null;
                return (LevelName: level?.Name ?? "(no Level)", HasRoom: hasRoom);
            })
            .ToList();

        if (toiletRows.Count == 0)
        {
            sb.AppendLine("  (no elements matched the Toilet Family.Name allowlist at all)");
            return;
        }

        var perLevel = toiletRows
            .GroupBy(row => row.LevelName)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("  Level                          Toilets   WithRoom  NoRoomFound");
        foreach (var group in perLevel)
        {
            int total = group.Count();
            int withRoom = group.Count(row => row.HasRoom);
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-30} {1,7}   {2,7}   {3,10}",
                group.Key, total, withRoom, total - withRoom));
        }

        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  TOTAL: {0} toilets across {1} level(s), {2} with Room, {3} with \"No Room found\".",
            toiletRows.Count,
            perLevel.Count(),
            toiletRows.Count(row => row.HasRoom),
            toiletRows.Count(row => !row.HasRoom)));
    }

    /// <summary>
    /// סעיף 2: בודקת אם קיימים בכלל אלמנטי Room (OST_Rooms) בכל Level -
    /// שאלה **שונה** מ"יש Room בנקודת-המיקום-של-אסלה-ספציפית" (זה כבר
    /// נבדק בסעיף 1). Room "ממוקם" (Location != null) אבל לא-תחום
    /// (Area==0) הוא מצב קלאסי ב-Revit של Room-tag שהוצב בלי שגבולות-
    /// חדר (Room Boundaries) סוגרים אותו בפועל - בדיוק המצב שיכול לגרום
    /// ל-GetRoomAtPoint להחזיר null גם כשיש "Room" טכני על אותה קומה.
    /// מדפיסה גם פירוט **מלא לכל Room בודד** (Name, Number, Area,
    /// LocationPoint) - לא רק ספירה - כדי לאפשר בדיקה עצמאית-לא-
    /// מבוססת-הערכה (למשל לפני פנייה לאדריכלית עם ממצא: אילו חדרים
    /// ספציפיים כן/לא תחומים, לא רק "כמה").
    /// </summary>
    private static void AppendRoomsPerLevel(StringBuilder sb, Document doc)
    {
        sb.AppendLine();
        sb.AppendLine("--- 2. Room elements per Level (do Rooms exist / are they enclosed at all?) ---");

        List<Room> allRooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .ToList();

        if (allRooms.Count == 0)
        {
            sb.AppendLine("  (no Room elements found anywhere in the entire host document)");
            return;
        }

        var perLevel = allRooms
            .GroupBy(room => (doc.GetElement(room.LevelId) as Level)?.Name ?? "(no Level)")
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("  Level                          TotalRooms  Enclosed(Area>0)  Unplaced/Unenclosed(Area=0)");
        foreach (var group in perLevel)
        {
            int total = group.Count();
            int enclosed = group.Count(room => room.Area > 0);
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-30} {1,10}  {2,16}  {3,27}",
                group.Key, total, enclosed, total - enclosed));
        }

        sb.AppendLine();
        sb.AppendLine("  Per-Room detail (every Room element, grouped by Level, sorted by Number):");
        foreach (var group in perLevel)
        {
            sb.AppendLine($"  Level '{group.Key}':");
            foreach (Room room in group.OrderBy(r => r.Number, StringComparer.OrdinalIgnoreCase))
            {
                LocationPoint? locationPoint = room.Location as LocationPoint;
                string locationText = locationPoint is null
                    ? "NO Location Point"
                    : FormatPoint(locationPoint.Point);
                string enclosedText = room.Area > 0 ? "ENCLOSED" : "NOT enclosed (Area=0)";

                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    ElementId={0,-10} Number='{1}'  Name='{2}'  Area={3:F2} m2  {4}  Location={5}",
                    room.Id.Value,
                    room.Number,
                    room.Name,
                    UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters),
                    enclosedText,
                    locationText));
            }
        }
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
