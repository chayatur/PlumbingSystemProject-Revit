using System.Diagnostics;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת-אבחון **זמנית**, `ReadOnly` לחלוטין - **לא** נוגעת ב-
/// <see cref="RevitModelReader.IsToiletFixture"/>, לא ב-
/// <see cref="Config.FixtureFamilyNames"/>, ולא בשום לוגיקת-זיהוי קיימת.
/// המטרה: לאסוף **נתונים גולמיים אמיתיים** (Connectors, פרמטרי-סיווג,
/// כל הפרמטרים) על **כל** Family+Type תחת <c>OST_PlumbingFixtures</c>
/// במסמך הפעיל - בלי סינון-מראש, בלי הנחה-איזה-מהם-אסלה - כדי לבדוק
/// אם קיים דפוס-מבדיל אמיתי לפני שמעצבים אלגוריתם-זיהוי חדש. ראו
/// docs/toilet-detection-investigation.md.
/// </summary>
/// <remarks>
/// **לא מדפיסה רק סיכום**: לכל Family+Type - כל ה-Connectors (עם כל
/// תכונה שאפשר לקרוא, ודיווח-מפורש-כשמשהו לא-זמין/null/לא-רלוונטי-
/// לצורה-הזו, לא שקט), פרמטרי-סיווג (OmniClass/Assembly, בכל השמות-
/// המוכרים, גם ברמת-Instance וגם Type), פרמטרי-Fixture-Unit (סריקה
/// לפי שם-מכיל, לא BuiltInParameter קבוע - כדי לא-לפספס-ניסוח-שונה),
/// וכל שאר הפרמטרים (Instance+Type) שיש להם ערך בפועל.
/// </remarks>
[Transaction(TransactionMode.ReadOnly)]
public class DiscoverFixtureSignatureCommand : IExternalCommand
{
    /// <summary>
    /// אוספת את כל <see cref="FamilyInstance"/> תחת <c>OST_PlumbingFixtures</c>
    /// במסמך הפעיל, מקבצת לפי Family+Type, וכותבת דוח-קריאה-בלבד
    /// מפורט לקובץ טקסט (Connectors, פרמטרי-סיווג, כל הפרמטרים) -
    /// לא יוצרת/משנה/מוחקת שום אלמנט.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">לא בשימוש - הפקודה לא אמורה להיכשל בתרחיש רגיל.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> לאחר כתיבת הדוח ופתיחתו.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;

        List<FamilyInstance> allFixtures = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Fixture Connector Signature Discovery (temporary diagnostic, READ-ONLY) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Purpose: raw Connector/classification/parameter data for EVERY OST_PlumbingFixtures");
        sb.AppendLine("Family+Type in the active document - no filtering, no assumption about which one");
        sb.AppendLine("is a toilet. Does NOT read/use IsToiletFixture or FixtureFamilyNames at all.");
        sb.AppendLine("Creates/changes NOTHING in the model.");
        sb.AppendLine($"Total OST_PlumbingFixtures instances found: {allFixtures.Count}");

        var groups = allFixtures
            .GroupBy(fi => (FamilyName: fi.Symbol?.Family?.Name ?? "(no Family)", TypeName: fi.Symbol?.Name ?? "(no Type)"))
            .OrderBy(g => g.Key.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sb.AppendLine($"Distinct Family+Type combinations: {groups.Count}");
        sb.AppendLine();
        sb.AppendLine("--- Overview (all Family+Type combinations found) ---");
        foreach (var group in groups)
        {
            sb.AppendLine($"  '{group.Key.FamilyName}' / '{group.Key.TypeName}': {group.Count()} instance(s)");
        }

        foreach (var group in groups)
        {
            try
            {
                AppendFixtureTypeSection(sb, doc, group.Key.FamilyName, group.Key.TypeName, group.ToList());
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine($"=== Family='{group.Key.FamilyName}' Type='{group.Key.TypeName}' - ERROR INSPECTING THIS TYPE ===");
                sb.AppendLine($"  {ex.GetType().Name}: {ex.Message}");
            }
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_FixtureSignatureDiscovery_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    private static void AppendFixtureTypeSection(
        StringBuilder sb, Document doc, string familyName, string typeName, List<FamilyInstance> instances)
    {
        FamilyInstance representative = instances[0];
        FamilySymbol? symbol = representative.Symbol;

        sb.AppendLine();
        sb.AppendLine($"=== Family='{familyName}'  Type='{typeName}'  TypeId={symbol?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "(no Symbol)"} ===");
        sb.AppendLine($"ElementIds ({instances.Count} instance(s)): {string.Join(", ", instances.Select(i => i.Id.Value))}");
        sb.AppendLine($"Category: {representative.Category?.Name ?? "(no Category)"}");
        sb.AppendLine($"Representative instance used for Connector/parameter inspection below: ElementId={representative.Id.Value}");

        AppendConnectorSection(sb, representative);
        AppendClassificationParameters(sb, representative, symbol);
        AppendFixtureUnitParameters(sb, representative, symbol);
        AppendAllParameters(sb, "INSTANCE", representative);
        if (symbol is not null)
        {
            AppendAllParameters(sb, "TYPE", symbol);
        }
    }

    private static void AppendConnectorSection(StringBuilder sb, FamilyInstance instance)
    {
        sb.AppendLine();
        sb.AppendLine("--- MEPModel / Connectors ---");

        MEPModel? mepModel = instance.MEPModel;
        sb.AppendLine($"MEPModel: {(mepModel is not null ? "present" : "NOT PRESENT (null)")}");

        if (mepModel is null)
        {
            return;
        }

        ConnectorManager? connectorManager = mepModel.ConnectorManager;
        sb.AppendLine($"ConnectorManager: {(connectorManager is not null ? "present" : "NOT PRESENT (null)")}");

        if (connectorManager is null)
        {
            return;
        }

        List<Connector> connectors = connectorManager.Connectors.Cast<Connector>().ToList();
        sb.AppendLine($"Connector count: {connectors.Count}");

        for (int i = 0; i < connectors.Count; i++)
        {
            AppendSingleConnector(sb, i + 1, connectors[i]);
        }
    }

    private static void AppendSingleConnector(StringBuilder sb, int index, Connector connector)
    {
        sb.AppendLine($"  Connector #{index}:");
        sb.AppendLine($"    ConnectorType = {DescribeSafe(() => connector.ConnectorType.ToString())}");

        Domain domain = Domain.DomainUndefined;
        string domainText = DescribeSafe(() =>
        {
            domain = connector.Domain;
            return domain.ToString();
        });
        sb.AppendLine($"    Domain = {domainText}");

        sb.AppendLine($"    PipeSystemType = {DescribeSafe(() =>
            domain == Domain.DomainPiping
                ? connector.PipeSystemType.ToString()
                : "N/A (not a piping-domain connector)")}");

        ConnectorProfileType shape = ConnectorProfileType.Invalid;
        string shapeText = DescribeSafe(() =>
        {
            shape = connector.Shape;
            return shape.ToString();
        });
        sb.AppendLine($"    Shape = {shapeText}");

        sb.AppendLine($"    Radius = {DescribeSafe(() =>
            shape == ConnectorProfileType.Round
                ? $"{connector.Radius:F4} ft = {UnitUtils.ConvertFromInternalUnits(connector.Radius, UnitTypeId.Millimeters):F1}mm (diameter {UnitUtils.ConvertFromInternalUnits(connector.Radius * 2, UnitTypeId.Millimeters):F1}mm)"
                : "N/A (shape is not Round)")}");

        sb.AppendLine($"    Width/Height = {DescribeSafe(() =>
            shape is ConnectorProfileType.Rectangular or ConnectorProfileType.Oval
                ? $"W={UnitUtils.ConvertFromInternalUnits(connector.Width, UnitTypeId.Millimeters):F1}mm  H={UnitUtils.ConvertFromInternalUnits(connector.Height, UnitTypeId.Millimeters):F1}mm"
                : "N/A (shape is not Rectangular/Oval)")}");

        sb.AppendLine($"    Origin = {DescribeSafe(() =>
        {
            XYZ o = connector.Origin;
            return $"({UnitUtils.ConvertFromInternalUnits(o.X, UnitTypeId.Meters):F3}, {UnitUtils.ConvertFromInternalUnits(o.Y, UnitTypeId.Meters):F3}, {UnitUtils.ConvertFromInternalUnits(o.Z, UnitTypeId.Meters):F3}) m";
        })}");

        sb.AppendLine($"    Direction (CoordinateSystem.BasisZ) = {DescribeSafe(() =>
        {
            XYZ d = connector.CoordinateSystem.BasisZ;
            return $"({d.X:F3}, {d.Y:F3}, {d.Z:F3})";
        })}");
    }

    /// <summary>
    /// עוטפת קריאה לתכונת-connector בודדת - מדווחת **במפורש** "(not
    /// available: ...)" במקום לזרוק/להיבלע-בשקט, כפי שנדרש: כל תכונה
    /// שלא-זמינה/לא-רלוונטית-לצורה-הזו/לא-קיימת בגרסת-ה-API הזו צריכה
    /// להיות גלויה בדוח, לא חוסר-שורה.
    /// </summary>
    private static string DescribeSafe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return $"(not available: {ex.GetType().Name}: {ex.Message})";
        }
    }

    private static readonly string[] ClassificationParameterNames =
    {
        "OmniClass Number", "OmniClass Title", "OmniClass Description",
        "Assembly Code", "Assembly Description", "Assembly Name",
    };

    private static void AppendClassificationParameters(StringBuilder sb, FamilyInstance instance, FamilySymbol? symbol)
    {
        sb.AppendLine();
        sb.AppendLine("--- Classification parameters (OmniClass / Assembly Code - checked by name, both levels) ---");

        sb.AppendLine("  [Instance level]");
        foreach (string name in ClassificationParameterNames)
        {
            sb.AppendLine($"    {name} = {DescribeNamedParameter(instance, name)}");
        }

        sb.AppendLine("  [Type level]");
        if (symbol is null)
        {
            sb.AppendLine("    (no Symbol/Type available)");
        }
        else
        {
            foreach (string name in ClassificationParameterNames)
            {
                sb.AppendLine($"    {name} = {DescribeNamedParameter(symbol, name)}");
            }
        }
    }

    private static void AppendFixtureUnitParameters(StringBuilder sb, FamilyInstance instance, FamilySymbol? symbol)
    {
        sb.AppendLine();
        sb.AppendLine("--- Plumbing Fixture Unit parameters (any parameter whose name contains \"Fixture Unit\", any level) ---");

        bool foundAny = false;
        foundAny |= AppendMatchingParameters(sb, "[Instance]", instance, "fixture unit");
        if (symbol is not null)
        {
            foundAny |= AppendMatchingParameters(sb, "[Type]", symbol, "fixture unit");
        }

        if (!foundAny)
        {
            sb.AppendLine("  (none found on this Family+Type, at either level)");
        }
    }

    private static bool AppendMatchingParameters(StringBuilder sb, string levelLabel, Element element, string nameContains)
    {
        bool foundAny = false;
        foreach (Parameter p in element.Parameters)
        {
            string? name = p.Definition?.Name;
            if (name is null || !name.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine($"  {levelLabel} {name} = {DescribeParameterValue(p)}");
            foundAny = true;
        }

        return foundAny;
    }

    private static void AppendAllParameters(StringBuilder sb, string levelLabel, Element element)
    {
        sb.AppendLine();
        sb.AppendLine($"--- All non-empty {levelLabel} parameters ---");

        var lines = new List<string>();
        foreach (Parameter p in element.Parameters)
        {
            string? name = p.Definition?.Name;
            if (name is null || !p.HasValue)
            {
                continue;
            }

            string value = DescribeParameterValue(p);
            if (string.IsNullOrWhiteSpace(value) || value == "0")
            {
                continue;
            }

            lines.Add($"  {name} = {value}");
        }

        if (lines.Count == 0)
        {
            sb.AppendLine("  (no non-empty parameters found)");
            return;
        }

        foreach (string line in lines.OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(line);
        }
    }

    private static string DescribeNamedParameter(Element element, string parameterName)
    {
        Parameter? p = element.LookupParameter(parameterName);
        if (p is null)
        {
            return "(parameter not found on this element)";
        }

        return p.HasValue ? DescribeParameterValue(p) : "(parameter exists but has no value)";
    }

    private static string DescribeParameterValue(Parameter p)
    {
        try
        {
            return p.StorageType switch
            {
                StorageType.String => p.AsString() ?? "(null string)",
                StorageType.Integer => p.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.Double => p.AsDouble().ToString("F4", CultureInfo.InvariantCulture),
                StorageType.ElementId => p.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                _ => $"(unsupported StorageType: {p.StorageType})",
            };
        }
        catch (Exception ex)
        {
            return $"(could not read value: {ex.Message})";
        }
    }
}
