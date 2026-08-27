using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;
using PlumbingSystem.Revit.Progress;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// בונה את מודל הדומיין ואת הקולטנים (כמו <see cref="BuildCollectorsCommand"/>),
/// אבל - בניגוד לכל הפקודות הקודמות שהן <see cref="TransactionMode.ReadOnly"/> -
/// **כותבת בפועל למודל**: מתקנת כל קולטן ל"צמוד לקצה קיר"
/// (<see cref="CollectorPlacementService.SnapToNearestWallEdge"/>) ויוצרת
/// אלמנט <see cref="DirectShape"/> אמיתי בכל מיקום מתוקן
/// (<see cref="CollectorPlacementService.CreateCollectorElement"/>).
/// </summary>
/// <remarks>
/// פקודה **נפרדת** מ-<see cref="BuildCollectorsCommand"/> (לא עדכון
/// שלה): <see cref="BuildCollectorsCommand"/> נשארת <c>ReadOnly</c>
/// בכוונה - דוח-בלבד, בלי שום שינוי במודל - כדי שאפשר יהיה להריץ אותה
/// כל פעם בלי סיכון, גם רק כדי לבדוק את הפריסה ההגיונית לפני שמחליטים
/// לכתוב בפועל. לערבב את שתי האחריויות היה מחייב לשנות את
/// ה-TransactionMode של הפקודה הקיימת ולפגוע ביכולת "הצג בלי לשנות".
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class PlaceCollectorsCommand : IExternalCommand
{
    /// <summary>
    /// מציגה קודם דיאלוג-בחירת-היקף (<see cref="ScopeSelector.TryChooseScope"/> -
    /// קומה פעילה מול כל הבניין, אותו דיאלוג בדיוק כמו ב-
    /// <see cref="DrawPipesCommand"/>) - מחזירה <see cref="Result.Cancelled"/>
    /// אם בוטל. אחר-כך מריצה <see cref="RevitModelReader.ReadApartments"/> ו-
    /// <see cref="CollectorLocator.Locate"/> (כמו <see cref="BuildCollectorsCommand"/>),
    /// ואז - בתוך <see cref="Autodesk.Revit.DB.Transaction"/> אחד שעוטף
    /// הכל - **קודם** מוחקת קולטנים ישנים מהרצות קודמות
    /// (<see cref="CollectorPlacementService.DeleteExistingCollectors"/>),
    /// ורק אז מתקנת מיקום ויוצרת אלמנט לכל קולטן חדש. אם שגיאה כלשהי
    /// קורית **תוך כדי** (מחיקה או יצירה, בכל דירה שהיא), כל ה-Transaction
    /// מתבטל (<c>Transaction.RollBack</c>) - כולל המחיקה - כך שלא נשארים
    /// לא באמצע-מחיקה (בלי קולטנים בכלל) ולא עם אלמנטים חלקיים מדירות
    /// שהספיקו להיווצר לפני הכשל.
    /// </summary>
    /// <remarks>
    /// בניגוד ל-<see cref="DrawPipesCommand"/>, הפקודה הזו **לא** מחריגה
    /// קומה 0 (<c>excludeFloorZero</c> נשאר <c>false</c>) - לא התבקש שינוי
    /// כזה כאן, וההתנהגות ביחס לקומה 0 נשארת זהה-להיום (ראו docs/step7.md).
    /// </remarks>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">מתמלא בהודעת השגיאה אם הפעולה נכשלה.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> אם כל הקולטנים נוצרו והדוח נכתב ונפתח.</returns>
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
        var placementService = new CollectorPlacementService(doc);

        List<Apartment> apartments;
        List<string> readerWarnings;
        Dictionary<string, List<CollectorPoint>> rawCollectorsByApartmentId;
        try
        {
            apartments = reader.ReadApartments(onlyLevelId);
            readerWarnings = reader.Warnings.ToList();
            rawCollectorsByApartmentId = apartments.ToDictionary(a => a.Id, CollectorLocator.Locate);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - מיקום קולטנים נכשל", ex.Message);
            return Result.Failed;
        }

        // דיווח-התקדמות-חי (ראו docs/progress-infrastructure.md) - אותה
        // תשתית בדיוק כמו ב-DrawPipesCommand, בלי שום שינוי בה. כאן
        // "SuccessCount"/"ManualReviewCount" מייצגים משהו אחר לגמרי
        // (PASS/FAIL של האימות האוטומטי, לא "דורש-תכנון-ידני") - זה
        // בדיוק מה שהתשתית הכללית נועדה לאפשר.
        int totalCollectorCount = rawCollectorsByApartmentId.Values.Sum(list => list.Count);
        IProgressReporter progressReporter;
        try
        {
            progressReporter = new ProgressWindowReporter("Place Collectors", totalCollectorCount);
        }
        catch
        {
            progressReporter = NullProgressReporter.Instance;
        }

        int processedCollectorCount = 0;
        int livePassCount = 0;
        int liveFailCount = 0;

        var placedByApartmentId = new Dictionary<string, List<PlacedCollector>>();

        using Transaction tx = new(doc, "PlumbingSystem - Place Collectors");
        tx.Start();

        int deletedCollectorCount;
        bool deletedCollectorMaterial;

        try
        {
            // מחיקת קולטנים ישנים (מהרצות קודמות) בתוך **אותו** Transaction
            // כמו היצירה - אם היצירה נכשלת באמצע, ה-Rollback המלא מבטל גם
            // את המחיקה, ולא נשארים בלי קולטנים בכלל.
            deletedCollectorCount = placementService.DeleteExistingCollectors();

            // מחיקת ה-Material של הקולטנים מהרצה קודמת (אם קיים) - כדי
            // ש-CreateCollectorElement תמיד ייצור Material חדש וייחודי,
            // ולעולם לא תשתמש-חוזר במשהו שאולי כבר משויך לאלמנטים אחרים.
            deletedCollectorMaterial = placementService.DeleteExistingCollectorMaterial();

            foreach (Apartment apartment in apartments)
            {
                var placedForApartment = new List<PlacedCollector>();

                foreach (CollectorPoint rawCollector in rawCollectorsByApartmentId[apartment.Id])
                {
                    (CollectorPoint snappedCollector, XYZ visualDrawCenter) =
                        placementService.SnapToNearestWallEdge(rawCollector);
                    DirectShape element = placementService.CreateCollectorElement(
                        snappedCollector, visualDrawCenter, activeView);

                    // הקשר ניווט: ה-Room/Level לפי המיקום **המקורי** (מיקום
                    // האסלה, לפני ה-snap) - לא לפי המיקום הסופי-אחרי-snap.
                    // המיקום הסופי צמוד ממש לקצה קיר, ולכן לפעמים נופל
                    // מחוץ לפוליגון ה-Room (GetRoomAtPoint מחזיר null) -
                    // המיקום המקורי תמיד בטוח בתוך התא הפיזי של האסלה.
                    (string roomName, string roomNumber, string levelName, ElementId levelId) =
                        GetRoomContext(doc, rawCollector.Location);

                    // אימות אוטומטי, עצמאי - ראו CollectorPlacementService.VerifyPlacement.
                    // Room-membership נבדק על המיקום המקורי (rawCollector) -
                    // לא הסופי - מאותה סיבה בדיוק כמו "Location context"
                    // למעלה: המיקום הסופי צמוד ממש לקיר ולפעמים נופל מחוץ
                    // לפוליגון ה-Room. קרבה-לקיר עדיין נבדקת על המיקום הסופי.
                    CollectorPlacementService.PlacementVerification verification =
                        placementService.VerifyPlacement(rawCollector, snappedCollector, apartment.Id, levelId);

                    placedForApartment.Add(new PlacedCollector(
                        rawCollector,
                        snappedCollector,
                        element.Id,
                        RoomName: roomName,
                        RoomNumber: roomNumber,
                        LevelName: levelName,
                        Verification: verification));

                    // דיווח-התקדמות: אחרי שהתוצאה האמיתית (כולל האלמנט
                    // שכבר נוצר ואומת) כבר ידועה במלואה - לא ניחוש/צפי.
                    processedCollectorCount++;
                    if (verification.OverallPass)
                    {
                        livePassCount++;
                    }
                    else
                    {
                        liveFailCount++;
                    }

                    TryReport(progressReporter, new ProgressReport(
                        Floor: apartment.FloorNumber.ToString(CultureInfo.InvariantCulture),
                        Apartment: apartment.Id,
                        CurrentItem: rawCollector.Id,
                        ProgressCurrent: processedCollectorCount,
                        ProgressTotal: totalCollectorCount,
                        SuccessCount: livePassCount,
                        ManualReviewCount: liveFailCount,
                        StatusMessage: verification.OverallPass ? "Placed successfully" : "Verification failed"));
                }

                placedByApartmentId[apartment.Id] = placedForApartment;
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted() && !tx.HasEnded())
            {
                tx.RollBack();
            }

            TryComplete(progressReporter, $"Failed - all changes rolled back: {ex.Message}");

            message = ex.Message;
            TaskDialog.Show(
                "PlumbingSystem - מיקום קולטנים נכשל",
                $"שגיאה תוך כדי יצירת אלמנטים - כל השינויים בוטלו (Rollback):\n\n{ex.Message}");
            return Result.Failed;
        }

        TryComplete(progressReporter, $"Done - {livePassCount} passed, {liveFailCount} failed verification.");

        string report = BuildReport(apartments, placedByApartmentId, deletedCollectorCount, deletedCollectorMaterial, scopeDescription, readerWarnings);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_PlacedCollectors_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, report, Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    /// <summary>
    /// עוטפת קריאה ל-<see cref="IProgressReporter.Report"/> ב-try/catch -
    /// כשל-UI לעולם לא יכול להפיל את הפעולה ההנדסית. אותו עיקרון בדיוק
    /// כמו ב-DrawPipesCommand - ראו docs/progress-infrastructure.md.
    /// </summary>
    private static void TryReport(IProgressReporter reporter, ProgressReport update)
    {
        try
        {
            reporter.Report(update);
        }
        catch
        {
        }
    }

    /// <summary>ראו <see cref="TryReport"/> - אותו עיקרון בדיוק, עבור <see cref="IProgressReporter.Complete"/>.</summary>
    private static void TryComplete(IProgressReporter reporter, string finalMessage)
    {
        try
        {
            reporter.Complete(finalMessage);
        }
        catch
        {
        }
    }

    /// <summary>
    /// קולטן שנוצר בפועל: המיקום הגולמי (מיקום אסלה), המיקום הסופי
    /// (צמוד-קיר), ה-ElementId שנוצר, ה-Room/Level לניווט בדוח, ותוצאת
    /// האימות האוטומטי (<see cref="CollectorPlacementService.PlacementVerification"/>).
    /// </summary>
    private readonly record struct PlacedCollector(
        CollectorPoint Raw,
        CollectorPoint Snapped,
        ElementId RevitElementId,
        string RoomName,
        string RoomNumber,
        string LevelName,
        CollectorPlacementService.PlacementVerification Verification);

    /// <summary>
    /// מוצאת את ה-Room (שם, מספר, Level) שנקודה נתונה (ב-Core, מטרים)
    /// נמצאת בו בפועל - כדי לתת נקודת התייחסות קריאה בדוח ("Room 1132,
    /// קומה 2") במקום רק קואורדינטות X/Y מופשטות, וכדי לספק Level אמין
    /// לאימות האוטומטי (<see cref="CollectorPlacementService.VerifyPlacement"/>).
    /// </summary>
    private static (string RoomName, string RoomNumber, string LevelName, ElementId LevelId) GetRoomContext(
        Document doc,
        Point3D location)
    {
        XYZ point = RevitUnitConversion.ToRevitPoint(location);
        Room? room = doc.GetRoomAtPoint(point);

        if (room is null)
        {
            return ("(no Room found)", "(no Room found)", "(unknown)", ElementId.InvalidElementId);
        }

        string levelName = (doc.GetElement(room.LevelId) as Level)?.Name ?? "(unknown)";
        return (room.Name, room.Number, levelName, room.LevelId);
    }

    private static string BuildReport(
        List<Apartment> apartments,
        Dictionary<string, List<PlacedCollector>> placedByApartmentId,
        int deletedCollectorCount,
        bool deletedCollectorMaterial,
        string scopeDescription,
        List<string> readerWarnings)
    {
        List<PlacedCollector> allCollectors = placedByApartmentId.Values.SelectMany(list => list).ToList();
        int passCount = allCollectors.Count(p => p.Verification.OverallPass);
        int totalCount = allCollectors.Count;

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Collectors Placed in Revit ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Scope: {scopeDescription}");
        sb.AppendLine($"VERIFICATION SUMMARY: {passCount}/{totalCount} collectors PASS" +
            (passCount == totalCount ? " (all good)." : " - see FAIL entries below for detail."));
        sb.AppendLine($"Deleted {deletedCollectorCount} existing collector(s) from previous runs before creating new ones.");
        sb.AppendLine($"Deleted previous collector Material (if any) before creating a fresh one: {deletedCollectorMaterial}");
        sb.AppendLine($"Apartments processed: {apartments.Count}");

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
            List<PlacedCollector> placed = placedByApartmentId[apartment.Id];
            Dictionary<string, ToiletFixture> fixturesById = apartment.Fixtures.ToDictionary(f => f.Id);

            sb.AppendLine();
            sb.AppendLine($"--- Apartment '{apartment.Id}' (Floor {apartment.FloorNumber}) - {placed.Count} collector(s) ---");

            foreach (PlacedCollector p in placed)
            {
                double movedDistance = GeometryUtils.Distance2D(p.Raw.Location, p.Snapped.Location);

                CollectorPlacementService.PlacementVerification v = p.Verification;
                string overallStatus = v.OverallPass ? "PASS" : "FAIL";

                sb.AppendLine($"  Collector Id={p.Raw.Id}  RevitElementId={p.RevitElementId.Value}  [{overallStatus}]");
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    Verification: wall-proximity={0} (distance={1:F4}m, tolerance={2:F1}m)  room-membership={3} (expected #{4}, actual #{5})",
                    v.WallProximityPass ? "PASS" : "FAIL",
                    v.DistanceToNearestWallMeters,
                    CollectorPlacementService.WallProximityToleranceMeters,
                    v.RoomMembershipPass ? "PASS" : "FAIL",
                    apartment.Id,
                    v.ActualRoomNumber));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    Corner check (informational, not PASS/FAIL): corner={0} (second-nearest wall={1}, tolerance={2:F1}m)",
                    v.CornerStatus,
                    v.SecondNearestWallDistanceMeters is double secondDistance
                        ? secondDistance.ToString("F4", CultureInfo.InvariantCulture) + "m"
                        : "(no second wall found)",
                    CollectorPlacementService.CornerSecondWallToleranceMeters));
                sb.AppendLine($"    Location context (based on original fixture location): Room '{p.RoomName}' #{p.RoomNumber}, Level '{p.LevelName}'");
                sb.AppendLine($"    Original (fixture) location: {p.Raw.Location}");
                sb.AppendLine($"    Final (wall-snapped) location: {p.Snapped.Location}");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "    Moved: {0:F4}m", movedDistance));

                // כל האסלות המחוברות לקולטן הזה (לא רק זו שממנה הוא נגזר) -
                // כדי שאפשר יהיה לאמת שכולן באמת מכוסות (<=4.0 מ' מהקולטן
                // המקורי, לפני ה-snap - זה המרחק שבאמת נבדק ואושר על ידי
                // CollectorLocator; ה-snap לקיר קורה אחרי, ולא משנה את
                // ההקצאה עצמה).
                sb.AppendLine($"    Connected fixtures ({p.Raw.ConnectedFixtureIds.Count}):");
                foreach (string fixtureId in p.Raw.ConnectedFixtureIds)
                {
                    ToiletFixture fixture = fixturesById[fixtureId];
                    double distanceToCollector = GeometryUtils.Distance2D(fixture.Location, p.Raw.Location);
                    string marker = fixture.IsGuestBathroom ? " [GUEST BATHROOM]" : string.Empty;

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "      ElementId={0}  distance={1:F4}m{2}",
                        fixtureId,
                        distanceToCollector,
                        marker));
                }
            }
        }

        return sb.ToString();
    }
}
