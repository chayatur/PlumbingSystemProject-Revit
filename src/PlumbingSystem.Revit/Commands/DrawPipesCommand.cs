using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;
using PlumbingSystem.Revit.Config;
using PlumbingSystem.Revit.Progress;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// שלב 7: מחשבת (Core) ומציירת בפועל מקטע צינור לכל אסלה המחוברת לכל
/// קולטן (<see cref="CollectorPoint.ConnectedFixtureIds"/>). קודם
/// מנסה מסלול ישר (<see cref="PipeRouteCalculator.Calculate"/>) ובודקת
/// אם קיר חוסם אותו; אם כן - עוברת למסלול-עוקף דו-מקטעי
/// (<see cref="PipeRouteCalculator.CalculateDetour"/>, חוק 9 - שני
/// מקטעים בזווית ~45°, לא זווית 90° חדה). ראו docs/step7.md.
/// </summary>
/// <remarks>
/// פקודה **נפרדת** מ-<see cref="PlaceCollectorsCommand"/> (לא הרחבה
/// שלה), באותו עיקרון בדיוק שכבר הנחה את ההפרדה בין
/// <see cref="BuildCollectorsCommand"/> ל-<see cref="PlaceCollectorsCommand"/>:
/// כל פקודה אחראית על סוג-אלמנט אחד (קולטנים מול צינורות), כדי
/// שאפשר יהיה להריץ/לבדוק/למחוק כל אחת בנפרד בלי להשפיע על השנייה.
/// מחשבת מחדש (לא קוראת) את מיקומי הקולטנים - אותה קריאה מדויקת
/// ל-<see cref="RevitModelReader.ReadApartments"/> + <see cref="CollectorLocator.Locate"/> +
/// <see cref="CollectorPlacementService.SnapToNearestWallEdge"/> שכבר
/// משמשת את <see cref="PlaceCollectorsCommand"/> - דטרמיניסטי (אותו
/// מודל נותן תמיד אותה תוצאה), כך שאין צורך "לקרוא בחזרה" את
/// הגיאומטריה של קולטנים שכבר צוירו.
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class DrawPipesCommand : IExternalCommand
{
    /// <summary>
    /// חצי-רוחב (מטרים) של חתך-הצינור המצויר - **פישוט מכוון**: ריבוע
    /// (לא עיגול אמיתי) בקוטר <see cref="PipeRouteCalculator.PipeDiameterMm"/>,
    /// באותו עיקרון כמו קוביית-הסימון של הקולטן (שלב 6) - "צר וארוך"
    /// לצורך בדיקה ויזואלית, לא רכיב MEP מדויק (ראו TODO ב-docs/step7.md).
    /// </summary>
    private static double HalfWidthFeet => UnitUtils.ConvertToInternalUnits(
        PipeRouteCalculator.PipeDiameterMm / 1000.0, UnitTypeId.Meters) / 2.0;

    /// <summary>קידומת קבועה של <c>Element.Name</c> לכל צינור - אות-זיהוי שלישי, כמו <see cref="CollectorPlacementService"/> לקולטנים.</summary>
    private const string PipeElementNamePrefix = "PlumbingSystem Pipe ";

    /// <summary>
    /// אורך כל "גדם" (מטרים) שמצויר במקום מקטע מלא כשמקטע מסומן
    /// <c>RequiresManualEngineering</c> - ערך לדוגמה (15-20 ס"מ), ניתן-
    /// לכיוונון. ראו <see cref="BuildManualEngineeringStubs"/>.
    /// </summary>
    private const double ManualEngineeringStubLengthMeters = 0.2;

    /// <summary>
    /// שם ה-Material הכתום-עמום הייחודי לגדמי "דורש פתרון ידני" - אותו
    /// עיקרון בדיוק כמו Material הקולטנים (שלב 6). צבע **עמום**
    /// (לא זרחני) - תוקן בעקבות משוב על מראה "debug-view" לא-מקצועי.
    /// </summary>
    private const string ManualEngineeringMaterialName = "PlumbingSystem Manual Engineering Orange";

    /// <summary>
    /// שם קודם (לפני התיקון ל-"מראה מקצועי") של Material הצינורות
    /// התקינים - נמחק במפורש בתחילת כל הרצה (ראו <see cref="Execute"/>)
    /// כניקוי-הגירה חד-פעמי, כדי לא להשאיר Material ישן ולא-בשימוש
    /// בקובץ (אותו עיקרון-זהירות כמו כל מחיקה אחרת בפרויקט הזה).
    /// </summary>
    private const string ObsoleteNormalPipeMaterialName = "PlumbingSystem Pipe DarkGreen";

    /// <summary>
    /// שם ה-Material הכחול-בינוני הייחודי לצינורות **תקינים** (ישר/עוקף/
    /// Y-מדורג שהצליחו - <c>obstruction=OK</c>, לא <c>RequiresManualEngineering</c>).
    /// **גילוי שהוביל לתיקון**: עד עכשיו צינורות תקינים לא קיבלו שום
    /// Material בכלל - נוצרו לבנים (ברירת-המחדל של Revit), ולכן לא
    /// נראו בתצוגה רגילה. **צבע נבחר במכוון**: כחול-בינוני, קרוב
    /// למוסכמה המקובלת ב-Revit MEP (למשל צנרת מים קרים) - נראה "טבעי"
    /// למי שמכיר תוכניות-אינסטלציה, לא זרחני/דגל-אבחון.
    /// </summary>
    private const string NormalPipeMaterialName = "PlumbingSystem Pipe Blue";

    /// <summary>צבע כתום-עמום (לא זרחני) לגדמי "דורש פתרון ידני" - מקור יחיד, משמש גם ל-Material וגם ל-Override.</summary>
    private static readonly Color ManualEngineeringColor = new(196, 110, 40);

    /// <summary>צבע כחול-בינוני לצינורות תקינים - מקור יחיד, משמש גם ל-Material וגם ל-Override.</summary>
    private static readonly Color NormalPipeColor = new(0, 112, 192);

    /// <summary>
    /// טקסט התווית שמוצמדת לכל מקטע <c>RequiresManualEngineering</c> -
    /// ראו <see cref="CreateManualEngineeringTextNote"/>. קצר בכוונה -
    /// אמור להיקרא מיד בתוכנית-קומה רגילה, לא להיבלע בטקסט ארוך.
    /// </summary>
    private const string ManualEngineeringNoteText = "דורש בדיקת מהנדס";

    /// <summary>
    /// כלל הנדסי שאושר במפורש (2026-09-02, המנהלת): **מותר לצינור הביוב
    /// להיכנס לתוך עובי הקיר כדי להגיע לקולטן ולהתחבר אליו**. הקבוע הזה
    /// חוסם את החדירה כדי שלא "תזלוג": route בדרך אל הקולטן רשאי לחדור
    /// לתוך קיר ש**גוף-הקיר שלו מכיל את הקולטן** (ראו <see cref="FindCollectorWallPenetration"/>) -
    /// אבל רק במקטע האחרון (זה שמסתיים בקולטן), ורק עד למרחק
    /// <c>עובי-הקיר × הגורם הזה</c> מסוף המקטע (= מהקולטן). גורם 2.0 (לא
    /// 1.0) כדי לאפשר גם כניסה אלכסונית לקיר (הצינור עובר מרחק גדול
    /// מעובי-הקיר-הנקי כשהוא לא ניצב אליו), אבל עדיין חוסם route
    /// ש"מתגלגל" לאורך הקיר הרבה מעבר לכך. **קירות אחרים בדרך - obstruction
    /// רגיל, ללא שינוי.** ראו docs/pipe-rca-chain.md חלק ה'.
    /// </summary>
    private const double CollectorWallPenetrationWidthFactor = 2.0;

    /// <summary>
    /// סבילות (מטרים) לקביעה "מיקום הקולטן נמצא בתוך גוף הקיר הזה" -
    /// נבדק כ-<c>מרחק(קולטן, קו-מיקום-הקיר) ≤ חצי-עובי-הקיר + הסבילות
    /// הזו</c>. קטן בכוונה (2 ס"מ) - רק שוליים לדיוק floating-point, לא
    /// מרווח הנדסי.
    /// </summary>
    private const double CollectorWallContainmentToleranceMeters = 0.02;

    /// <summary>
    /// מריצה <see cref="RevitModelReader.ReadApartments"/> ו-
    /// <see cref="CollectorLocator.Locate"/> ו-<see cref="CollectorPlacementService.SnapToNearestWallEdge"/>
    /// (בדיוק כמו <see cref="PlaceCollectorsCommand"/>, כדי לקבל את
    /// אותם מיקומי-קולטן מדויקים בלי ליצור/למחוק קולטנים כאן). לכל אסלה
    /// מחוברת: מחשבת מסלול ישר, בודקת חסימה - אם נמצאה, מחשבת ומציירת
    /// מסלול-עוקף במקומו (ראו <see cref="BuildRoute"/>). בתוך
    /// <see cref="Autodesk.Revit.DB.Transaction"/> אחד - **קודם** מוחקת
    /// צינורות ישנים (<see cref="DeleteExistingPipes"/>), ואז יוצרת את
    /// כל המסלולים. אם שגיאה קורית תוך כדי, כל ה-Transaction מתבטל
    /// (<c>RollBack</c>) - כולל המחיקה - אותו עיקרון בדיוק כמו
    /// <see cref="PlaceCollectorsCommand"/>.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">מתמלא בהודעת השגיאה אם הפעולה נכשלה.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> אם כל מסלולי הצינור נוצרו והדוח נכתב ונפתח.</returns>
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

        // הפקודה הזו (בניגוד ל-PlaceCollectorsCommand/BuildCollectorsCommand)
        // תמיד מחריגה קומה 0 (excludeFloorZero: true, ראו הקריאה ל-
        // ReadApartments למטה) - גם במצב "כל הבניין", אז מוסיפים את
        // ההערה הזו לתיאור-ההיקף כאן, לא בתוך ScopeSelector המשותף
        // (שלא יודע איזה קורא מחריג קומה 0 ואיזה לא).
        if (onlyLevelId is null)
        {
            scopeDescription += " except Floor 0 - commercial, always excluded";
        }

        var reader = new RevitModelReader(doc);
        var placementService = new CollectorPlacementService(doc);
        var wallRayCasting = new WallRayCasting(doc);

        List<Apartment> apartments;
        List<string> readerWarnings;
        Dictionary<string, List<CollectorPoint>> rawCollectorsByApartmentId;
        try
        {
            apartments = reader.ReadApartments(onlyLevelId, excludeFloorZero: true);
            readerWarnings = reader.Warnings.ToList();
            rawCollectorsByApartmentId = apartments.ToDictionary(a => a.Id, CollectorLocator.Locate);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - ציור צינורות נכשל", ex.Message);
            return Result.Failed;
        }

        // דיווח-התקדמות-חי (ראו docs/progress-infrastructure.md) - נוצר
        // רק **אחרי** ש-apartments כבר נקראו בהצלחה (מספר-הפריטים-הכולל
        // ידוע בפועל, לא הערכה). כישלון ביצירת-החלון עצמו (למשל בעיית-
        // WPF כלשהי) נופל בחזרה ל-NullProgressReporter - הריצה ההנדסית
        // ממשיכה בדיוק כרגיל, בלי שום תלות בהצלחת-ה-UI.
        int totalFixtureCount = apartments.Sum(a => a.Fixtures.Count);
        IProgressReporter progressReporter;
        try
        {
            progressReporter = new ProgressWindowReporter("Draw Pipes", totalFixtureCount);
        }
        catch
        {
            progressReporter = NullProgressReporter.Instance;
        }

        int processedFixtureCount = 0;
        int liveSuccessCount = 0;
        int liveManualCount = 0;

        var drawnByApartmentId = new Dictionary<string, List<DrawnPipe>>();

        using Transaction tx = new(doc, "PlumbingSystem - Draw Pipes");
        tx.Start();

        int deletedPipeCount;
        ElementId? manualEngineeringMaterialId = null;
        ElementId? normalPipeMaterialId = null;

        try
        {
            // מחיקת צינורות ישנים (מהרצות קודמות) בתוך **אותו** Transaction
            // כמו היצירה - אותו עיקרון בדיוק כמו CollectorPlacementService.DeleteExistingCollectors.
            deletedPipeCount = DeleteExistingPipes(doc);
            DeleteExistingManualEngineeringNotes(doc);

            // ניקוי-הגירה חד-פעמי: Material בשם הישן (מלפני החלפת הצבע
            // לכחול-בינוני) - לא ישמש שוב, אז לא משאירים אותו בקובץ.
            new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => m.Name == ObsoleteNormalPipeMaterialName)
                .ToList()
                .ForEach(m => doc.Delete(m.Id));

            foreach (Apartment apartment in apartments)
            {
                Dictionary<string, ToiletFixture> fixturesById = apartment.Fixtures.ToDictionary(f => f.Id);
                var drawnForApartment = new List<DrawnPipe>();

                foreach (CollectorPoint rawCollector in rawCollectorsByApartmentId[apartment.Id])
                {
                    (CollectorPoint snappedCollector, _) = placementService.SnapToNearestWallEdge(rawCollector);

                    foreach (string fixtureId in rawCollector.ConnectedFixtureIds)
                    {
                        ToiletFixture fixture = fixturesById[fixtureId];

                        (IReadOnlyList<PipeSegment> routeSegments, bool isDetour, bool detourUnavailable,
                            bool requiresManualEngineering, ElementId? finalBlockingWallId,
                            WallEdgeSnapper.WallSegment? blockingWallGeometry) =
                            BuildRoute(doc, wallRayCasting, fixture, snappedCollector);

                        // אבחון-מלא (כל הניסיונות, לא רק "PASS/FAIL סופי") נאסף
                        // ב**מעבר שני**, נפרד - רק עבור מקטעים שכבר ידוע (מהמעבר
                        // הראשון, למעלה) שנכשלו בכל הצירופים. BuildRoute דטרמיניסטית
                        // (Core טהורה + ray-casting יציב בתוך אותו Transaction) -
                        // ריצה חוזרת נותנת בדיוק אותה תוצאה, בלי סיכון לסטייה בין
                        // שני המעברים, ובלי שום עלות-ביצועים נוספת למקטעים תקינים
                        // (המעבר השני פשוט לא רץ עבורם).
                        var diagnostics = new List<AttemptDiagnostic>();
                        if (requiresManualEngineering)
                        {
                            BuildRoute(doc, wallRayCasting, fixture, snappedCollector, diagnostics);
                        }

                        string routeId = PipeRouteCalculator.BuildRouteId(fixture, snappedCollector);

                        DirectShape element;
                        if (requiresManualEngineering)
                        {
                            // Material כתום, נוצר-פעם-אחת (ממש, לא בכל מקטע) ומוחל
                            // על כל גדמי "דורש פתרון ידני" באותה הרצה - ראו GetManualEngineeringMaterialId.
                            manualEngineeringMaterialId ??= GetManualEngineeringMaterialId(doc);
                            element = CreatePipeElement(
                                doc, routeId, routeSegments, manualEngineeringMaterialId, activeView,
                                orange: true, extendAtJoints: false);

                            // תווית טקסטואלית - ראו CreateManualEngineeringTextNote:
                            // הגיאומטריה התלת-ממדית (כמו הקולטנים) קשה-לראות
                            // בקנה-מידה רגיל בלי Isolate; Text Note מוצג תמיד
                            // "מעל" בתצוגה, בלי תלות בגובה-חיתוך.
                            CreateManualEngineeringTextNote(doc, activeView, routeId, routeSegments);
                        }
                        else
                        {
                            // Material כחול-בינוני, נוצר-פעם-אחת ומוחל על כל הצינורות
                            // התקינים (ישר/עוקף/Y-מדורג) - ראו GetNormalPipeMaterialId.
                            normalPipeMaterialId ??= GetNormalPipeMaterialId(doc);
                            element = CreatePipeElement(doc, routeId, routeSegments, normalPipeMaterialId, activeView, orange: false);
                        }

                        drawnForApartment.Add(new DrawnPipe(
                            fixture.Id,
                            snappedCollector.Id,
                            routeId,
                            routeSegments,
                            element.Id,
                            finalBlockingWallId,
                            isDetour,
                            detourUnavailable,
                            requiresManualEngineering,
                            diagnostics,
                            blockingWallGeometry));

                        // דיווח-התקדמות: **אחרי** שהתוצאה האמיתית של המקטע
                        // הזה כבר ידועה במלואה (כולל האלמנט שכבר נוצר
                        // ב-Revit) - לא ניחוש/צפי. לא משנה את סדר-העיבוד
                        // או משהו אחר בלולאה - רק שורה נוספת בסופה.
                        processedFixtureCount++;
                        if (requiresManualEngineering)
                        {
                            liveManualCount++;
                        }
                        else
                        {
                            liveSuccessCount++;
                        }

                        TryReport(progressReporter, new ProgressReport(
                            Floor: apartment.FloorNumber.ToString(CultureInfo.InvariantCulture),
                            Apartment: apartment.Id,
                            CurrentItem: fixture.Id,
                            ProgressCurrent: processedFixtureCount,
                            ProgressTotal: totalFixtureCount,
                            SuccessCount: liveSuccessCount,
                            ManualReviewCount: liveManualCount,
                            StatusMessage: requiresManualEngineering ? "Manual review required" : "Routed successfully"));
                    }
                }

                drawnByApartmentId[apartment.Id] = drawnForApartment;
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
                "PlumbingSystem - ציור צינורות נכשל",
                $"שגיאה תוך כדי יצירת צינורות - כל השינויים בוטלו (Rollback):\n\n{ex.Message}");
            return Result.Failed;
        }

        TryComplete(progressReporter, $"Done - {liveSuccessCount} succeeded, {liveManualCount} require manual review.");

        string report = BuildReport(doc, apartments, drawnByApartmentId, deletedPipeCount, scopeDescription, readerWarnings);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_Pipes_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, report, Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        List<DrawnPipe> allPipes = drawnByApartmentId.Values.SelectMany(list => list).ToList();
        int manualRequiredCount = allPipes.Count(p => p.RequiresManualEngineering);
        if (manualRequiredCount > 0)
        {
            // דוח-אבחון **נפרד** (לא מוסיפים לדוח הרגיל למעלה) - מיועד
            // להעברה למהנדס-אינסטלציה חיצוני: לכל מקטע RequiresManualEngineering,
            // כל 28 הניסיונות שנוסו (לא רק "נכשל") + גיאומטריית הקיר החוסם -
            // ראו BuildManualEngineeringDiagnosticReport.
            string diagnosticReport = BuildManualEngineeringDiagnosticReport(doc, apartments, drawnByApartmentId);
            string diagnosticPath = Path.Combine(
                Path.GetTempPath(),
                $"PlumbingSystem_ManualEngineeringDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(diagnosticPath, diagnosticReport, Encoding.UTF8);
            Process.Start(new ProcessStartInfo(diagnosticPath) { UseShellExecute = true });

            TaskDialog.Show(
                "PlumbingSystem - נדרש תכנון ידני",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} מתוך {1} מקטעים דורשים תכנון ידני של מהנדס אינסטלציה - " +
                    "ראה תוויות כתומות \"{2}\" בתוכנית (ליד גדמי הצינור הלא-מחוברים), " +
                    "וקובץ-אבחון מפורט שנפתח בנפרד.",
                    manualRequiredCount,
                    allPipes.Count,
                    ManualEngineeringNoteText));
        }

        return Result.Succeeded;
    }

    /// <summary>
    /// עוטפת קריאה ל-<see cref="IProgressReporter.Report"/> ב-try/catch -
    /// כשל-UI (מכל סוג) לעולם לא יכול להפיל את הפעולה ההנדסית האמיתית.
    /// זו העטיפה בצד-הקורא - הגנה **נוספת** על-גבי ההגנה-הפנימית שכבר
    /// יש ב-<see cref="ProgressWindowReporter"/> עצמה, לא תחליף לה. ראו
    /// docs/progress-infrastructure.md.
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
    /// מסלול צינור שצויר בפועל: מזהי האסלה/הקולטן, מזהה-המסלול המשותף,
    /// כל מקטעי-הדרך (אחד אם ישר, שניים אם מסלול-עוקף), ה-ElementId
    /// שנוצר (אחד לכל המסלול - <see cref="CreatePipeElement"/> מרכיבה
    /// את כל המקטעים ל-<see cref="DirectShape"/> יחיד), ותוצאת בדיקת-
    /// החסימה **הסופית** (אחרי ניסיון-עקיפה, אם היה). <see cref="RequiresManualEngineering"/>
    /// מסמן שגם הצד המועדף וגם הצד השני נכשלו - זו מגבלה גיאומטרית
    /// אמיתית (לא כישלון-קוד) - ראו docs/step7.md.
    /// </summary>
    private readonly record struct DrawnPipe(
        string FixtureId,
        string CollectorId,
        string RouteId,
        IReadOnlyList<PipeSegment> Segments,
        ElementId RevitElementId,
        ElementId? BlockingWallId,
        bool IsDetour,
        bool DetourUnavailable,
        bool RequiresManualEngineering,
        IReadOnlyList<AttemptDiagnostic> Diagnostics,
        WallEdgeSnapper.WallSegment? BlockingWallGeometry);

    /// <summary>
    /// תיעוד-אבחון של ניסיון-עקיפה **בודד** (אחד מתוך עד 28 שנוסים ב-
    /// <see cref="BuildRoute"/> - נאסף **בזמן-אמת** תוך כדי ההרצה עצמה
    /// (לא משוחזר בדיעבד), כדי שלא יהיה צורך "לנחש" בדיעבד מה בדיוק
    /// קרה בכל ניסיון. נאסף עבור **כל** מקטע (לא רק <c>RequiresManualEngineering</c>),
    /// אבל נכתב לדוח-האבחון (<see cref="BuildManualEngineeringDiagnosticReport"/>)
    /// רק עבור מקטעים שבסוף אכן נשארו <c>RequiresManualEngineering</c> -
    /// לצורך העברה למהנדס-אינסטלציה חיצוני, ראו בקשת המשתמשת.
    /// </summary>
    private readonly record struct AttemptDiagnostic(
        string Label,
        string CoreOutcome,
        string? WaypointsMeters,
        bool? StillBlockedAfterBuild,
        string? BlockingDetail);

    /// <summary>
    /// אורכי מקטע-ביניים (מטרים) שמנוסים במסלול "Y מדורג" (ראו
    /// <see cref="PipeRouteCalculator.CalculateStaggeredDetour"/>) לפני
    /// שמוותרים - סדרה עולה, כדי לנסות קודם עקיפה "צנועה" (מרחק-
    /// ביניים קצר) ולעבור לגדולה יותר רק אם צריך. ערכים לדוגמה, ניתנים-
    /// לכיוונון - לא נתון הנדסי מאושר.
    /// </summary>
    private static readonly double[] StaggeredCrossoverLengthCandidatesMeters =
        { 0.1, 0.2, 0.3, 0.5, 0.75, 1.0 };

    /// <summary>
    /// בונה את מסלול הצינור: מנסה קודם מסלול ישר
    /// (<see cref="PipeRouteCalculator.Calculate"/>) ובודקת אם קיר חוסם
    /// אותו (<see cref="FindObstructingWall"/>). אם אין חסימה - מחזירה
    /// את המקטע הישר כמו שהוא. אם יש - שולפת את גיאומטריית הקיר החוסם
    /// (<see cref="GetWallSegment"/>) ומנסה, בסדר הזה: (1) מסלול-עוקף
    /// דו-מקטעי **בשני הצדדים** (<see cref="TryBuildDetour"/>, חוק 9 -
    /// "2 זוויות של 45°" כפי שמומש), (2) אם שניהם נכשלו - מסלול
    /// "Y מדורג" תלת-מקטעי (<see cref="TryBuildStaggeredDetour"/>, חוק 9 -
    /// האלטרנטיבה "או Y מדורג" - שתי פניות עדינות יותר, עם דרגת-חופש
    /// נוספת של אורך-ביניים, שעשויה להצליח דווקא במרחבים צרים שבהם
    /// הדו-מקטעי הקשיח נכשל). אם **גם זה** נכשל בכל הצירופים שנוסו -
    /// זו מגבלה גיאומטרית אמיתית שהתגלתה בפועל, **מוכחת** (14 ניסיונות
    /// שונים על אותם 5 מקטעים אמיתיים - כולם נכשלו, אותם קירות בדיוק -
    /// ראו docs/step7.md), לא עוד באג לתקן: מסומנת כ-<c>RequiresManualEngineering</c>,
    /// ומצוירים שני "גדמים" קצרים ולא-מחוברים (<see cref="BuildManualEngineeringStubs"/>)
    /// במקום קו-מלא-חוצה-קיר - וה-Transaction **ממשיך** לשאר המקטעים,
    /// לא נזרקת שגיאה שעוצרת את כל הריצה.
    /// אם הקיר החוסם אינו קיר-ישר פשוט (<c>LocationCurve</c> שאינו
    /// <c>Line</c>) - לא ניתן אפילו לנסות מסלול-עוקף (אותה מגבלה
    /// בדיוק כמו קירות מעוקלים בשלב 6) - נשאר המסלול הישר, מסומן
    /// בדוח כ"חסום, עקיפה לא נוסתה" (לא זהה ל"דורש פתרון ידני").
    /// </summary>
    private static (
        IReadOnlyList<PipeSegment> Segments,
        bool IsDetour,
        bool DetourUnavailable,
        bool RequiresManualEngineering,
        ElementId? BlockingWallId,
        WallEdgeSnapper.WallSegment? BlockingWallGeometry) BuildRoute(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        CollectorPoint collector,
        List<AttemptDiagnostic>? diagnostics = null)
    {
        PipeSegment straightSegment = PipeRouteCalculator.Calculate(fixture, collector);
        ElementId? blockingWallId = FindObstructingWall(doc, wallRayCasting, fixture, straightSegment);

        if (blockingWallId is null)
        {
            return (new[] { straightSegment }, false, false, false, null, null);
        }

        // הכלל ההנדסי שאושר (2026-09-02, המנהלת): מותר לצינור להיכנס לתוך
        // עובי הקיר שמכיל את הקולטן, בדרך אל הקולטן. מאתרים את הקיר/ים
        // האלה **רק כשכבר יש חסימה** (אין עלות למקטעים תקינים), ובודקים
        // מחדש את המסלול הישר: אם החסימה היחידה הייתה הכניסה לקיר של
        // הקולטן - המסלול הישר כשר עכשיו. אם עדיין חוסם קיר אחר - ממשיכים
        // לניסיונות-העקיפה מול אותו קיר, כשגם להם מותרת אותה חדירה במקטע
        // האחרון. ראו CollectorWallPenetrationWidthFactor, docs/pipe-rca-chain.md.
        CollectorWallPenetration? collectorWalls = FindCollectorWallPenetration(doc, fixture, collector);
        if (collectorWalls is not null)
        {
            ElementId? blockingWallIdAllowingCollectorWall =
                FindObstructingWall(doc, wallRayCasting, fixture, straightSegment, collectorWalls);
            if (blockingWallIdAllowingCollectorWall is null)
            {
                return (new[] { straightSegment }, false, false, false, null, null);
            }

            blockingWallId = blockingWallIdAllowingCollectorWall;
        }

        WallEdgeSnapper.WallSegment? wallSegment = GetWallSegment(doc, blockingWallId!);
        if (wallSegment is null)
        {
            return (new[] { straightSegment }, false, true, false, blockingWallId, null);
        }

        // סבב 1: זווית-הפנייה נגזרת מ-D (הקו הישר) - ראו docs/step7.md.
        IReadOnlyList<PipeSegment>? attempt =
            TryBuildDetour(doc, wallRayCasting, fixture, collector, wallSegment.Value, useOppositeSide: false, useWallDirectionAsReference: false, diagnostics, collectorWalls)
            ?? TryBuildDetour(doc, wallRayCasting, fixture, collector, wallSegment.Value, useOppositeSide: true, useWallDirectionAsReference: false, diagnostics, collectorWalls)
            ?? TryBuildStaggeredDetour(doc, wallRayCasting, fixture, collector, wallSegment.Value, useWallDirectionAsReference: false, diagnostics, collectorWalls)
            // סבב 2: זווית-הפנייה נגזרת מכיוון **הקיר החוסם עצמו** - לא
            // שאלת-כיוונון-קודמת, שאלה חדשה (ראו התיעוד ב-
            // PipeRouteCalculator.ComputeBendDirections). ייתכן שהבנייה
            // הזו תיכשל (גיאומטרית - "אחורה") ליותר גיאומטריות מהגרסה
            // המבוססת-D, כי U1/U2 לא בהכרח קרובים לכיוון-ההתקדמות - זה
            // מטופל כבר (try/catch) בדיוק כמו כל ניסיון אחר.
            ?? TryBuildDetour(doc, wallRayCasting, fixture, collector, wallSegment.Value, useOppositeSide: false, useWallDirectionAsReference: true, diagnostics, collectorWalls)
            ?? TryBuildDetour(doc, wallRayCasting, fixture, collector, wallSegment.Value, useOppositeSide: true, useWallDirectionAsReference: true, diagnostics, collectorWalls)
            ?? TryBuildStaggeredDetour(doc, wallRayCasting, fixture, collector, wallSegment.Value, useWallDirectionAsReference: true, diagnostics, collectorWalls);

        if (attempt is not null)
        {
            return (attempt, true, false, false, null, null);
        }

        // הוכח (28 ניסיונות - 2+12 ביחס-ל-D, 2+12 ביחס-לכיוון-הקיר -
        // כולם נכשלו על אותם 5 מקטעים בפועל, אותם קירות) שזו מגבלה
        // גיאומטרית אמיתית, לא באג. **לא** מציירים קו-מלא-חוצה-קיר
        // (נראה כמו טעות) - שני גדמים קצרים, לא-מחוברים, מסמנים בבירור
        // "כאן יש פער שדורש החלטת-מהנדס" - ראו BuildManualEngineeringStubs, docs/step7.md.
        IReadOnlyList<PipeSegment> stubs = BuildManualEngineeringStubs(fixture, collector);
        return (stubs, false, false, true, blockingWallId, wallSegment);
    }

    /// <summary>
    /// בונה שני "גדמים" קצרים (<see cref="ManualEngineeringStubLengthMeters"/>,
    /// לא מחוברים ביניהם) - אחד מהאסלה לכיוון הקולטן, אחד מהקולטן
    /// לכיוון האסלה - לציור ויזואלי כשמקטע מסומן <c>RequiresManualEngineering</c>.
    /// **בכוונה לא מקטע מלא**: קו-ישר שלם שחוצה קיר בפועל נראה כמו
    /// טעות/באג, לא כמו "זוהה ותועד" - שני גדמים-שאינם-מחוברים מסמנים
    /// בבירור, בעין, שיש כאן פער שטרם נפתר, לא צינור-גמור.
    /// </summary>
    private static IReadOnlyList<PipeSegment> BuildManualEngineeringStubs(
        ToiletFixture fixture, CollectorPoint collector)
    {
        double totalDistance = GeometryUtils.Distance2D(fixture.Location, collector.Location);
        double stubLength = Math.Min(ManualEngineeringStubLengthMeters, totalDistance / 2.0);

        double directionX = (collector.Location.X - fixture.Location.X) / totalDistance;
        double directionY = (collector.Location.Y - fixture.Location.Y) / totalDistance;

        var fixtureStubEnd = new Point3D(
            fixture.Location.X + (stubLength * directionX),
            fixture.Location.Y + (stubLength * directionY),
            fixture.Location.Z);

        var collectorStubEnd = new Point3D(
            collector.Location.X - (stubLength * directionX),
            collector.Location.Y - (stubLength * directionY),
            collector.Location.Z);

        string routeId = PipeRouteCalculator.BuildRouteId(fixture, collector);

        // slopePercent=0: אלו סמני-אבחון, לא מקטעים הנדסיים אמיתיים -
        // PipeSegment לא מבצעת ולידציה הנדסית על השדה הזה (ראו התיעוד
        // במודל עצמו), אז אין בעיה שהוא לא בטווח התקין.
        var stubFromFixture = new PipeSegment(
            id: $"{routeId}-manual-stub-fixture",
            startPoint: fixture.Location,
            endPoint: fixtureStubEnd,
            diameterMm: PipeRouteCalculator.PipeDiameterMm,
            slopePercent: 0);

        var stubFromCollector = new PipeSegment(
            id: $"{routeId}-manual-stub-collector",
            startPoint: collector.Location,
            endPoint: collectorStubEnd,
            diameterMm: PipeRouteCalculator.PipeDiameterMm,
            slopePercent: 0);

        return new[] { stubFromFixture, stubFromCollector };
    }

    /// <summary>
    /// יוצרת <see cref="TextNote"/> (לא <see cref="DirectShape"/> תלת-ממדי)
    /// בדיוק ב"פער" שבין שני הגדמים (<paramref name="stubSegments"/>) -
    /// **הפתרון לבעיית-הנראות החוזרת**: גיאומטריה תלת-ממדית (כמו
    /// הקולטנים וגם הצינורות) מתגלה שוב ושוב כקשה-לזיהוי בקנה-מידה
    /// רגיל בלי Isolate/Select-by-ID. <c>TextNote</c> הוא אלמנט-annotation
    /// דו-ממדי, ספציפי-ל-View - מוצג תמיד "מעל" הגיאומטריה בתצוגה שבה
    /// נוצר, בלי תלות בגובה-חיתוך/View Range שמשפיע על DirectShape.
    /// </summary>
    private static void CreateManualEngineeringTextNote(
        Document doc, View activeView, string routeId, IReadOnlyList<PipeSegment> stubSegments)
    {
        Point3D gapMidpoint = new(
            (stubSegments[0].EndPoint.X + stubSegments[1].EndPoint.X) / 2.0,
            (stubSegments[0].EndPoint.Y + stubSegments[1].EndPoint.Y) / 2.0,
            (stubSegments[0].EndPoint.Z + stubSegments[1].EndPoint.Z) / 2.0);

        XYZ position = RevitUnitConversion.ToRevitPoint(gapMidpoint);
        ElementId textNoteTypeId = GetOrCreateSmallTextNoteTypeId(doc);

        TextNote note = TextNote.Create(doc, activeView.Id, position, ManualEngineeringNoteText, textNoteTypeId);
        note.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(routeId);
        note.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(routeId);
    }

    /// <summary>שם ה-TextNoteType הקטן הייחודי - ראו <see cref="GetOrCreateSmallTextNoteTypeId"/>.</summary>
    private const string SmallNoteTypeName = "PlumbingSystem Small Note";

    /// <summary>מקדם-הקטנה (יחסי לגודל ברירת-המחדל של המסמך) - ערך לדוגמה, ניתן-לכיוונון.</summary>
    private const double SmallNoteTextSizeScale = 0.6;

    /// <summary>
    /// מוצאת (או יוצרת, בפעם הראשונה) <see cref="TextNoteType"/> ייעודי
    /// בגודל-טקסט מוקטן (<see cref="SmallNoteTextSizeScale"/> מגודל
    /// ברירת-המחדל של המסמך) - כדי שהתווית "דורש בדיקת מהנדס" לא
    /// "תצעק" יותר מדי על תוכנית-הקומה.
    /// </summary>
    /// <remarks>
    /// **שונה במכוון מ-<see cref="GetOrCreateFreshMaterial"/>**: כאן
    /// **לא** מוחקים-ויוצרים-מחדש בכל הרצה - <c>TextNoteType</c> הוא
    /// הגדרת-עיצוב בלבד, בלי סיכון-הזליגה שהיה ב-Material (שלב 6) -
    /// שימוש-חוזר בטוח ולגיטימי, לא "עקיפת-בדיקה" כמו שם. אם הטיפוס
    /// כבר קיים (מהרצה קודמת), הוא פשוט נשלף מחדש כמו שהוא.
    /// </remarks>
    private static ElementId GetOrCreateSmallTextNoteTypeId(Document doc)
    {
        TextNoteType? existing = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault(t => t.Name == SmallNoteTypeName);

        if (existing is not null)
        {
            return existing.Id;
        }

        ElementId defaultTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (doc.GetElement(defaultTypeId) is not TextNoteType defaultType
            || defaultType.Duplicate(SmallNoteTypeName) is not TextNoteType smallType)
        {
            // נפילה בטוחה - עדיף גודל-ברירת-מחדל (לא קטן) מאשר שגיאה
            // שעוצרת את כל ההרצה בגלל תווית לא-קריטית.
            return defaultTypeId;
        }

        Parameter? textSizeParam = smallType.get_Parameter(BuiltInParameter.TEXT_SIZE);
        if (textSizeParam is not null && !textSizeParam.IsReadOnly)
        {
            textSizeParam.Set(textSizeParam.AsDouble() * SmallNoteTextSizeScale);
        }

        return smallType.Id;
    }

    /// <summary>
    /// מוחקת תוויות-טקסט (<see cref="TextNote"/>) שנוצרו בהרצות קודמות -
    /// אותו עיקרון בדיוק כמו <see cref="DeleteExistingPipes"/> (זיהוי
    /// לפי Comments/Mark שמתחיל ב-<see cref="PipeRouteCalculator.PipeIdPrefix"/>) -
    /// אבל על מחלקת <c>TextNote</c>, לא קטגוריית Generic Model, כי
    /// תוויות-טקסט הן סוג-אלמנט שונה לגמרי.
    /// </summary>
    private static int DeleteExistingManualEngineeringNotes(Document doc)
    {
        List<ElementId> existingNoteIds = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNote))
            .Cast<TextNote>()
            .Where(note =>
            {
                string? comments = note.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                string? mark = note.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                return (comments?.StartsWith(PipeRouteCalculator.PipeIdPrefix, StringComparison.Ordinal) ?? false)
                    || (mark?.StartsWith(PipeRouteCalculator.PipeIdPrefix, StringComparison.Ordinal) ?? false);
            })
            .Select(note => note.Id)
            .ToList();

        if (existingNoteIds.Count > 0)
        {
            doc.Delete(existingNoteIds);
        }

        return existingNoteIds.Count;
    }

    /// <summary>
    /// מנסה מסלול "Y מדורג" - עוברת על <see cref="StaggeredCrossoverLengthCandidatesMeters"/>
    /// (סדר עולה), ולכל אורך-ביניים מנסה את שני הצדדים - עוצרת ומחזירה
    /// בהצלחה הראשונה (גיאומטרית תקינה, ולא חוסמת קיר בפועל). מחזירה
    /// <c>null</c> אם **כל** הצירופים נכשלו.
    /// </summary>
    /// <param name="doc">המסמך הפעיל.</param>
    /// <param name="wallRayCasting">עוזר-הקרניים המשותף לבדיקת-חסימה.</param>
    /// <param name="fixture">האסלה.</param>
    /// <param name="collector">הקולטן.</param>
    /// <param name="wallSegment">הקיר החוסם (גיאומטריה טהורה).</param>
    /// <param name="useWallDirectionAsReference">מועבר כמו-שהוא ל-<see cref="PipeRouteCalculator.CalculateStaggeredDetour"/>.</param>
    /// <param name="diagnostics">
    /// אם ניתנה (לא <c>null</c>) - כל אחד מ-12 הצירופים (6 אורכי-ביניים
    /// × 2 צדדים) מתועד כ-<see cref="AttemptDiagnostic"/> משלו, **בזמן-
    /// אמת** תוך כדי הניסיון עצמו - לא רק "PASS/FAIL" אלא גם אם Core
    /// זרקה (ואיזו הודעה), ואם לא - איפה בדיוק (אם בכלל) המסלול פגע
    /// בקיר. ראו <see cref="BuildManualEngineeringDiagnosticReport"/>.
    /// </param>
    /// <param name="collectorWalls">
    /// אם ניתנה (לא <c>null</c>) - הכלל ההנדסי שאושר (2026-09-02): המקטע
    /// האחרון (זה שמסתיים בקולטן) רשאי לחדור לתוך הקירות שמכילים את
    /// הקולטן, עד לקולטן. מועבר כמו-שהוא ל-<see cref="FindObstructingWallForRouteDetailed"/>.
    /// ראו <see cref="FindCollectorWallPenetration"/>.
    /// </param>
    private static IReadOnlyList<PipeSegment>? TryBuildStaggeredDetour(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        CollectorPoint collector,
        WallEdgeSnapper.WallSegment wallSegment,
        bool useWallDirectionAsReference,
        List<AttemptDiagnostic>? diagnostics = null,
        CollectorWallPenetration? collectorWalls = null)
    {
        foreach (double crossoverLengthMeters in StaggeredCrossoverLengthCandidatesMeters)
        {
            foreach (bool useOppositeSide in new[] { false, true })
            {
                string label = string.Format(
                    CultureInfo.InvariantCulture,
                    "Staggered-Y | crossover={0:F2}m | side={1} | reference={2}",
                    crossoverLengthMeters,
                    useOppositeSide ? "opposite" : "preferred",
                    useWallDirectionAsReference ? "wall direction" : "fixture-collector line (D)");

                IReadOnlyList<PipeSegment> segments;
                try
                {
                    segments = PipeRouteCalculator.CalculateStaggeredDetour(
                        fixture, collector, wallSegment, crossoverLengthMeters, useOppositeSide, useWallDirectionAsReference);
                }
                catch (InvalidOperationException ex)
                {
                    diagnostics?.Add(new AttemptDiagnostic(label, $"Core threw: {ex.Message}", null, null, null));
                    continue;
                }

                RouteObstructionResult obstruction = FindObstructingWallForRouteDetailed(doc, wallRayCasting, fixture, segments, collectorWalls);
                RecordAttempt(diagnostics, label, segments, obstruction);

                // אותו עיקרון בדיוק כמו לפני התוספת: יציאה מיידית בהצלחה
                // הראשונה (לא ממשיכים "בשביל" האבחון) - למקטעים
                // RequiresManualEngineering (המקרה היחיד שבו האבחון בכלל
                // רלוונטי) שום ניסיון לא מצליח בהגדרה, אז הלולאה המלאה
                // ממילא רצה עד הסוף - אין כאן שינוי-ביצועים למקטעים תקינים.
                if (obstruction.WallId is null)
                {
                    return segments;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// מנסה לבנות מסלול-עוקף **בצד נתון** (<paramref name="useOppositeSide"/>)
    /// ולוודא שהוא בפועל לא חוצה קיר. מחזירה <c>null</c> (לא זורקת) אם
    /// הצד הזה לא ישים - בין אם כי <see cref="PipeRouteCalculator.CalculateDetour"/>
    /// עצמה זרקה (גיאומטריה לא-תקינה לצד הזה - למשל נקודת-הצטלבות
    /// "אחורה", או אורך-כולל שחורג מ-4.0 מ') ובין אם כי המסלול שהתקבל
    /// עדיין חוצה קיר בפועל. <c>null</c> כאן הוא איתות "נסה את הצד
    /// השני" - לא כשל-סופי (זה מטופל ב-<see cref="BuildRoute"/>).
    /// </summary>
    /// <param name="doc">המסמך הפעיל.</param>
    /// <param name="wallRayCasting">עוזר-הקרניים המשותף לבדיקת-חסימה.</param>
    /// <param name="fixture">האסלה.</param>
    /// <param name="collector">הקולטן.</param>
    /// <param name="wallSegment">הקיר החוסם (גיאומטריה טהורה).</param>
    /// <param name="useOppositeSide">מועבר כמו-שהוא ל-<see cref="PipeRouteCalculator.CalculateDetour"/>.</param>
    /// <param name="useWallDirectionAsReference">מועבר כמו-שהוא ל-<see cref="PipeRouteCalculator.CalculateDetour"/>.</param>
    /// <param name="diagnostics">
    /// אם ניתנה (לא <c>null</c>) - הניסיון הזה מתועד כ-<see cref="AttemptDiagnostic"/>
    /// משלו. ראו התיעוד המקביל ב-<see cref="TryBuildStaggeredDetour"/>.
    /// </param>
    /// <param name="collectorWalls">ראו התיעוד המקביל ב-<see cref="TryBuildStaggeredDetour"/> - הכלל ההנדסי שאושר (2026-09-02): חדירה מותרת לקירות שמכילים את הקולטן, במקטע האחרון בלבד.</param>
    private static IReadOnlyList<PipeSegment>? TryBuildDetour(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        CollectorPoint collector,
        WallEdgeSnapper.WallSegment wallSegment,
        bool useOppositeSide,
        bool useWallDirectionAsReference,
        List<AttemptDiagnostic>? diagnostics = null,
        CollectorWallPenetration? collectorWalls = null)
    {
        string label = string.Format(
            CultureInfo.InvariantCulture,
            "2-leg detour | side={0} | reference={1}",
            useOppositeSide ? "opposite" : "preferred",
            useWallDirectionAsReference ? "wall direction" : "fixture-collector line (D)");

        IReadOnlyList<PipeSegment> segments;
        try
        {
            segments = PipeRouteCalculator.CalculateDetour(
                fixture, collector, wallSegment, useOppositeSide, useWallDirectionAsReference);
        }
        catch (InvalidOperationException ex)
        {
            diagnostics?.Add(new AttemptDiagnostic(label, $"Core threw: {ex.Message}", null, null, null));
            return null;
        }

        RouteObstructionResult obstruction = FindObstructingWallForRouteDetailed(doc, wallRayCasting, fixture, segments, collectorWalls);
        RecordAttempt(diagnostics, label, segments, obstruction);

        return obstruction.WallId is null ? segments : null;
    }

    /// <summary>
    /// עוזר משותף ל-<see cref="TryBuildDetour"/>/<see cref="TryBuildStaggeredDetour"/> -
    /// בונה ומוסיפה <see cref="AttemptDiagnostic"/> מלא (waypoints +
    /// פרטי-חסימה אם רלוונטי) ל-<paramref name="diagnostics"/>, אם ניתנה.
    /// הופרד לכאן כדי ששני המקומות שקוראים לו לא יסטו זה מזה בטעות
    /// בפורמט התיעוד.
    /// </summary>
    private static void RecordAttempt(
        List<AttemptDiagnostic>? diagnostics,
        string label,
        IReadOnlyList<PipeSegment> segments,
        RouteObstructionResult obstruction)
    {
        if (diagnostics is null)
        {
            return;
        }

        string waypoints = DescribeWaypoints(segments);
        string? blockingDetail = obstruction.WallId is null ? null : DescribeBlockingDetail(segments, obstruction);
        diagnostics.Add(new AttemptDiagnostic(label, "Built OK (geometrically valid)", waypoints, obstruction.WallId is not null, blockingDetail));
    }

    /// <summary>
    /// מתארת, בקריאה אנושית, את נקודות-הביניים (waypoints) של מסלול
    /// שנבנה בהצלחה - **לא** כולל נקודת ההתחלה (אסלה) והסיום (קולטן)
    /// עצמן, רק את הצמתים-הפנימיים (סוף כל leg פרט לאחרון) - אלה
    /// הנקודות שבאמת נבחרו על ידי הבנייה הגיאומטרית.
    /// </summary>
    private static string DescribeWaypoints(IReadOnlyList<PipeSegment> segments)
    {
        var waypoints = new List<string>();
        for (int i = 0; i < segments.Count - 1; i++)
        {
            Point3D p = segments[i].EndPoint;
            waypoints.Add(string.Format(
                CultureInfo.InvariantCulture, "waypoint{0}=({1:F4}, {2:F4}, {3:F4})m", i + 1, p.X, p.Y, p.Z));
        }

        return waypoints.Count == 0 ? "(no intermediate waypoints)" : string.Join("; ", waypoints);
    }

    /// <summary>
    /// מתארת, בקריאה אנושית, **איפה בדיוק** מסלול שנבנה גיאומטרית תקין
    /// עדיין נתקל בקיר בפועל: איזה leg (מספר סידורי, מ-1), נקודת-הפגיעה
    /// המדויקת (מטרים), והמרחק ממנה גם מתחילת ה-leg וגם מסופו - כדי
    /// שאפשר יהיה לראות **כמה מה-leg הזה כבר "בתוך" הקיר**, לא רק
    /// "חסום/לא-חסום".
    /// </summary>
    private static string DescribeBlockingDetail(IReadOnlyList<PipeSegment> segments, RouteObstructionResult obstruction)
    {
        if (obstruction.WallId is null || obstruction.BlockedLegIndex is not int legIndex || obstruction.HitPoint is not XYZ hitPoint)
        {
            return "(no blocking detail)";
        }

        PipeSegment leg = segments[legIndex];
        Point3D hitPointMeters = RevitUnitConversion.ToCorePoint(hitPoint);
        double legLength = GeometryUtils.Distance2D(leg.StartPoint, leg.EndPoint);
        double distanceFromLegStart = GeometryUtils.Distance2D(leg.StartPoint, hitPointMeters);

        return string.Format(
            CultureInfo.InvariantCulture,
            "WallId={0}  leg #{1} of {2} (from ({3:F4},{4:F4})m to ({5:F4},{6:F4})m, length={7:F4}m)  " +
            "hit point=({8:F4},{9:F4},{10:F4})m  distance from leg start={11:F4}m  distance to leg end={12:F4}m",
            obstruction.WallId.Value,
            legIndex + 1,
            segments.Count,
            leg.StartPoint.X, leg.StartPoint.Y,
            leg.EndPoint.X, leg.EndPoint.Y,
            legLength,
            hitPointMeters.X, hitPointMeters.Y, hitPointMeters.Z,
            distanceFromLegStart,
            legLength - distanceFromLegStart);
    }

    /// <summary>
    /// יוצרת <see cref="DirectShape"/> **אחד** למסלול השלם, מורכב
    /// מ-solid אחד לכל מקטע-דרך (<see cref="BuildPipeLegSolid"/>) -
    /// מקטע יחיד למסלול ישר, שניים למסלול-עוקף, שני גדמים-לא-מחוברים
    /// למסלול "דורש פתרון ידני". תיוג (Comments/Mark/Name) לפי
    /// <paramref name="routeId"/> המשותף - לא לפי Id של מקטע בודד -
    /// כדי שמחיקת-מסלולים ישנים (<see cref="DeleteExistingPipes"/>)
    /// תתייחס לכל מסלול כאלמנט אחד.
    /// </summary>
    /// <param name="doc">המסמך הפעיל.</param>
    /// <param name="routeId">מזהה-המסלול המשותף לתיוג.</param>
    /// <param name="segments">מקטעי-הדרך (אחד/שניים/שלושה, או שני גדמים).</param>
    /// <param name="materialId">
    /// אם ניתן (לא null) - נצרב לתוך הגיאומטריה (כמו ה-Material של
    /// הקולטנים, שלב 6) ומוחל גם כ-<see cref="Autodesk.Revit.DB.OverrideGraphicSettings"/>
    /// על <paramref name="overrideView"/> (שכבה כפולה, אותו עיקרון).
    /// **גילוי מתוקן**: בעבר צינורות תקינים לא קיבלו שום Material -
    /// נוצרו לבנים, לא נראו בתצוגה רגילה. עכשיו **כל** קטגוריה מקבלת
    /// Material משלה (כחול-בינוני לתקינים, כתום-עמום לגדמי "דורש פתרון ידני") -
    /// ראו docs/step7.md.
    /// </param>
    /// <param name="overrideView">ה-View להחלת ה-Override הפר-view - נדרש רק אם <paramref name="materialId"/> ניתן.</param>
    /// <param name="orange">קובעת איזה צבע-Override להחיל (<c>true</c>=כתום, <c>false</c>=כחול-בינוני) - רלוונטי רק אם <paramref name="materialId"/> ניתן.</param>
    /// <param name="extendAtJoints">
    /// <c>true</c> (ברירת מחדל) - כל מקטע-דרך **פנימי** (לא הקצה הראשון/
    /// אחרון של המסלול) מוארך בחצי-רוחב-הצינור לכיוון השכן שלו בכל
    /// צומת משותף - ראו <see cref="BuildPipeLegSolid"/> להסבר המלא.
    /// <c>false</c> - בלי הארכה (לגדמי "דורש פתרון ידני", שמעולם לא
    /// אמורים לגעת זה בזה - זה בדיוק הנקודה שלהם).
    /// </param>
    private static DirectShape CreatePipeElement(
        Document doc,
        string routeId,
        IReadOnlyList<PipeSegment> segments,
        ElementId? materialId = null,
        View? overrideView = null,
        bool orange = false,
        bool extendAtJoints = true)
    {
        double halfWidth = HalfWidthFeet;
        var solids = new List<GeometryObject>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            double extendStart = (extendAtJoints && i > 0) ? halfWidth : 0.0;
            double extendEnd = (extendAtJoints && i < segments.Count - 1) ? halfWidth : 0.0;
            solids.Add(BuildPipeLegSolid(segments[i], materialId, extendStart, extendEnd));
        }

        DirectShape directShape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
        directShape.SetShape(solids);
        directShape.Name = $"{PipeElementNamePrefix}{routeId}";

        directShape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(routeId);
        directShape.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(routeId);

        if (materialId is not null && overrideView is not null)
        {
            Color overrideColor = orange ? ManualEngineeringColor : NormalPipeColor;
            overrideView.SetElementOverrides(directShape.Id, BuildColorOverrides(doc, overrideColor));
        }

        return directShape;
    }

    /// <summary>
    /// בונה <see cref="Solid"/> "צר וארוך" (תיבה מלבנית, לא גליל אמיתי -
    /// פישוט מכוון) למקטע-דרך יחיד. כיוון-ההוצאה (Extrusion) הוא
    /// **הכיוון המלא בתלת-ממד** של המקטע (יש לו גם מרכיב Z, בגלל
    /// השיפוע) - לכן צריך לבנות בסיס מאונך (<see cref="BuildPerpendicularBasis"/>)
    /// לכיוון הזה, לא להניח שהוא אנכי.
    /// </summary>
    /// <remarks>
    /// **תיקון "כתמים ירוקים עגולים בצמתים"**: כל leg הוא תיבה עם קצוות
    /// **חתוכים-בניצב** לציר-שלה-עצמה (לא "משופעים" להתאמה לשכן) - בצומת
    /// בין שני legs שנפגשים בזווית (לא 0°/180°, כלומר כל צומת של מסלול-
    /// עוקף/Y-מדורג), שני חתכים-ניצבים-שונים לא נפגשים בצורה חלקה: נוצר
    /// חסר (wedge) בצד החיצוני של הפנייה, וחפיפה דקה (sliver) בצד
    /// הפנימי - שהופיעה ויזואלית כ"כתם" בצומת. הפתרון: <paramref name="extendStartFeet"/>/<paramref name="extendEndFeet"/>
    /// מאריכים את ה-leg **לכיוון השכן** (לא לכיוון-הצומת-בלבד) בחצי-
    /// רוחב-הצינור - כך שהחפיפה בצומת הופכת גדולה ואחידה (מכסה את
    /// כל האזור, כמו מרפק אמיתי), במקום שוליים-דקים שנראים כארטיפקט.
    /// </remarks>
    private static Solid BuildPipeLegSolid(
        PipeSegment segment, ElementId? materialId = null, double extendStartFeet = 0.0, double extendEndFeet = 0.0)
    {
        XYZ rawStart = RevitUnitConversion.ToRevitPoint(segment.StartPoint);
        XYZ rawEnd = RevitUnitConversion.ToRevitPoint(segment.EndPoint);

        XYZ vector = rawEnd - rawStart;
        double rawLength = vector.GetLength();
        XYZ direction = vector.Normalize();

        XYZ start = rawStart - (direction * extendStartFeet);
        double length = rawLength + extendStartFeet + extendEndFeet;

        (XYZ right, XYZ up) = BuildPerpendicularBasis(direction);
        double halfWidth = HalfWidthFeet;

        XYZ p0 = start + (right * -halfWidth) + (up * -halfWidth);
        XYZ p1 = start + (right * halfWidth) + (up * -halfWidth);
        XYZ p2 = start + (right * halfWidth) + (up * halfWidth);
        XYZ p3 = start + (right * -halfWidth) + (up * halfWidth);

        var profile = new CurveLoop();
        profile.Append(Line.CreateBound(p0, p1));
        profile.Append(Line.CreateBound(p1, p2));
        profile.Append(Line.CreateBound(p2, p3));
        profile.Append(Line.CreateBound(p3, p0));

        var solidOptions = new SolidOptions(materialId ?? ElementId.InvalidElementId, ElementId.InvalidElementId);

        return GeometryCreationUtilities.CreateExtrusionGeometry(
            new List<CurveLoop> { profile }, direction, length, solidOptions);
    }

    /// <summary>
    /// בונה שני וקטורי-יחידה מאונכים ל-<paramref name="direction"/> (ולזה
    /// את זה) - מגדירים את המישור שבו נבנה חתך-הצינור המרובע. משתמשת
    /// ב-<see cref="Autodesk.Revit.DB.XYZ.BasisZ"/> כווקטור-ייחוס, חוץ
    /// אם <paramref name="direction"/> כמעט מקביל אליו (צינור כמעט-אנכי) -
    /// אז עוברת ל-<see cref="Autodesk.Revit.DB.XYZ.BasisX"/> כדי למנוע
    /// מכפלה-וקטורית מנוונת (קרובה לאפס) בין שני וקטורים מקבילים כמעט.
    /// </summary>
    private static (XYZ Right, XYZ Up) BuildPerpendicularBasis(XYZ direction)
    {
        XYZ referenceUp = Math.Abs(direction.DotProduct(XYZ.BasisZ)) > 0.999 ? XYZ.BasisX : XYZ.BasisZ;

        XYZ right = direction.CrossProduct(referenceUp).Normalize();
        XYZ up = right.CrossProduct(direction).Normalize();

        return (right, up);
    }

    /// <summary>
    /// **בדיקת-חסימה, לא פתרון**: משתמשת ב-<see cref="WallRayCasting.FindBlockingWall"/>
    /// (אותה טכניקה גנרית שכבר משמשת את <see cref="RevitModelReader"/>
    /// לבדיקת חסימה בין אסלה לאלמנט רטוב, שלב 4) כדי לבדוק אם קיר חוצה
    /// את הקו הישר של מקטע-דרך בודד.
    /// </summary>
    private static ElementId? FindObstructingWall(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        PipeSegment segment,
        CollectorWallPenetration? collectorWalls = null) =>
        FindObstructingWallDetailedSingle(doc, wallRayCasting, fixture, segment, collectorWalls)?.WallId;

    /// <summary>
    /// אותה לוגיקה בדיוק כמו <see cref="FindObstructingWall"/> (פתרון
    /// familyInstance/Host מ-<paramref name="fixture"/>, אותם כללי-
    /// החרגה), אבל מחזירה גם את נקודת-הפגיעה המדויקת ואת המרחק לאורכה -
    /// ראו <see cref="WallRayCasting.BlockingWallHit"/>. נדרש לדיווח-
    /// אבחון מפורט (<see cref="RecordAttempt"/>) - <see cref="FindObstructingWall"/>
    /// הוא עטיפה-דקה סביב המתודה הזו, לא שכפול-קוד נפרד.
    /// </summary>
    private static WallRayCasting.BlockingWallHit? FindObstructingWallDetailedSingle(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        PipeSegment segment,
        CollectorWallPenetration? collectorWalls = null)
    {
        if (!long.TryParse(fixture.Id, out long fixtureIdValue)
            || doc.GetElement(new ElementId(fixtureIdValue)) is not FamilyInstance familyInstance)
        {
            // לא אמור לקרות - fixture.Id תמיד ElementId תקין של FamilyInstance
            // קיים (הגיע מ-RevitModelReader). נפילה בטוחה: לא נדע לבדוק
            // חסימה, אבל זו לא סיבה לעצור את כל הריצה - ראו "OK" בדוח.
            return null;
        }

        XYZ from = RevitUnitConversion.ToRevitPoint(segment.StartPoint);
        XYZ to = RevitUnitConversion.ToRevitPoint(segment.EndPoint);

        // הכלל ההנדסי שאושר (2026-09-02): route בדרך אל הקולטן רשאי לחדור
        // לתוך הקיר/ים שמכילים את הקולטן - אבל **רק** למקטע שמסתיים בקולטן
        // עצמו (segment.EndPoint == מיקום הקולטן), ורק עד למרחק המותר
        // מסוף המקטע. מקטע-ביניים (שמסתיים ב-waypoint) וכל קיר אחר -
        // obstruction רגיל, ללא שינוי.
        IReadOnlySet<ElementId>? penetrableWallIds = null;
        double penetrableWithinEndDistanceFeet = 0.0;
        if (collectorWalls is CollectorWallPenetration cw
            && GeometryUtils.Distance2D(segment.EndPoint, cw.CollectorLocation) <= 1e-6)
        {
            penetrableWallIds = cw.WallIds;
            penetrableWithinEndDistanceFeet = cw.AllowanceFeet;
        }

        return wallRayCasting.FindBlockingWallDetailed(
            from, to, familyInstance.LevelId, familyInstance.Host?.Id, null,
            penetrableWallIds, penetrableWithinEndDistanceFeet);
    }

    /// <summary>
    /// בודקת חסימה על **כל** מקטעי מסלול (אחד אם ישר, שניים/שלושה אם
    /// עוקף) - לדיווח **סופי** בדוח, אחרי שכל המסלול כבר נבנה. מחזירה
    /// את הקיר הראשון שנמצא חוסם (אם בכלל) - כולל, במקרה של מסלול-
    /// עוקף, בדיקה שהעקיפה בעצמה לא "ברחה" לתוך קיר אחר.
    /// </summary>
    private static ElementId? FindObstructingWallForRoute(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        IReadOnlyList<PipeSegment> routeSegments) =>
        FindObstructingWallForRouteDetailed(doc, wallRayCasting, fixture, routeSegments).WallId;

    /// <summary>
    /// תוצאת בדיקת-חסימה **מפורטת** על מסלול שלם - לא רק **איזה** קיר
    /// (<see cref="WallId"/>, כמו <see cref="FindObstructingWallForRoute"/>),
    /// אלא גם **איזה leg** (<see cref="BlockedLegIndex"/>, אינדקס מ-0)
    /// ו**איפה בדיוק** (<see cref="HitPoint"/>/<see cref="ProximityFeet"/>) -
    /// לשימוש בדיווח-אבחון (<see cref="RecordAttempt"/>) בלבד. כל
    /// השדות <c>null</c> אם המסלול לא חסום בכלל.
    /// </summary>
    private readonly record struct RouteObstructionResult(
        ElementId? WallId,
        int? BlockedLegIndex,
        XYZ? HitPoint,
        double? ProximityFeet);

    /// <summary>
    /// אותה לוגיקה בדיוק כמו <see cref="FindObstructingWallForRoute"/>,
    /// אבל מחזירה פרטים מלאים - ראו <see cref="RouteObstructionResult"/>.
    /// </summary>
    private static RouteObstructionResult FindObstructingWallForRouteDetailed(
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        IReadOnlyList<PipeSegment> routeSegments,
        CollectorWallPenetration? collectorWalls = null)
    {
        for (int i = 0; i < routeSegments.Count; i++)
        {
            WallRayCasting.BlockingWallHit? hit = FindObstructingWallDetailedSingle(doc, wallRayCasting, fixture, routeSegments[i], collectorWalls);
            if (hit is not null)
            {
                return new RouteObstructionResult(hit.Value.WallId, i, hit.Value.HitPoint, hit.Value.ProximityFeet);
            }
        }

        return new RouteObstructionResult(null, null, null, null);
    }

    /// <summary>
    /// שולפת את גיאומטריית הקיר (כ-<see cref="WallEdgeSnapper.WallSegment"/>,
    /// ייצוג טהור-Core, לא <see cref="Autodesk.Revit.DB.Wall"/>) - נדרש
    /// כדי ש-<see cref="PipeRouteCalculator.CalculateDetour"/> (Core,
    /// בלי RevitAPI) תוכל לבחור צד להיסט ה-waypoint. מחזירה
    /// <c>null</c> אם הקיר אינו קיים, או שה-<c>LocationCurve</c> שלו
    /// אינו <see cref="Line"/> (קיר מעוקל) - אותה מגבלה בדיוק כמו
    /// <c>FindNearbyWallsByRayCasting</c> בשלב 6.
    /// </summary>
    private static WallEdgeSnapper.WallSegment? GetWallSegment(Document doc, ElementId wallId)
    {
        if (doc.GetElement(wallId) is not Wall wall
            || wall.Location is not LocationCurve locationCurve
            || locationCurve.Curve is not Line line)
        {
            return null;
        }

        return new WallEdgeSnapper.WallSegment(
            wallId.Value.ToString(CultureInfo.InvariantCulture),
            RevitUnitConversion.ToCorePoint(line.GetEndPoint(0)),
            RevitUnitConversion.ToCorePoint(line.GetEndPoint(1)));
    }

    /// <summary>
    /// תוצאת <see cref="FindCollectorWallPenetration"/>: מזהי הקירות
    /// שגוף-הקיר שלהם מכיל (או כמעט מכיל) את מיקום הקולטן, מיקום הקולטן
    /// עצמו (לזיהוי "המקטע שמסתיים בקולטן"), והמרחק המרבי (feet) שמותר
    /// ל-route לחדור לתוכם בדרך אל הקולטן. ראו
    /// <see cref="CollectorWallPenetrationWidthFactor"/>.
    /// </summary>
    private readonly record struct CollectorWallPenetration(
        IReadOnlySet<ElementId> WallIds,
        Point3D CollectorLocation,
        double AllowanceFeet);

    /// <summary>
    /// מאתרת את הקירות (על ה-Level של האסלה) שגוף-הקיר שלהם מכיל את
    /// מיקום הקולטן <b>וגם</b> ש-<see cref="Config.WallPenetrationPolicy.IsPenetrationAllowed"/>
    /// מתיר לחדור אליהם (הכלל העסקי: קיר-בטון אסור, כל קיר אחר מותר;
    /// זיהוי "בטון" לפי קובץ-ההגדרות המשרדי, מפתח <c>"Concrete wall"</c>) -
    /// קיר <c>Basic</c> שקו-המיקום שלו עובר במרחק
    /// <c>≤ חצי-עובי + <see cref="CollectorWallContainmentToleranceMeters"/></c>
    /// ממיקום הקולטן, ושסוגו מותר לחדירה. לפי הכלל ההנדסי שאושר
    /// (2026-09-02), route בדרך אל הקולטן רשאי לחדור לתוך הקירות האלה
    /// עד לקולטן (לא מעבר, ולא דרך קירות אחרים - ראו
    /// <see cref="CollectorWallPenetrationWidthFactor"/>). מחזירה
    /// <c>null</c> אם אף קיר לא מכיל-ומותר - ואז בדיקת-החסימה זהה
    /// לחלוטין להתנהגות שלפני התוספת (כולל: קיר-בטון שמכיל את הקולטן
    /// יגרום כרגיל למקטע להיות <c>RequiresManualEngineering</c>).
    /// **שער-ההרשאה הוא תנאי נוסף בלבד - אינו משנה את הכלל הגיאומטרי.**
    /// </summary>
    private static CollectorWallPenetration? FindCollectorWallPenetration(
        Document doc, ToiletFixture fixture, CollectorPoint collector)
    {
        if (!long.TryParse(fixture.Id, out long fixtureIdValue)
            || doc.GetElement(new ElementId(fixtureIdValue)) is not FamilyInstance familyInstance)
        {
            return null;
        }

        XYZ collectorPoint = RevitUnitConversion.ToRevitPoint(collector.Location);
        double containmentToleranceFeet = UnitUtils.ConvertToInternalUnits(
            CollectorWallContainmentToleranceMeters, UnitTypeId.Meters);

        var wallsOnLevelFilter = new LogicalAndFilter(
            new ElementCategoryFilter(BuiltInCategory.OST_Walls),
            new ElementLevelFilter(familyInstance.LevelId));

        var wallIds = new HashSet<ElementId>();
        double maxWidthFeet = 0.0;

        foreach (Wall wall in new FilteredElementCollector(doc)
            .WherePasses(wallsOnLevelFilter)
            .WhereElementIsNotElementType()
            .OfType<Wall>())
        {
            if (wall.WallType?.Kind is not WallKind.Basic
                || wall.Location is not LocationCurve locationCurve
                || locationCurve.Curve is not Line line)
            {
                continue;
            }

            IntersectionResult? projection = line.Project(collectorPoint);
            if (projection is null)
            {
                continue;
            }

            // מרחק **דו-ממדי** (X,Y) בלבד - כמו כל שאר לוגיקת-הניתוב
            // (GeometryUtils.Distance2D, WallEdgeSnapper) - כדי שהפרש-Z
            // אפשרי בין קו-מיקום-הקיר למיקום-האסלה לא יפסול קיר בטעות.
            XYZ foot = projection.XYZPoint;
            double dx = foot.X - collectorPoint.X;
            double dy = foot.Y - collectorPoint.Y;
            double distanceFeet = Math.Sqrt((dx * dx) + (dy * dy));
            if (distanceFeet <= (wall.Width / 2.0) + containmentToleranceFeet)
            {
                // תנאי **נוסף** (AND) על הכלל הגיאומטרי - לא מחליף אותו:
                // רק אם WallPenetrationPolicy מתיר (הכלל העסקי: קיר-בטון
                // אסור, כל קיר אחר מותר; "בטון" לפי קובץ-ההגדרות המשרדי,
                // מפתח "Concrete wall"). קיר-בטון, גם אם הוא מכיל את
                // הקולטן, **אינו** מתווסף כאן → נשאר obstruction רגיל →
                // detour → Manual Engineering, בדיוק כמו קיר שאינו מכיל
                // את הקולטן. ראו Config/WallPenetrationPolicy.cs,
                // Config/OfficeConfig.cs, docs/pipe-rca-chain.md חלק י"ג.
                if (!WallPenetrationPolicy.IsPenetrationAllowed(wall, out _))
                {
                    continue;
                }

                wallIds.Add(wall.Id);
                maxWidthFeet = Math.Max(maxWidthFeet, wall.Width);
            }
        }

        if (wallIds.Count == 0)
        {
            return null;
        }

        return new CollectorWallPenetration(
            wallIds, collector.Location, maxWidthFeet * CollectorWallPenetrationWidthFactor);
    }

    /// <summary>
    /// יוצרת (או מוצאת-ואז-מוחקת, אותו עיקרון בדיוק כמו
    /// <c>CollectorPlacementService.GetCollectorMaterialId</c> בשלב 6 -
    /// לעולם לא שימוש-חוזר שקט) Material בשם/צבע נתונים - לוגיקה
    /// משותפת ל-<see cref="GetManualEngineeringMaterialId"/> ול-
    /// <see cref="GetNormalPipeMaterialId"/>.
    /// </summary>
    private static ElementId GetOrCreateFreshMaterial(Document doc, string materialName, Color color)
    {
        Material? existing = new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(m => m.Name == materialName);

        if (existing is not null)
        {
            doc.Delete(existing.Id);
        }

        ElementId materialId = Material.Create(doc, materialName);
        if (doc.GetElement(materialId) is Material material)
        {
            material.Color = color;

            // אותה סיבה בדיוק כמו הקולטנים (שלב 6): בלי זה, Shaded View
            // עלול להציג Render Appearance במקום צבע ה-Shading שקבענו.
            material.UseRenderAppearanceForShading = false;
        }

        return materialId;
    }

    /// <summary>
    /// Material כתום-עמום ייחודי לגדמי "דורש פתרון ידני" - נקראת **פעם
    /// אחת בלבד** להרצה (ראו ה-<c>??=</c> ב-<see cref="Execute"/>), לא
    /// לכל גדם בנפרד.
    /// </summary>
    private static ElementId GetManualEngineeringMaterialId(Document doc) =>
        GetOrCreateFreshMaterial(doc, ManualEngineeringMaterialName, ManualEngineeringColor);

    /// <summary>
    /// Material כחול-בינוני ייחודי לצינורות **תקינים** (ישר/עוקף/Y-מדורג
    /// שהצליחו) - נקראת **פעם אחת בלבד** להרצה. ראו
    /// <see cref="NormalPipeMaterialName"/> לגבי הגילוי שהוביל לתיקון
    /// הזה (צינורות תקינים לא קיבלו שום Material קודם, ולכן נראו לבנים).
    /// </summary>
    private static ElementId GetNormalPipeMaterialId(Document doc) =>
        GetOrCreateFreshMaterial(doc, NormalPipeMaterialName, NormalPipeColor);

    /// <summary>
    /// עובי-קו בינוני (מתוך טווח Revit 1-16) - ראו שימוש ב-
    /// <see cref="BuildColorOverrides"/>. תוספת **מעבר לצבע** לבעיית-
    /// הנראות החוזרת: קו עבה-יחסית בולט בעין בתוכנית-קומה בזום רגיל,
    /// בלי "לבלוע" את שאר פרטי-התוכנית. עודכן מ-16 (מקסימלי) ל-5
    /// בעקבות משוב על מראה "debug-view" גס מדי, ואז **מ-5 ל-7** בעקבות
    /// משוב נוסף שמקטעים קצרים (כ-0.35-0.51 מ') עדיין לא נראים
    /// בקנה-מידה רגיל גם אחרי הגדלת-קוטר-התצוגה של הקולטן. **חל על שתי
    /// הקטגוריות בו-זמנית** (תקינים וגם גדמי "דורש פתרון ידני") - זו
    /// אותה קבוע יחיד ששתיהן חולקות דרך <see cref="BuildColorOverrides"/>,
    /// לא הבדל-בין-קטגוריות; שכבה **זולה וחסרת-סיכון** (משפיעה רק על
    /// האלמנטים המתויגים שלנו, דרך Override פר-אלמנט, לא על Object
    /// Styles של כל הקטגוריה, ולא על הקוטר ההנדסי האמיתי - 110 מ"מ -
    /// שממשיך לשמש ללא שינוי בכל חישוב/דוח).
    /// </summary>
    private const int BoldLineWeight = 7;

    /// <summary>
    /// בונה <see cref="Autodesk.Revit.DB.OverrideGraphicSettings"/> בצבע
    /// נתון - שכבה שנייה, פר-View, בנוסף ל-Material (אותו עיקרון בדיוק
    /// כמו הקולטנים בשלב 6 - ראו
    /// <c>CollectorPlacementService.BuildCollectorOverrides</c>). משותפת לשתי
    /// הקטגוריות (כחול-בינוני לתקינים, כתום-עמום לגדמי "דורש פתרון ידני") - רק
    /// הצבע משתנה. כוללת גם עובי-קו בינוני (<see cref="BoldLineWeight"/>) -
    /// תוספת-נראות מעבר לצבע בלבד.
    /// </summary>
    private static OverrideGraphicSettings BuildColorOverrides(Document doc, Color color)
    {
        var ogs = new OverrideGraphicSettings();

        ogs.SetProjectionLineColor(color);
        ogs.SetCutLineColor(color);
        ogs.SetSurfaceForegroundPatternColor(color);
        ogs.SetCutForegroundPatternColor(color);
        ogs.SetProjectionLineWeight(BoldLineWeight);
        ogs.SetCutLineWeight(BoldLineWeight);

        ElementId? solidFillPatternId = FindSolidFillPatternId(doc);
        if (solidFillPatternId is not null)
        {
            ogs.SetSurfaceForegroundPatternId(solidFillPatternId);
            ogs.SetSurfaceForegroundPatternVisible(true);
            ogs.SetCutForegroundPatternId(solidFillPatternId);
            ogs.SetCutForegroundPatternVisible(true);
        }

        return ogs;
    }

    /// <summary>מוצאת את ה-ElementId של תבנית "Solid fill" הראשונה שנמצאת במסמך - בלי caching (מספר קריאות זעיר, פקודה אחת-פעמית).</summary>
    private static ElementId? FindSolidFillPatternId(Document doc)
    {
        FillPatternElement? solidFill = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);

        return solidFill?.Id;
    }

    /// <summary>
    /// מוחקת מהמודל את כל אלמנטי-המסלול שנוצרו בהרצות קודמות - אותו
    /// עיקרון בדיוק כמו <see cref="CollectorPlacementService.DeleteExistingCollectors"/>
    /// (זיהוי לפי קטגוריית Generic Model + Comments/Mark/Name שמתחיל
    /// ב-<see cref="PipeRouteCalculator.PipeIdPrefix"/> - קידומת **שונה**
    /// מ-<see cref="CollectorLocator.CollectorIdPrefix"/>, כך שהמחיקה
    /// הזו לא נוגעת בקולטנים, ולהפך).
    /// </summary>
    private static int DeleteExistingPipes(Document doc)
    {
        List<ElementId> existingPipeIds = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .Where(IsExistingPipe)
            .Select(element => element.Id)
            .ToList();

        if (existingPipeIds.Count > 0)
        {
            doc.Delete(existingPipeIds);
        }

        return existingPipeIds.Count;
    }

    private static bool IsExistingPipe(Element element)
    {
        string? comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
        string? mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();

        return (comments?.StartsWith(PipeRouteCalculator.PipeIdPrefix, StringComparison.Ordinal) ?? false)
            || (mark?.StartsWith(PipeRouteCalculator.PipeIdPrefix, StringComparison.Ordinal) ?? false)
            || element.Name.StartsWith(PipeElementNamePrefix, StringComparison.Ordinal);
    }

    /// <summary>זווית-החיבור (מעלות) בין שני מקטעי-מסלול עוקבים - אותו חישוב בדיוק כמו ב-<c>PipeRouteCalculator.CalculateDetour</c>, לצורך דיווח בלבד.</summary>
    private static double ComputeAngleBetweenLegsDegrees(PipeSegment leg1, PipeSegment leg2)
    {
        double leg1DirX = leg1.EndPoint.X - leg1.StartPoint.X;
        double leg1DirY = leg1.EndPoint.Y - leg1.StartPoint.Y;
        double leg2DirX = leg2.EndPoint.X - leg2.StartPoint.X;
        double leg2DirY = leg2.EndPoint.Y - leg2.StartPoint.Y;

        double leg1Length = Math.Sqrt((leg1DirX * leg1DirX) + (leg1DirY * leg1DirY));
        double leg2Length = Math.Sqrt((leg2DirX * leg2DirX) + (leg2DirY * leg2DirY));

        double cosAngle = ((leg1DirX * leg2DirX) + (leg1DirY * leg2DirY)) / (leg1Length * leg2Length);
        return Math.Acos(Math.Clamp(cosAngle, -1.0, 1.0)) * (180.0 / Math.PI);
    }

    /// <summary>
    /// קוראת מ-<paramref name="elementId"/> **בפועל** (לא מניחה) אילו
    /// Material-ים מוחלים עליו, דרך <c>Element.GetMaterialIds</c> -
    /// אותה טכניקה בדיוק שכבר שימשה לחקירת "דליפת" הצבע האדום
    /// לכיורים קודם בפרויקט. נוצרה כי המשתמשת ביקשה במפורש הוכחה,
    /// לא הנחה, שהצבע-בפועל תואם למה שהקוד "אמור" להחיל.
    /// </summary>
    private static string DescribeActualMaterials(Document doc, ElementId elementId)
    {
        Element? element = doc.GetElement(elementId);
        if (element is null)
        {
            return "(element not found)";
        }

        ICollection<ElementId> materialIds;
        try
        {
            materialIds = element.GetMaterialIds(false);
        }
        catch (Exception)
        {
            return "(GetMaterialIds not supported for this element)";
        }

        if (materialIds.Count == 0)
        {
            return "(none - element has no Material at all)";
        }

        List<string> names = materialIds
            .Select(id => (doc.GetElement(id) as Material)?.Name ?? $"(unknown, ElementId={id.Value})")
            .ToList();

        return string.Join(", ", names);
    }

    /// <summary>
    /// כותבת את שורות-הדוח למקטע <c>RequiresManualEngineering</c> -
    /// **נפרד** מהבלוק הרגיל (לא בלוק-ולידציה-שיפוע/זווית, כי הגדמים
    /// אינם מקטעי-צנרת הנדסיים אמיתיים, רק סמני-אבחון) - מציג את שני
    /// אורכי-הגדם וסטטוס "דורש פתרון ידני" בבירור.
    /// </summary>
    private static void AppendManualEngineeringPipeReport(StringBuilder sb, Document doc, DrawnPipe pipe)
    {
        string obstructionStatus = pipe.BlockingWallId is ElementId wallId
            ? $"MANUAL ENGINEERING REQUIRED: WallId={wallId.Value} (2-leg detour + staggered-Y both tried, all sides/lengths - still blocked, see docs/step7.md)"
            : "MANUAL ENGINEERING REQUIRED (blocking wall id unavailable)";

        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  Pipe RouteId={0}  RevitElementId={1}  Kind=MANUAL STUBS (2 disconnected markers, orange, {2:F0}cm each - not a real pipe)",
            pipe.RouteId, pipe.RevitElementId.Value, ManualEngineeringStubLengthMeters * 100.0));
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "    FixtureElementId={0}  CollectorId={1}  DiameterMm={2:F0}  obstruction={3}",
            pipe.FixtureId, pipe.CollectorId, PipeRouteCalculator.PipeDiameterMm, obstructionStatus));
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "    Actual Material(s) on this element (verified via GetMaterialIds, not assumed): {0}",
            DescribeActualMaterials(doc, pipe.RevitElementId)));

        foreach (PipeSegment stub in pipe.Segments)
        {
            double stubLength = GeometryUtils.Distance2D(stub.StartPoint, stub.EndPoint);
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    Stub Id={0}  Length={1:F4}m  (diagnostic marker only - no slope/angle validation applies)",
                stub.Id,
                stubLength));
        }
    }

    /// <summary>
    /// דוח-אבחון **נפרד** מהדוח הרגיל (<see cref="BuildReport"/>) -
    /// נכתב (ונפתח) רק אם יש לפחות מקטע אחד <c>RequiresManualEngineering</c>.
    /// מיועד ספציפית להעברה למהנדס-אינסטלציה **חיצוני** שאין לו גישה
    /// לקוד או למודל ב-Revit: לכל מקטע כזה (רק אלה, לא כל המסלולים) -
    /// המיקום המדויק של האסלה/הקולטן, גיאומטריית הקיר החוסם (נקודות-
    /// קצה + זווית ביחס לקו אסלה-קולטן), ו**כל** ניסיון-עקיפה שנוסה
    /// (<see cref="AttemptDiagnostic"/>, עד 28 - ראו <see cref="BuildRoute"/>)
    /// עם התוצאה המדויקת שלו - לא רק "נכשל" אלא **למה** ו**איפה בדיוק**.
    /// </summary>
    private static string BuildManualEngineeringDiagnosticReport(
        Document doc,
        List<Apartment> apartments,
        Dictionary<string, List<DrawnPipe>> drawnByApartmentId)
    {
        List<DrawnPipe> allManualPipes = drawnByApartmentId.Values
            .SelectMany(list => list)
            .Where(p => p.RequiresManualEngineering)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Manual Engineering Required: Full Diagnostic Report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("For external plumbing engineer review - includes every attempted route and its exact outcome, not just a final PASS/FAIL.");
        sb.AppendLine($"Total segments requiring manual engineering: {allManualPipes.Count}");

        int index = 0;
        foreach (Apartment apartment in apartments.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!drawnByApartmentId.TryGetValue(apartment.Id, out List<DrawnPipe>? apartmentPipes))
            {
                continue;
            }

            foreach (DrawnPipe pipe in apartmentPipes.Where(p => p.RequiresManualEngineering))
            {
                index++;
                sb.AppendLine();
                sb.AppendLine(new string('=', 90));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "SEGMENT {0}/{1}   RouteId={2}   Apartment={3} (Floor {4})",
                    index, allManualPipes.Count, pipe.RouteId, apartment.Id, apartment.FloorNumber));
                sb.AppendLine(new string('=', 90));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "FixtureElementId={0}  CollectorId={1}  BlockingWallId={2}",
                    pipe.FixtureId,
                    pipe.CollectorId,
                    pipe.BlockingWallId is ElementId wallId ? wallId.Value.ToString(CultureInfo.InvariantCulture) : "(unavailable)"));

                // pipe.Segments הם שני הגדמים - stub[0] מתחיל **בדיוק** במיקום
                // האסלה, stub[1] מתחיל **בדיוק** במיקום הקולטן הסופי (אחרי
                // wall-snap) - ראו BuildManualEngineeringStubs. לא נתון נפרד
                // ששמור על DrawnPipe - נגזר ישירות מהגיאומטריה שכבר צוירה.
                Point3D fixtureLocation = pipe.Segments[0].StartPoint;
                Point3D collectorLocation = pipe.Segments[1].StartPoint;
                double horizontalDistance = GeometryUtils.Distance2D(fixtureLocation, collectorLocation);

                sb.AppendLine();
                sb.AppendLine("--- Geometry (all coordinates in meters, from the actual Revit model) ---");
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Fixture location: ({0:F4}, {1:F4}, {2:F4})",
                    fixtureLocation.X, fixtureLocation.Y, fixtureLocation.Z));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Collector location (final, wall-snapped): ({0:F4}, {1:F4}, {2:F4})",
                    collectorLocation.X, collectorLocation.Y, collectorLocation.Z));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Horizontal (2D) distance, fixture to collector: {0:F4}m",
                    horizontalDistance));

                if (pipe.BlockingWallGeometry is WallEdgeSnapper.WallSegment wall)
                {
                    double angleDegrees = ComputeAngleBetweenLinesDegrees(
                        fixtureLocation.X, fixtureLocation.Y, collectorLocation.X, collectorLocation.Y,
                        wall.Start.X, wall.Start.Y, wall.End.X, wall.End.Y);
                    double distanceCollectorToNearWallEnd = Math.Min(
                        GeometryUtils.Distance2D(collectorLocation, wall.Start),
                        GeometryUtils.Distance2D(collectorLocation, wall.End));

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "Blocking wall segment: ({0:F4}, {1:F4}) to ({2:F4}, {3:F4})",
                        wall.Start.X, wall.Start.Y, wall.End.X, wall.End.Y));
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "Angle between the blocking wall and the straight fixture-collector line: {0:F2} degrees " +
                        "(0=parallel to the route, 90=perpendicular to it)",
                        angleDegrees));
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "Distance from the collector to the nearer end of this wall (corner proximity): {0:F4}m",
                        distanceCollectorToNearWallEnd));
                }
                else
                {
                    sb.AppendLine("Blocking wall geometry not available (unexpected - please report this).");
                }

                sb.AppendLine();
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture, "--- All {0} attempts tried, in order ---", pipe.Diagnostics.Count));

                int attemptNumber = 0;
                foreach (AttemptDiagnostic attempt in pipe.Diagnostics)
                {
                    attemptNumber++;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  [{0}] {1}", attemptNumber, attempt.Label));
                    sb.AppendLine($"      Result: {attempt.CoreOutcome}");

                    if (attempt.WaypointsMeters is not null)
                    {
                        sb.AppendLine($"      Waypoints: {attempt.WaypointsMeters}");
                    }

                    if (attempt.StillBlockedAfterBuild is bool stillBlocked)
                    {
                        sb.AppendLine($"      Still blocked after build: {(stillBlocked ? "YES" : "no")}");
                    }

                    if (attempt.BlockingDetail is not null)
                    {
                        sb.AppendLine($"      Blocking detail: {attempt.BlockingDetail}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// זווית (0°-90°, לא-מכוונת) בין שני **קווים** (לא וקטורים מכוונים -
    /// בניגוד ל-<see cref="ComputeAngleBetweenLegsDegrees"/> שמודדת זווית-
    /// חיבור מכוונת בין שני legs עוקבים) - משמשת כאן למדוד את הזווית
    /// בין הקיר החוסם לקו הישר אסלה-קולטן, שאין לה כיוון-משמעותי
    /// (קיר יכול "להצביע" לשני הכיוונים ההפוכים של אותו קו פיזי).
    /// <c>Math.Abs</c> על ה-dot product הוא מה שהופך זווית-כיוון (0-180°)
    /// לזווית-בין-קווים (0-90°).
    /// </summary>
    private static double ComputeAngleBetweenLinesDegrees(
        double aX1, double aY1, double aX2, double aY2,
        double bX1, double bY1, double bX2, double bY2)
    {
        double aDx = aX2 - aX1;
        double aDy = aY2 - aY1;
        double bDx = bX2 - bX1;
        double bDy = bY2 - bY1;

        double aLength = Math.Sqrt((aDx * aDx) + (aDy * aDy));
        double bLength = Math.Sqrt((bDx * bDx) + (bDy * bDy));
        if (aLength < 1e-9 || bLength < 1e-9)
        {
            return double.NaN;
        }

        double dot = ((aDx * bDx) + (aDy * bDy)) / (aLength * bLength);
        double angleRadians = Math.Acos(Math.Clamp(Math.Abs(dot), 0.0, 1.0));
        return angleRadians * (180.0 / Math.PI);
    }

    private static string BuildReport(
        Document doc,
        List<Apartment> apartments,
        Dictionary<string, List<DrawnPipe>> drawnByApartmentId,
        int deletedPipeCount,
        string scopeDescription,
        List<string> readerWarnings)
    {
        List<DrawnPipe> allPipes = drawnByApartmentId.Values.SelectMany(list => list).ToList();
        int detourCount = allPipes.Count(p => p.IsDetour);
        int manualRequiredCount = allPipes.Count(p => p.RequiresManualEngineering);
        int obstructedCount = allPipes.Count(p => p.BlockingWallId is not null);

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Pipes Drawn in Revit ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Scope: {scopeDescription}");
        sb.AppendLine($"Deleted {deletedPipeCount} existing pipe route(s) from previous runs before creating new ones.");
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Total pipe routes created: {0} (detour routes: {1}, manual engineering required: {2}, obstructed: {3}).",
            allPipes.Count, detourCount, manualRequiredCount, obstructedCount));
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
            List<DrawnPipe> drawn = drawnByApartmentId[apartment.Id];

            sb.AppendLine();
            sb.AppendLine($"--- Apartment '{apartment.Id}' (Floor {apartment.FloorNumber}) - {drawn.Count} pipe route(s) ---");

            foreach (DrawnPipe pipe in drawn)
            {
                if (pipe.RequiresManualEngineering)
                {
                    AppendManualEngineeringPipeReport(sb, doc, pipe);
                    continue;
                }

                string routeKind = pipe.Segments.Count switch
                {
                    3 => $"STAGGERED Y (3 legs, 2x~{PipeRouteCalculator.StaggeredDetourBendAngleDegrees:F1} deg bends, law 9 alt.)",
                    2 => $"DETOUR (2 legs, ~{PipeRouteCalculator.DetourHalfBendAngleDegrees * 2:F1} deg bend, law 9)",
                    _ => "STRAIGHT (1 leg)",
                };
                string obstructionStatus = pipe.BlockingWallId is ElementId wallId
                    ? (pipe.DetourUnavailable
                        ? $"OBSTRUCTED: WallId={wallId.Value} (detour NOT attempted - blocking wall is not a straight Line)"
                        : $"STILL OBSTRUCTED after detour attempt: WallId={wallId.Value}")
                    : "OK";

                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Pipe RouteId={0}  RevitElementId={1}  Kind={2}",
                    pipe.RouteId, pipe.RevitElementId.Value, routeKind));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    FixtureElementId={0}  CollectorId={1}  DiameterMm={2:F0}  obstruction={3}",
                    pipe.FixtureId, pipe.CollectorId, PipeRouteCalculator.PipeDiameterMm, obstructionStatus));
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    Actual Material(s) on this element (verified via GetMaterialIds, not assumed): {0}",
                    DescribeActualMaterials(doc, pipe.RevitElementId)));

                for (int i = 0; i < pipe.Segments.Count; i++)
                {
                    PipeSegment segment = pipe.Segments[i];
                    double horizontalLength = GeometryUtils.Distance2D(segment.StartPoint, segment.EndPoint);
                    double zDrop = segment.StartPoint.Z - segment.EndPoint.Z;
                    bool slopeValid = segment.SlopePercent >= PipeRouteCalculator.MinSlopePercent
                        && segment.SlopePercent <= PipeRouteCalculator.MaxSlopePercent;

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "    Leg {0}: Id={1}  Horizontal length={2:F4}m  Z-drop={3:F4}m  computed slope={4:F4}%  validation={5} (range {6:F1}%-{7:F1}%)",
                        i + 1,
                        segment.Id,
                        horizontalLength,
                        zDrop,
                        segment.SlopePercent,
                        slopeValid ? "PASS" : "FAIL",
                        PipeRouteCalculator.MinSlopePercent,
                        PipeRouteCalculator.MaxSlopePercent));
                }

                if (pipe.Segments.Count == 2)
                {
                    double angleDegrees = ComputeAngleBetweenLegsDegrees(pipe.Segments[0], pipe.Segments[1]);
                    bool angleValid = angleDegrees >= PipeRouteCalculator.MinDetourAngleDegrees
                        && angleDegrees <= PipeRouteCalculator.MaxDetourAngleDegrees;

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "    Bend angle between legs={0:F2} deg  validation={1} (range {2:F0}-{3:F0} deg, ~{4:F0} deg per law 9)",
                        angleDegrees,
                        angleValid ? "PASS" : "FAIL",
                        PipeRouteCalculator.MinDetourAngleDegrees,
                        PipeRouteCalculator.MaxDetourAngleDegrees,
                        PipeRouteCalculator.DetourHalfBendAngleDegrees * 2));
                }
                else if (pipe.Segments.Count == 3)
                {
                    double bend1Degrees = ComputeAngleBetweenLegsDegrees(pipe.Segments[0], pipe.Segments[1]);
                    double bend2Degrees = ComputeAngleBetweenLegsDegrees(pipe.Segments[1], pipe.Segments[2]);

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "    Bend 1 (leg1->crossover)={0:F2} deg  Bend 2 (crossover->leg3)={1:F2} deg  (target ~{2:F1} deg each per law 9 alt.)",
                        bend1Degrees,
                        bend2Degrees,
                        PipeRouteCalculator.StaggeredDetourBendAngleDegrees));
                }
            }
        }

        return sb.ToString();
    }
}