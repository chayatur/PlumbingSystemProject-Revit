using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// בונה את מודל הדומיין (<see cref="RevitModelReader"/>) ואז מריצה
/// עליו את <see cref="CollectorLocator"/> לכל דירה, ומדפיסה דוח קריא:
/// כמה קולטנים נוצרו לכל דירה, מיקום כל קולטן, אילו אסלות (ElementId)
/// משויכות אליו, והמרחק בפועל מכל אסלה לקולטן שלה - כדי לאפשר אימות
/// ידני (למשל שקולטן לא מכסה אסלה מעל 4.0 מ') לפני שממשיכים לשלב
/// הבא (מיקום מדויק צמוד לקיר, וכתיבה חזרה ל-Revit). פקודת אבחון/בנייה
/// מקדימה, לא הפיצ'ר הסופי.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class BuildCollectorsCommand : IExternalCommand
{
    /// <summary>
    /// מריצה <see cref="RevitModelReader.ReadApartments"/> ואז
    /// <see cref="CollectorLocator.Locate"/> לכל דירה, ומדפיסה את
    /// התוצאה לקובץ טקסט. אם אחת מהשתיים נכשלת עם
    /// <see cref="System.InvalidOperationException"/> או <see cref="System.ArgumentException"/>
    /// (מצבי-קצה שלא אמורים לקרות - ראו <see cref="GuestBathroomSelector"/>
    /// ו-<see cref="CollectorLocator"/>), הפקודה מציגה את הודעת השגיאה
    /// המדויקת ב-TaskDialog ומחזירה <see cref="Result.Failed"/>, במקום
    /// לתת ל-Revit להציג חריגה גולמית או להמשיך עם דוח חלקי.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">מתמלא בהודעת השגיאה אם הבנייה נכשלה.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> אם הדוח נכתב ונפתח בהצלחה.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;
        var reader = new RevitModelReader(doc);

        List<Apartment> apartments;
        Dictionary<string, List<CollectorPoint>> collectorsByApartmentId;
        try
        {
            apartments = reader.ReadApartments();
            collectorsByApartmentId = apartments.ToDictionary(a => a.Id, CollectorLocator.Locate);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - בניית קולטנים נכשלה", ex.Message);
            return Result.Failed;
        }

        string report = BuildReport(apartments, collectorsByApartmentId);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_Collectors_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, report, Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    private static string BuildReport(
        List<Apartment> apartments,
        Dictionary<string, List<CollectorPoint>> collectorsByApartmentId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Collector Location Report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Apartments found: {apartments.Count}");
        sb.AppendLine(
            $"MaxDistanceMeters={CollectorLocator.MaxDistanceMeters:F1}  " +
            $"PreferredDistanceMeters={CollectorLocator.PreferredDistanceMeters:F1}");

        foreach (Apartment apartment in apartments.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            List<CollectorPoint> collectors = collectorsByApartmentId[apartment.Id];
            Dictionary<string, ToiletFixture> fixturesById = apartment.Fixtures.ToDictionary(f => f.Id);

            sb.AppendLine();
            sb.AppendLine($"--- Apartment '{apartment.Id}' (Floor {apartment.FloorNumber}) ---");
            sb.AppendLine($"Toilet fixtures: {apartment.Fixtures.Count}  Collectors: {collectors.Count}");

            foreach (CollectorPoint collector in collectors)
            {
                sb.AppendLine($"  Collector Id={collector.Id}  Location={collector.Location}");

                foreach (string fixtureId in collector.ConnectedFixtureIds)
                {
                    ToiletFixture fixture = fixturesById[fixtureId];
                    double distance = GeometryUtils.Distance2D(fixture.Location, collector.Location);
                    string marker = fixture.IsGuestBathroom ? " [GUEST BATHROOM]" : string.Empty;

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "    ElementId={0}  distance={1:F4}m{2}",
                        fixtureId,
                        distance,
                        marker));
                }
            }
        }

        return sb.ToString();
    }
}
