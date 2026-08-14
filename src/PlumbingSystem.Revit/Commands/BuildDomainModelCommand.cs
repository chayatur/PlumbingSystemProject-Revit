using System.Diagnostics;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// בונה בפועל את מודל הדומיין (<see cref="Apartment"/>/<see cref="ToiletFixture"/>)
/// מנתוני המסמך הפעיל, דרך <see cref="RevitModelReader"/>, ומדפיסה דוח
/// קריא לקובץ טקסט: לכל דירה - כמה אסלות, איזו מסומנת כשירותים בודדים,
/// ואילו מרחקים בפועל הובילו להחלטה - כדי לאפשר אימות ידני לפני שממשיכים
/// לאלגוריתם מיקום הקולטן. עדיין פקודת אבחון/בנייה מקדימה, לא הפיצ'ר
/// הסופי (שיכתוב את המודל בחזרה ל-Revit כאלמנטים).
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class BuildDomainModelCommand : IExternalCommand
{
    /// <summary>
    /// מריצה את <see cref="RevitModelReader.ReadApartments"/> ומדפיסה
    /// את התוצאה. אם הקריאה נכשלת עם <see cref="System.InvalidOperationException"/>
    /// או <see cref="System.ArgumentException"/> (מצבי-קצה שלא אמורים לקרות,
    /// כמו דירה עם 0 או 2+ מועמדים לשירותים בודדים - ראו
    /// <see cref="GuestBathroomSelector"/>), הפקודה מציגה את הודעת
    /// השגיאה במפורש ב-TaskDialog במקום לתת ל-Revit להציג חריגה גולמית,
    /// ומחזירה <see cref="Result.Failed"/> - השגיאה עצמה לא מוסתרת, רק
    /// מוצגת בערוץ קריא יותר.
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
        try
        {
            apartments = reader.ReadApartments();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - בניית מודל דומיין נכשלה", ex.Message);
            return Result.Failed;
        }

        string report = BuildReport(apartments, reader.Warnings, reader.SelectionExplanations);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_DomainModel_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, report, Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    private static string BuildReport(
        List<Apartment> apartments,
        IReadOnlyList<string> warnings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selectionExplanations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Domain Model Report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Apartments found: {apartments.Count}");

        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"--- Warnings ({warnings.Count}) - elements skipped, not included above ---");
            foreach (string warning in warnings)
            {
                sb.AppendLine($"  {warning}");
            }
        }

        foreach (Apartment apartment in apartments.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine($"--- Apartment '{apartment.Id}' (Floor {apartment.FloorNumber}) ---");
            sb.AppendLine($"Toilet fixtures: {apartment.Fixtures.Count}");

            foreach (ToiletFixture fixture in apartment.Fixtures)
            {
                string marker = fixture.IsGuestBathroom ? "GUEST BATHROOM" : "regular";
                sb.AppendLine($"  ElementId={fixture.Id}  Location={fixture.Location}  [{marker}]");
            }

            if (selectionExplanations.TryGetValue(apartment.Id, out IReadOnlyList<string>? explanation))
            {
                sb.AppendLine("  Why:");
                foreach (string line in explanation)
                {
                    sb.AppendLine($"    {line}");
                }
            }
        }

        return sb.ToString();
    }
}
