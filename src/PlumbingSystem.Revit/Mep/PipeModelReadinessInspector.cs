using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using PlumbingSystem.Core.Domain;

namespace PlumbingSystem.Revit.Mep;

/// <summary>
/// שכבת-Revit של בדיקת מוכנות-המודל (PIPE Step 4): שולפת מהמסמך את
/// טיפוסי-המערכת המסווגים <c>Sanitary</c> ואת הקטרים ש-<c>PipeType</c>-ים
/// תומכים בהם **בפועל** (דרך <c>RoutingPreferenceManager</c> →
/// <c>PipeSegment.GetSizes()</c>), ומעבירה ל-<see cref="PipeModelReadiness.Evaluate"/>
/// הטהור (Core) שמכריע "מוכן / לא-מוכן, ולמה".
/// </summary>
/// <remarks>
/// אותה טכניקת-שליפה בדיוק כמו <c>DiscoverPipingTypesCommand</c> (פקודת-
/// האבחון של PIPE Step 1), אבל כאן היא מוצאת לשירות משותף כדי שגם
/// <c>CreateTestPipeCommand</c> (Step 4) וגם השילוב העתידי ב-Step 5
/// יוכלו להשתמש בה בלי לשכפל. **קריאה-בלבד** - לא נוגעת במודל.
/// </remarks>
public static class PipeModelReadinessInspector
{
    /// <summary>
    /// בודקת אם <paramref name="doc"/> יכול לספק צינור העומד ב-
    /// <paramref name="requirement"/> (דרישת-הקוטר מקובץ-ההגדרות המשרדי -
    /// מדויק או טווח).
    /// </summary>
    public static PipeModelReadinessResult Evaluate(Document doc, PipeDiameterRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(requirement);

        List<string> sanitarySystemTypeNames = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .Cast<PipingSystemType>()
            .Where(t => t.SystemClassification == MEPSystemClassification.Sanitary)
            .Select(t => t.Name)
            .ToList();

        List<CandidatePipeType> pipeTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(PipeType))
            .Cast<PipeType>()
            .Select(pt => new CandidatePipeType(pt.Name, ReadSupportedDiametersMm(doc, pt)))
            .ToList();

        return PipeModelReadiness.Evaluate(requirement, sanitarySystemTypeNames, pipeTypes);
    }

    /// <summary>
    /// ה-<c>ElementId</c> של ה-<c>PipeType</c> ששמו <paramref name="name"/>
    /// (הראשון, אם יש כמה), או <c>null</c>.
    /// </summary>
    public static ElementId? FindPipeTypeId(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(PipeType))
            .Cast<PipeType>()
            .FirstOrDefault(pt => string.Equals(pt.Name, name, StringComparison.Ordinal))
            ?.Id;

    /// <summary>
    /// ה-<c>ElementId</c> של טיפוס-מערכת Sanitary ששמו <paramref name="name"/>
    /// (או הראשון המסווג Sanitary, אם <paramref name="name"/> הוא <c>null</c>),
    /// או <c>null</c>.
    /// </summary>
    public static ElementId? FindSanitarySystemTypeId(Document doc, string? name)
    {
        List<PipingSystemType> sanitary = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .Cast<PipingSystemType>()
            .Where(t => t.SystemClassification == MEPSystemClassification.Sanitary)
            .ToList();

        PipingSystemType? chosen = name is null
            ? sanitary.FirstOrDefault()
            : sanitary.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        return chosen?.Id;
    }

    private static IReadOnlyList<double> ReadSupportedDiametersMm(Document doc, PipeType pipeType)
    {
        var sizesMm = new List<double>();

        try
        {
            RoutingPreferenceManager routingManager = pipeType.RoutingPreferenceManager;
            int ruleCount = routingManager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);

            for (int i = 0; i < ruleCount; i++)
            {
                RoutingPreferenceRule rule = routingManager.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                if (doc.GetElement(rule.MEPPartId) is not PipeSegment segment)
                {
                    continue;
                }

                sizesMm.AddRange(segment.GetSizes()
                    .Select(size => UnitUtils.ConvertFromInternalUnits(size.NominalDiameter, UnitTypeId.Millimeters)));
            }
        }
        catch (Exception)
        {
            // PipeType עם routing-preferences חריג - מטופל כ"אין קטרים
            // נתמכים", בדיוק כמו ב-DiscoverPipingTypesCommand (try/catch פר-טיפוס).
        }

        return sizesMm.Distinct().ToList();
    }
}
