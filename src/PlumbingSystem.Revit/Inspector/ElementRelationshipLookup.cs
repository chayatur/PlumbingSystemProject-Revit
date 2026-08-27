using Autodesk.Revit.DB;

namespace PlumbingSystem.Revit.Inspector;

/// <summary>
/// **קוראת בלבד - אף פעם לא מחשבת** קשר. כל הקשר שהיא מחזירה כבר
/// persisted על אלמנטים קיימים במודל (נכתב פעם אחת, ב-`DrawPipesCommand`/
/// `CollectorPlacementService`, בזמן היצירה) - הכלי הזה רק מפענח
/// מחרוזות (`Mark`) שכבר שם. אפס קריאה ל-`PipeRouteCalculator`,
/// `CollectorLocator`, `WallEdgeSnapper` או `RevitModelReader`. ראו
/// docs/connection-inspector.md.
/// </summary>
/// <remarks>
/// **זיהוי "זו אסלה-רלוונטית" נעשה בעקיפין, בכוונה**: לא נבדקת קטגוריית/
/// שם-משפחת האלמנט הנבחר בכלל (זה היה מחייב לשכפל/לחשוף את לוגיקת-
/// הזיהוי הפרטית ב-<c>RevitModelReader.IsToiletFixture</c>). במקום זה,
/// נבדק אם **קיים כבר צינור** שה-<c>Mark</c> שלו מתחיל ב-<c>"PIPE-{id}-"</c> -
/// אם כן, זו בהכרח אסלה שכבר עובדה בהצלחה על ידי "צייר צינורות" בעבר,
/// בלי תלות בקטגוריה/שם-משפחה שלה כלל. המשמעות: אסלה שעדיין לא
/// עובדה (הרצה עדיין לא בוצעה על הקומה שלה) תוצג כ"לא-רלוונטי", לא
/// כ"אסלה בלי חיבור" - מגבלה ידועה של הגישה הזו, לא תקלה.
/// </remarks>
/// <remarks>
/// **קבועי-מחרוזת כפולים בכוונה**: <see cref="CollectorNamePrefix"/>,
/// <see cref="PipeNamePrefix"/> ו-<see cref="ManualEngineeringMaterialName"/>
/// חייבים להישאר זהים-בדיוק למקביליהם ב-<c>CollectorPlacementService.cs</c>/
/// <c>DrawPipesCommand.cs</c> (שם הם <c>private</c>, לא נגישים מכאן).
/// נבחרה כפילות-קטנה-ומתועדת על פני שינוי-נראות בקבצי-ליבה קיימים -
/// ראו docs/connection-inspector.md להסבר המלא.
/// </remarks>
/// <remarks>
/// **מספר-דירה (<see cref="ConnectedPipe.ApartmentLabel"/>)**: לא persisted
/// באף מקום (RouteId/CollectorId לא מכילים אותו) - הדרך היחידה לקבל
/// אותו בלי לחשב-מחדש שום דבר הנדסי היא <c>Document.GetRoomAtPoint</c>
/// על מיקום-האסלה עצמה, בדיוק אותה טכניקה שכבר קיימת ב-
/// <c>RevitModelReader.CollectFixturesWithRoom</c>/<c>CollectorPlacementService.GetRoomContext</c> -
/// קריאת-Room-קיים, לא שיוך-קולטן-מחדש. מחושב **רק** על התוצאות-הסופיות-
/// המסוננות (לא על כל צינור במסמך) - שיקול-ביצועים, ראו <see cref="Enrich"/>.
/// </remarks>
public static class ElementRelationshipLookup
{
    private const string CollectorNamePrefix = "PlumbingSystem Collector ";
    private const string PipeNamePrefix = "PlumbingSystem Pipe ";
    private const string ManualEngineeringMaterialName = "PlumbingSystem Manual Engineering Orange";
    private const string PipeIdPrefix = "PIPE-";
    private const string CollectorMarkerInfix = "-COL-";

    /// <summary>סוג-האלמנט שנבחר בפועל - קובע איזה כיוון-פענוח מופעל.</summary>
    public enum SelectedKind
    {
        /// <summary>נבחרה אסלה (או כל אלמנט אחר שכבר יש לו צינור מחובר) - לא נבדקת קטגוריה/שם-משפחה, ראו remarks.</summary>
        Fixture,

        /// <summary>נבחר קולטן (DirectShape שנוצר על ידי CollectorPlacementService).</summary>
        Collector,

        /// <summary>נבחר הצינור עצמו (DirectShape שנוצר על ידי DrawPipesCommand) - פענוח ה-Mark של האלמנט-הנבחר-עצמו, בלי סריקת-אחרים.</summary>
        Pipe,
    }

    /// <summary>
    /// צינור בודד שנמצא, כפי שפוענח מה-<c>Mark</c> הקיים שלו, מועשר
    /// (רק על תוצאות-סופיות, ראו <see cref="Enrich"/>) עם ה-<c>ElementId</c>
    /// של האסלה-המקורית ומספר-הדירה שלה - שניהם יכולים להיות <c>null</c>
    /// אם האסלה נמחקה מאז, או שאין ל-Room-שלה מספר.
    /// </summary>
    public sealed record ConnectedPipe(
        ElementId PipeElementId,
        string RouteId,
        string FixtureIdLabel,
        ElementId? FixtureElementId,
        string? ApartmentLabel,
        string CollectorId,
        bool RequiresManualEngineering);

    /// <summary>תוצאת-הפענוח המלאה עבור אלמנט נבחר אחד.</summary>
    public sealed record RelationshipInfo(
        SelectedKind Kind,
        ElementId SelectedElementId,
        ElementId? CollectorElementId,
        string? CollectorLabel,
        IReadOnlyList<ConnectedPipe> Pipes);

    /// <summary>
    /// מנסה לפענח את הקשר של <paramref name="selectedId"/> - מחזירה
    /// <c>null</c> אם האלמנט אינו קולטן/צינור/אסלה-שכבר-עובדה (הקורא
    /// אחראי להציג הודעת "לא-רלוונטי", לא לשנות שום דבר במודל).
    /// </summary>
    public static RelationshipInfo? TryDescribe(Document doc, ElementId selectedId)
    {
        Element? selected = doc.GetElement(selectedId);
        if (selected?.Name is not string selectedName)
        {
            return null;
        }

        if (selectedName.StartsWith(CollectorNamePrefix, StringComparison.Ordinal))
        {
            string? collectorMark = selected.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            if (string.IsNullOrEmpty(collectorMark))
            {
                return null;
            }

            List<ConnectedPipe> pipes = AllPipes(doc)
                .Where(p => p.CollectorId == collectorMark)
                .Select(p => Enrich(doc, p))
                .ToList();
            return new RelationshipInfo(SelectedKind.Collector, selectedId, selectedId, collectorMark, pipes);
        }

        if (selectedName.StartsWith(PipeNamePrefix, StringComparison.Ordinal))
        {
            ConnectedPipe? selfPipe = ParsePipe(selected);
            if (selfPipe is null)
            {
                return null;
            }

            ConnectedPipe enriched = Enrich(doc, selfPipe);
            ElementId? pipeCollectorElementId = FindCollectorElementId(doc, enriched.CollectorId);
            return new RelationshipInfo(SelectedKind.Pipe, selectedId, pipeCollectorElementId, enriched.CollectorId, new[] { enriched });
        }

        string fixturePrefix = $"{PipeIdPrefix}{selectedId.Value}{CollectorMarkerInfix}";
        List<ConnectedPipe> matchingPipes = AllPipes(doc)
            .Where(p => p.RouteId.StartsWith(fixturePrefix, StringComparison.Ordinal))
            .Select(p => Enrich(doc, p))
            .ToList();

        if (matchingPipes.Count == 0)
        {
            return null;
        }

        ElementId? collectorElementId = FindCollectorElementId(doc, matchingPipes[0].CollectorId);
        return new RelationshipInfo(SelectedKind.Fixture, selectedId, collectorElementId, matchingPipes[0].CollectorId, matchingPipes);
    }

    /// <summary>
    /// כל הצינורות במסמך, מפוענחים **בזול** בלבד (בלי חיפוש-אסלה/Room -
    /// ראו <see cref="Enrich"/>) - נקראת פעם אחת לכל שאילתה, לפני סינון.
    /// </summary>
    private static IEnumerable<ConnectedPipe> AllPipes(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .Where(e => e.Name is not null && e.Name.StartsWith(PipeNamePrefix, StringComparison.Ordinal))
            .Select(ParsePipe)
            .Where(p => p is not null)
            .Select(p => p!);
    }

    private static ConnectedPipe? ParsePipe(Element element)
    {
        string? routeId = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        if (string.IsNullOrEmpty(routeId) || !routeId.StartsWith(PipeIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // routeId format: "PIPE-{fixtureId}-COL-{collectorFixtureId}" -
        // ראו PipeRouteCalculator.BuildRouteId. ה-CollectorId עצמו הוא
        // "COL-{collectorFixtureId}" - כלומר כל מה שאחרי המקף הראשון
        // שמפריד בין fixtureId ל-"COL-...".
        string withoutPrefix = routeId[PipeIdPrefix.Length..];
        int collectorMarkerIndex = withoutPrefix.IndexOf(CollectorMarkerInfix, StringComparison.Ordinal);
        if (collectorMarkerIndex < 0)
        {
            return null;
        }

        string fixtureIdLabel = withoutPrefix[..collectorMarkerIndex];
        string collectorId = withoutPrefix[(collectorMarkerIndex + 1)..];

        return new ConnectedPipe(
            element.Id, routeId, fixtureIdLabel,
            FixtureElementId: null, ApartmentLabel: null,
            collectorId, HasManualEngineeringMaterial(element));
    }

    /// <summary>
    /// מוסיפה ל-<paramref name="raw"/> את ה-<c>ElementId</c> של האסלה-
    /// המקורית ואת מספר-הדירה שלה - נקראת **רק** על תוצאות-סופיות-
    /// מסוננות (לא על כל צינור במסמך), כי זו קריאת-Revit (GetRoomAtPoint)
    /// שאין טעם לבזבז על צינורות שלא ייכללו בתוצאה בכלל.
    /// </summary>
    private static ConnectedPipe Enrich(Document doc, ConnectedPipe raw)
    {
        (ElementId? fixtureElementId, string? apartmentLabel) = ResolveFixtureInfo(doc, raw.FixtureIdLabel);
        return raw with { FixtureElementId = fixtureElementId, ApartmentLabel = apartmentLabel };
    }

    private static (ElementId? FixtureElementId, string? ApartmentLabel) ResolveFixtureInfo(Document doc, string fixtureIdLabel)
    {
        if (!long.TryParse(fixtureIdLabel, out long rawId))
        {
            return (null, null);
        }

        var fixtureElementId = new ElementId(rawId);
        Element? fixtureElement = doc.GetElement(fixtureElementId);
        if (fixtureElement is null)
        {
            // האסלה נמחקה מאז שהצינור נוצר - עדיין מציגים את מה שכן ידוע.
            return (null, null);
        }

        XYZ? point = (fixtureElement.Location as LocationPoint)?.Point;
        string? apartmentLabel = point is null ? null : doc.GetRoomAtPoint(point)?.Number;

        return (fixtureElementId, apartmentLabel);
    }

    private static bool HasManualEngineeringMaterial(Element element)
    {
        try
        {
            return element.GetMaterialIds(false)
                .Select(id => element.Document.GetElement(id) as Material)
                .Any(m => m?.Name == ManualEngineeringMaterialName);
        }
        catch
        {
            return false;
        }
    }

    private static ElementId? FindCollectorElementId(Document doc, string collectorId)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .Where(e => e.Name is not null && e.Name.StartsWith(CollectorNamePrefix, StringComparison.Ordinal))
            .FirstOrDefault(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() == collectorId)
            ?.Id;
    }
}
