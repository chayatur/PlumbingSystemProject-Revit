using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Revit.Config;
using PlumbingSystem.Revit.Mep;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת-ניסוי **זמנית** (PIPE Step 4): מוכיחה בצורה מבודדת ש-
/// <c>Pipe.Create(document, systemTypeId, pipeTypeId, levelId, start, end)</c>
/// יכול ליצור <see cref="Pipe"/> אמיתי במודל, בגודל שנבחר לפי **דרישת-
/// הקוטר של המשרד** (מדויק או טווח) מקובץ-ההגדרות (הזרימה:
/// <c>PlumbingSystem.OfficeConfig.txt</c> → <see cref="PipingOfficeSettings"/>
/// → <see cref="PipeDiameterRequirement"/> → <see cref="PipeModelReadiness"/>).
/// </summary>
/// <remarks>
/// **מה הפקודה עושה**: טוענת את דרישת-הקוטר מקובץ-ההגדרות, מריצה בדיקת
/// מוכנות-מודל (<see cref="PipeModelReadinessInspector"/> → <see cref="PipeModelReadiness"/>)
/// שמאתרת את **הגודל הקיים הקטן ביותר** שעומד בדרישה, ואם המודל מוכן -
/// מבקשת נקודת-התחלה מהמשתמש ויוצרת צינור בודד קצר (2 מ', אופקי) בגודל
/// הזה, עם <c>PipeType</c>/<c>PipingSystemType</c> שנבחרו על ידי בדיקת-
/// המוכנות.
///
/// **מה הפקודה לא עושה** (PIPE Step 4 מבודד): לא route שלם, לא נתוני
/// אסלה/קולטן, לא Fittings/Elbows/Connectors, לא wall-routing, לא נגיעה
/// ב-<c>DrawPipesCommand</c> / ב-<c>DirectShape</c> / ב-<c>DeleteExistingPipes</c> /
/// ב-Connection Inspector / באלגוריתם ה-Core. אם אף גודל-<c>Segment</c>
/// קיים אינו עומד בדרישה - **אין fallback לקוטר אחר, אין עיגול, אין
/// יצירת גודל** - מוצגת שגיאת Model Readiness מפורשת.
///
/// **הצינור הניסיוני מסומן** (<see cref="TestPipeMarker"/> ב-Comments וב-
/// Mark) - לזיהוי מיידי, ואינו מעורבב עם צינורות STARTARC (<c>"PIPE-"</c>).
/// כל הרצה מוחקת קודם צינורות-ניסוי קודמים (לפי הסימון) ואז יוצרת אחד -
/// כך אין הצטברות. ניתן גם למחוק ידנית (בחירה + Delete).
/// </remarks>
[Transaction(TransactionMode.Manual)]
public class CreateTestPipeCommand : IExternalCommand
{
    /// <summary>סימון (Comments + Mark) של הצינור הניסיוני - שונה מקידומת <c>"PIPE-"</c> של STARTARC, כדי ש-<c>DeleteExistingPipes</c> לא ייגע בו ולהפך.</summary>
    private const string TestPipeMarker = "STEP4-TESTPIPE (PIPE Step 4 diagnostic - safe to delete)";

    /// <summary>אורך הצינור הניסיוני (מטרים) - קבוע, אופקי, מנקודת-ההתחלה בכיוון +X.</summary>
    private const double TestPipeLengthMeters = 2.0;

    /// <inheritdoc/>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        // 1. דרישת-הקוטר מקובץ-ההגדרות המשרדי (מדויק או טווח - Step 4.1). לא hardcoded.
        PipeDiameterRequirement requirement;
        try
        {
            requirement = PipingOfficeSettings.Load().SewerPipeDiameterRequirement;
        }
        catch (OfficeConfigException ex)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - Pipe ניסיוני נכשל", ex.Message);
            return Result.Failed;
        }

        // 2. בדיקת מוכנות-מודל: מחפשת גודל-Segment קיים שעומד בדרישה
        //    (הקטן ביותר, אם כמה מתאימים). אם אין - שגיאה מפורשת, בלי fallback.
        PipeModelReadinessResult readiness = PipeModelReadinessInspector.Evaluate(doc, requirement);

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - PIPE Step 4: Create Test Pipe (isolated Pipe.Create proof) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("--- Model Readiness ---");
        sb.AppendLine(readiness.Report);

        if (!readiness.IsReady)
        {
            string path = WriteReport(sb.ToString());
            message = "המודל אינו מוכן לקוטר שהוגדר במשרד - ראו הדוח שנפתח.";
            TaskDialog.Show("PlumbingSystem - המודל אינו מוכן (Model Readiness)", readiness.Report);
            OpenReport(path);
            return Result.Failed;
        }

        // 3. Level (מהתצוגה הפעילה, או הנמוך ביותר במודל).
        Level? level = ResolveLevel(doc, uidoc.ActiveView);
        if (level is null)
        {
            message = "לא נמצא Level במודל - לא ניתן ליצור Pipe.";
            TaskDialog.Show("PlumbingSystem - Pipe ניסיוני נכשל", message);
            return Result.Failed;
        }

        // 4. נקודת-התחלה בטוחה: המשתמש בוחר. נקודת-הסיום קבועה יחסית אליה.
        XYZ startPoint;
        try
        {
            startPoint = uidoc.Selection.PickPoint("PIPE Step 4: בחר נקודת התחלה לצינור-הניסוי (קליק במקום פנוי)");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        double lengthFeet = UnitUtils.ConvertToInternalUnits(TestPipeLengthMeters, UnitTypeId.Meters);
        var endPoint = new XYZ(startPoint.X + lengthFeet, startPoint.Y, startPoint.Z);

        ElementId? pipeTypeId = PipeModelReadinessInspector.FindPipeTypeId(doc, readiness.MatchingPipeTypeName!);
        ElementId? systemTypeId = PipeModelReadinessInspector.FindSanitarySystemTypeId(doc, readiness.SanitarySystemTypeName);
        if (pipeTypeId is null || systemTypeId is null)
        {
            message = "בדיקת המוכנות עברה אך לא נמצאו ה-ElementId של ה-PipeType/PipingSystemType - מצב לא צפוי.";
            TaskDialog.Show("PlumbingSystem - Pipe ניסיוני נכשל", message);
            return Result.Failed;
        }

        // הגודל הקיים שנבחר מהמודל (הקטן ביותר שעומד בדרישה) - **לא** גבול-
        // טווח ולא הדרישה הגולמית. IsReady מבטיח שהוא לא null.
        double selectedDiameterMm = readiness.SelectedDiameterMm!.Value;

        // 5. יצירה - בתוך Transaction אחד: מחיקת צינורות-ניסוי קודמים, ואז יצירה + קוטר + סימון.
        Pipe createdPipe;
        double appliedDiameterMm;
        int deletedPriorTestPipes;
        using (var tx = new Transaction(doc, "PlumbingSystem - Create Test Pipe (Step 4)"))
        {
            tx.Start();
            try
            {
                deletedPriorTestPipes = DeletePriorTestPipes(doc);

                createdPipe = Pipe.Create(doc, systemTypeId, pipeTypeId, level.Id, startPoint, endPoint);

                double diameterFeet = UnitUtils.ConvertToInternalUnits(selectedDiameterMm, UnitTypeId.Millimeters);
                createdPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(diameterFeet);

                createdPipe.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(TestPipeMarker);
                createdPipe.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(TestPipeMarker);

                doc.Regenerate();
                appliedDiameterMm = UnitUtils.ConvertFromInternalUnits(
                    createdPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0.0,
                    UnitTypeId.Millimeters);

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                {
                    tx.RollBack();
                }

                string failReport = sb.ToString()
                    + Environment.NewLine
                    + "--- Pipe.Create FAILED (all changes rolled back) ---" + Environment.NewLine
                    + ex.Message;
                string failPath = WriteReport(failReport);
                message = ex.Message;
                TaskDialog.Show(
                    "PlumbingSystem - Pipe.Create נכשל",
                    $"יצירת ה-Pipe נכשלה - כל השינויים בוטלו (Rollback):{Environment.NewLine}{Environment.NewLine}{ex.Message}");
                OpenReport(failPath);
                return Result.Failed;
            }
        }

        // 6. אימות: זהו Revit Pipe אמיתי (לא DirectShape), בקטגוריית Pipes.
        Element verify = doc.GetElement(createdPipe.Id);
        bool isRealPipe = verify is Pipe;
        long? categoryId = verify.Category?.Id.Value;
        bool isPipesCategory = categoryId == (long)BuiltInCategory.OST_PipeCurves;

        sb.AppendLine();
        sb.AppendLine("--- Pipe.Create result ---");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Deleted prior test pipes: {0}", deletedPriorTestPipes));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Created ElementId: {0}", createdPipe.Id.Value));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Runtime type: {0}  (is Autodesk.Revit.DB.Plumbing.Pipe = {1})", verify.GetType().FullName, isRealPipe));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Category: {0}  (is OST_PipeCurves = {1})", verify.Category?.Name ?? "(none)", isPipesCategory));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "PipeType: \"{0}\"   PipingSystemType: \"{1}\"", readiness.MatchingPipeTypeName, readiness.SanitarySystemTypeName));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Office requirement (from OfficeConfig): {0}", requirement.Describe()));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Selected existing size (smallest that meets the requirement): {0:0.###} mm", selectedDiameterMm));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Applied diameter (read back from the pipe): {0:0.###} mm", appliedDiameterMm));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Level: \"{0}\"", level.Name));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Marker (Comments + Mark): \"{0}\"", TestPipeMarker));
        sb.AppendLine("NOTE: this test pipe is NOT a STARTARC route (no \"PIPE-\" prefix). Re-running replaces it. Safe to delete manually.");

        string reportPath = WriteReport(sb.ToString());

        uidoc.Selection.SetElementIds(new List<ElementId> { createdPipe.Id });
        uidoc.ShowElements(createdPipe.Id);

        TaskDialog.Show(
            "PlumbingSystem - Pipe ניסיוני נוצר (Step 4)",
            string.Format(
                CultureInfo.InvariantCulture,
                "נוצר Revit Pipe אמיתי.{5}{5}ElementId: {0}{5}סוג: {1}{5}דרישת המשרד (OfficeConfig): {2}{5}גודל קיים שנבחר (הקטן ביותר שעומד בדרישה): {3:0.###} מ\"מ{5}קוטר בפועל על הצינור: {4:0.###} מ\"מ{5}{5}הצינור נבחר ומסומן בתצוגה. ראו הדוח שנפתח.",
                createdPipe.Id.Value,
                isRealPipe ? "Autodesk.Revit.DB.Plumbing.Pipe (קטגוריית Pipes)" : verify.GetType().Name,
                requirement.Describe(),
                selectedDiameterMm,
                appliedDiameterMm,
                Environment.NewLine));

        OpenReport(reportPath);
        return Result.Succeeded;
    }

    private static Level? ResolveLevel(Document doc, View activeView)
    {
        if (activeView.GenLevel is Level fromView)
        {
            return fromView;
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .FirstOrDefault();
    }

    private static int DeletePriorTestPipes(Document doc)
    {
        List<ElementId> priorTestPipeIds = new FilteredElementCollector(doc)
            .OfClass(typeof(Pipe))
            .Cast<Pipe>()
            .Where(p =>
                p.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == TestPipeMarker
                || p.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() == TestPipeMarker)
            .Select(p => p.Id)
            .ToList();

        if (priorTestPipeIds.Count > 0)
        {
            doc.Delete(priorTestPipeIds);
        }

        return priorTestPipeIds.Count;
    }

    private static string WriteReport(string content)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_TestPipe_Step4_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static void OpenReport(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
