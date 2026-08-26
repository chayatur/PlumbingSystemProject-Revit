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
    /// מציגה קודם דיאלוג-בחירת-היקף (<see cref="ScopeSelector.TryChooseScope"/> -
    /// קומה פעילה מול כל הבניין, אותו דיאלוג בדיוק כמו ב-<see cref="DrawPipesCommand"/>
    /// ו-<see cref="PlaceCollectorsCommand"/>) - מחזירה <see cref="Result.Cancelled"/>
    /// אם בוטל. אחר-כך מריצה <see cref="RevitModelReader.ReadApartments"/> ואז
    /// <see cref="CollectorLocator.Locate"/> לכל דירה, ומדפיסה את
    /// התוצאה לקובץ טקסט. אם אחת מהשתיים נכשלת עם
    /// <see cref="System.InvalidOperationException"/> או <see cref="System.ArgumentException"/>
    /// (מצבי-קצה שלא אמורים לקרות - ראו <see cref="GuestBathroomSelector"/>
    /// ו-<see cref="CollectorLocator"/>), הפקודה מציגה את הודעת השגיאה
    /// המדויקת ב-TaskDialog ומחזירה <see cref="Result.Failed"/>, במקום
    /// לתת ל-Revit להציג חריגה גולמית או להמשיך עם דוח חלקי.
    /// </summary>
    /// <remarks>
    /// בניגוד ל-<see cref="DrawPipesCommand"/>, הפקודה הזו **לא** מחריגה
    /// קומה 0 (<c>excludeFloorZero</c> נשאר <c>false</c>) - לא התבקש שינוי
    /// כזה כאן, וההתנהגות ביחס לקומה 0 נשארת זהה-להיום (ראו docs/step7.md).
    /// </remarks>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">מתמלא בהודעת השגיאה אם הבנייה נכשלה.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> אם הדוח נכתב ונפתח בהצלחה.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;
        View activeView = commandData.Application.ActiveUIDocument.ActiveView;

        Level? activeLevel = activeView.GenLevel;
        int? activeFloorNumber = RevitModelReader.TryGetFloorNumber(activeLevel);

        if (!ScopeSelector.TryChooseScope(activeLevel, activeFloorNumber, out ElementId? onlyLevelId, out string scopeDescription))
        {
            return Result.Cancelled;
        }

        var reader = new RevitModelReader(doc);

        List<Apartment> apartments;
        List<string> readerWarnings;
        Dictionary<string, List<CollectorPoint>> collectorsByApartmentId;
        try
        {
            apartments = reader.ReadApartments(onlyLevelId);
            readerWarnings = reader.Warnings.ToList();
            collectorsByApartmentId = apartments.ToDictionary(a => a.Id, CollectorLocator.Locate);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - בניית קולטנים נכשלה", ex.Message);
            return Result.Failed;
        }

        string report = BuildReport(apartments, collectorsByApartmentId, scopeDescription, readerWarnings);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_Collectors_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, report, Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    private static string BuildReport(
        List<Apartment> apartments,
        Dictionary<string, List<CollectorPoint>> collectorsByApartmentId,
        string scopeDescription,
        List<string> readerWarnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Collector Location Report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Scope: {scopeDescription}");
        sb.AppendLine($"Apartments found: {apartments.Count}");
        sb.AppendLine(
            $"MaxDistanceMeters={CollectorLocator.MaxDistanceMeters:F1}  " +
            $"PreferredDistanceMeters={CollectorLocator.PreferredDistanceMeters:F1}");

        if (readerWarnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"--- Warnings ({readerWarnings.Count}) - elements skipped/excluded while reading the model ---");
            foreach (string warning in readerWarnings)
            {
                sb.AppendLine($"  {warning}");
            }
        }

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
