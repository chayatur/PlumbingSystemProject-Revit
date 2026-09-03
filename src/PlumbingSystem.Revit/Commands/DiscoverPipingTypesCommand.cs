using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת-אבחון **זמנית**, `ReadOnly` לחלוטין (בדיוק כמו <see cref="DiscoverModelCommand"/>) -
/// לא יוצרת/משנה שום אלמנט, לא נוגעת ב-Pipe/Fitting/DirectShape קיים,
/// ולא משנה שום קוד/לוגיקה קיימים. **המטרה שונה במפורש מ-"מה יש
/// בפרויקט הזה"**: בודקת אם קיים מנגנון **גנרי, לא-תלוי-שם/ElementId**
/// לבחור אוטומטית PipingSystemType/PipeType מתאימים ל-STARTARC בכל
/// קובץ Revit - לא רק בפרויקט הנוכחי. ראו docs/pipe-mep-investigation.md.
/// </summary>
/// <remarks>
/// **שני האותות הגנריים שנבדקים, שניהם מובְנים ב-Revit API עצמו, לא
/// מוסכמות-שם**:
/// 1. <see cref="MEPSystemType.SystemClassification"/> - enum סטנדרטי
///    (לא מחרוזת-שם חופשית) - <c>MEPSystemClassification.Sanitary</c>
///    הוא הסיווג המתאים לביוב, בלי תלות באיך שהטיפוס נקרא בפועל
///    (יכול להיקרא "ביוב"/"Sanitary"/כל דבר אחר).
/// 2. <c>PipeType.RoutingPreferenceManager</c> - לכל <see cref="PipeType"/>,
///    בודקת אילו <see cref="PipeSegment"/> (Revit, לא Core) מוגדרים
///    בכללי-ה-Segments שלו, ואיזה קטרים (<c>MEPSize.NominalDiameter</c>)
///    כל Segment תומך-בהם בפועל - בלי להניח ששם-הטיפוס-הראשון-שנמצא
///    בהכרח-תומך ב-110 מ"מ.
/// </remarks>
[Transaction(TransactionMode.ReadOnly)]
public class DiscoverPipingTypesCommand : IExternalCommand
{
    /// <summary>קוטר-היעד (מ"מ) שה-STARTARC דורש - אותו קבוע בדיוק כמו <c>PipeRouteCalculator.PipeDiameterMm</c> (לא מיובא מ-Core בכוונה - זו פקודת-חקירה עצמאית, לא תלויה בלוגיקה הקיימת).</summary>
    private const double TargetDiameterMm = 110.0;

    /// <summary>סבילות-השוואה (מ"מ) - קטרים מדווחים לפעמים בעיגול-פנימי-של-Revit, לא שווי-בדיוק-לביט.</summary>
    private const double DiameterToleranceMm = 0.5;

    /// <summary>
    /// בודקת את שני האותות-הגנריים (ראו תיעוד-המחלקה) על המסמך הפעיל,
    /// כותבת דוח-קריאה-בלבד לקובץ טקסט ופותחת אותו. לא יוצרת/משנה/
    /// מוחקת שום אלמנט - <c>ReadOnly</c> אמיתי.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">לא בשימוש - הפקודה לא אמורה להיכשל בתרחיש רגיל.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> לאחר כתיבת הדוח ופתיחתו.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Piping Types Discovery (temporary diagnostic, READ-ONLY) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Purpose: investigate GENERIC, model-independent signals for auto-selecting a");
        sb.AppendLine("PipingSystemType/PipeType in ANY Revit file - not specific to this project.");
        sb.AppendLine("Creates/changes NOTHING in the model - pure read-only inspection.");
        sb.AppendLine($"Target diameter for STARTARC: {TargetDiameterMm:F0}mm (matches PipeRouteCalculator.PipeDiameterMm - not imported, this command is standalone).");

        AppendSystemTypesSection(sb, doc);
        AppendPipeTypesSection(sb, doc);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_PipingTypesDiscovery_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    /// <summary>
    /// סעיף 1: כל <see cref="PipingSystemType"/> במסמך, מקובץ לפי
    /// <see cref="MEPSystemType.SystemClassification"/> (ה-enum הגנרי,
    /// לא ה-<c>Name</c>) - מדגישה כמה מסווגים בפועל כ-
    /// <see cref="MEPSystemClassification.Sanitary"/>.
    /// </summary>
    private static void AppendSystemTypesSection(StringBuilder sb, Document doc)
    {
        sb.AppendLine();
        sb.AppendLine("--- 1. PipingSystemType elements (classified by SystemClassification enum, not by Name) ---");

        List<PipingSystemType> systemTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .Cast<PipingSystemType>()
            .ToList();

        sb.AppendLine($"Found: {systemTypes.Count} total");

        foreach (PipingSystemType systemType in systemTypes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  Name='{systemType.Name}'  SystemClassification={systemType.SystemClassification}  ElementId={systemType.Id.Value}");
        }

        int sanitaryCount = systemTypes.Count(t => t.SystemClassification == MEPSystemClassification.Sanitary);
        sb.AppendLine();
        sb.AppendLine($"Sanitary-classified system types found: {sanitaryCount}" +
            (sanitaryCount == 0
                ? " - GENERIC AUTO-SELECTION WOULD FIND NOTHING HERE. Would need an explicit fallback (fail clearly, or let the user pick) - not a silent guess."
                : " - generic selection by SystemClassification==Sanitary would find at least one candidate here, independent of its actual Name."));
    }

    /// <summary>
    /// סעיף 2: כל <see cref="PipeType"/> במסמך - לכל אחד, בודקת את
    /// <c>RoutingPreferenceManager</c> (כללי-Segments) ומדווחת אילו
    /// קטרים כל Segment תומך-בהם בפועל, ובפרט אם <see cref="TargetDiameterMm"/>
    /// נתמך. עטוף ב-try/catch **פר-טיפוס** - טיפוס אחד עם routing-
    /// preferences חריג לא אמור להפיל את כל הדוח.
    /// </summary>
    private static void AppendPipeTypesSection(StringBuilder sb, Document doc)
    {
        sb.AppendLine();
        sb.AppendLine("--- 2. PipeType elements and the diameters their Segments actually support ---");

        List<PipeType> pipeTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(PipeType))
            .Cast<PipeType>()
            .ToList();

        sb.AppendLine($"Found: {pipeTypes.Count} total");

        var supportsTarget = new List<string>();
        var doesNotSupportTarget = new List<string>();

        foreach (PipeType pipeType in pipeTypes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  PipeType='{pipeType.Name}'  ElementId={pipeType.Id.Value}");

            bool anySegmentSupportsTarget;
            try
            {
                anySegmentSupportsTarget = AppendSegmentRules(sb, doc, pipeType);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    (could not inspect RoutingPreferenceManager for this type: {ex.Message})");
                anySegmentSupportsTarget = false;
            }

            if (anySegmentSupportsTarget)
            {
                supportsTarget.Add(pipeType.Name);
            }
            else
            {
                doesNotSupportTarget.Add(pipeType.Name);
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- Summary ---");
        sb.AppendLine(supportsTarget.Count > 0
            ? $"PipeTypes that support {TargetDiameterMm:F0}mm: {string.Join(", ", supportsTarget.Select(n => $"'{n}'"))}"
            : $"PipeTypes that support {TargetDiameterMm:F0}mm: NONE FOUND - generic auto-selection would find nothing here.");
        sb.AppendLine(doesNotSupportTarget.Count > 0
            ? $"PipeTypes that do NOT support {TargetDiameterMm:F0}mm (or could not be inspected): {string.Join(", ", doesNotSupportTarget.Select(n => $"'{n}'"))}"
            : "PipeTypes that do NOT support it: none.");
    }

    /// <summary>
    /// מפרטת את כללי-ה-Segments של <paramref name="pipeType"/> - מחזירה
    /// <c>true</c> אם **לפחות אחד** מהם תומך ב-<see cref="TargetDiameterMm"/>
    /// בפועל (לא הנחה - קריאה אמיתית של <c>MEPSize.NominalDiameter</c>
    /// של כל גודל מוגדר על ה-<see cref="Autodesk.Revit.DB.Plumbing.PipeSegment"/> - שם Revit-ה"אמיתי" ל-Segment, לא ה-<c>PipeSegment</c> של Core שלנו).
    /// </summary>
    private static bool AppendSegmentRules(StringBuilder sb, Document doc, PipeType pipeType)
    {
        RoutingPreferenceManager routingManager = pipeType.RoutingPreferenceManager;
        int ruleCount = routingManager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);

        if (ruleCount == 0)
        {
            sb.AppendLine("    (no Segments rules defined in RoutingPreferenceManager)");
            return false;
        }

        bool anySupportsTarget = false;

        for (int i = 0; i < ruleCount; i++)
        {
            RoutingPreferenceRule rule = routingManager.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
            ElementId segmentId = rule.MEPPartId;

            if (doc.GetElement(segmentId) is not Autodesk.Revit.DB.Plumbing.PipeSegment segment)
            {
                sb.AppendLine($"    Segment rule #{i + 1}: MEPPartId={segmentId.Value} (could not resolve to a PipeSegment element)");
                continue;
            }

            List<double> nominalDiametersMm = segment.GetSizes()
                .Select(size => UnitUtils.ConvertFromInternalUnits(size.NominalDiameter, UnitTypeId.Millimeters))
                .OrderBy(mm => mm)
                .ToList();

            bool supportsTarget = nominalDiametersMm.Any(mm => Math.Abs(mm - TargetDiameterMm) <= DiameterToleranceMm);
            anySupportsTarget |= supportsTarget;

            string sizesText = nominalDiametersMm.Count > 0
                ? string.Join(", ", nominalDiametersMm.Select(mm => mm.ToString("F1", CultureInfo.InvariantCulture)))
                : "(no sizes defined)";

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    Segment rule #{0}: Material='{1}'  sizes(mm)=[{2}]  supports-{3:F0}mm={4}",
                i + 1,
                segment.MaterialId != ElementId.InvalidElementId ? (doc.GetElement(segment.MaterialId) as Material)?.Name ?? "(unknown)" : "(none)",
                sizesText,
                TargetDiameterMm,
                supportsTarget ? "YES" : "no"));
        }

        return anySupportsTarget;
    }
}
