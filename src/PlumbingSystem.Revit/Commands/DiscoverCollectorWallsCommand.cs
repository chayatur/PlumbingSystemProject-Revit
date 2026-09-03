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
using PlumbingSystem.Revit.Config;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת אבחון **זמנית, ReadOnly** - לא חלק מהפיצ'ר הסופי, לא נוגעת
/// בשום אלמנט, לא יוצרת/משנה/מוחקת כלום, ולא משנה שום חוק ניתוב.
/// המטרה: לאסוף את **הנתונים האמיתיים מהמודל** על סוגי-הקירות, החומרים,
/// העובי והמאפיינים המבניים של (א) הקיר/ים שמכילים את הקולטן ו-(ב)
/// הקיר/ים שחוסמים את המסלול הישר - עבור **כל** מסלול אסלה→קולטן בבניין -
/// כבסיס-נתונים להחלטה ההנדסית "אילו סוגי-קיר מותר לצינור ביוב לחדור
/// בדרך אל הקולטן". ראו docs/pipe-rca-chain.md חלק ז'/ח'.
/// </summary>
/// <remarks>
/// **דינאמי לחלוטין - אין Wall/Fixture/Collector IDs קשיחים**: הפקודה
/// עוברת על כל הדירות (<see cref="RevitModelReader.ReadApartments"/>),
/// מחשבת מיקומי-קולטן באותה שרשרת בדיוק כמו
/// <see cref="DrawPipesCommand"/> (<see cref="CollectorLocator.Locate"/>
/// + <see cref="CollectorPlacementService.SnapToNearestWallEdge"/>),
/// ומזהה את הקירות **גיאומטרית** - לא לפי ID.
///
/// **זיהוי קיר-הקולטן** משכפל את הלוגיקה של
/// <c>DrawPipesCommand.FindCollectorWallPenetration</c> (מרחק 2D
/// ≤ חצי-עובי + <see cref="CollectorWallContainmentToleranceMeters"/>) -
/// **הועתק, לא נגזר** (המתודה שם <c>private</c>; אותו עיקרון כמו
/// <c>CollectorSetbackDiagnosticCommand</c> שהעתיק את
/// <c>StaggeredCrossoverLengthCandidatesMeters</c>). כדי שהפער בין
/// שתי ההעתקות יתגלה אם אי-פעם יסטו, הקבוע כאן נושא את אותו שם ואותו
/// ערך, וסעיף ההשוואה בדוח מציין זאת במפורש.
///
/// **זיהוי קיר-חוסם** משתמש ב-<see cref="WallRayCasting.FindBlockingWallDetailed"/>
/// **האמיתי** (אותו ray-casting כמו "צייר צינורות"), וקולף קיר-אחר-קיר
/// (עד <see cref="MaxBlockingWallsPerRoute"/>) כדי לרשום את **כל** הקירות
/// שהקו הישר חוצה, לא רק הראשון.
/// </remarks>
[Transaction(TransactionMode.ReadOnly)]
public class DiscoverCollectorWallsCommand : IExternalCommand
{
    /// <summary>
    /// סבילות (מטרים) לקביעה "מיקום הקולטן נמצא בתוך גוף הקיר הזה" -
    /// **זהה בערכו ובמשמעותו** ל-<c>DrawPipesCommand.CollectorWallContainmentToleranceMeters</c>
    /// (מרחק 2D ≤ חצי-עובי-הקיר + הסבילות הזו). מועתק, לא נגזר - ראו
    /// הערת-המחלקה.
    /// </summary>
    private const double CollectorWallContainmentToleranceMeters = 0.02;

    /// <summary>
    /// כמה קירות לכל היותר לקלף מהקו הישר (חסימה-אחרי-חסימה) לצורך
    /// הרשימה בדוח. 4 - מספיק כדי לתפוס פינת-שני-קירות + קיר-חוצה נוסף,
    /// בלי לולאה בלתי-חסומה אם משהו משתבש.
    /// </summary>
    private const int MaxBlockingWallsPerRoute = 4;

    /// <summary>
    /// טווח (מטרים) ל"קירות בסביבת הקולטן" - הקשר בלבד (לא הקריטריון
    /// של <c>FindCollectorWallPenetration</c>). מציג גם קירות שאינם
    /// <c>Basic</c> / שעוביים לא נקרא, כדי לראות את התמונה המלאה סביב
    /// נקודת-החיבור.
    /// </summary>
    private const double NearbyContextRadiusMeters = 0.6;

    /// <summary>
    /// אוספת את כל הדירות, מחשבת לכל מסלול אסלה→קולטן את קיר/י-הקולטן
    /// וקיר/י-החסימה, ומדפיסה דוח-טקסט מפורט (סוג, חומרים, עובי, מבני)
    /// לקובץ שנפתח אוטומטית. לא משנה שום דבר במודל.
    /// </summary>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;

        var reader = new RevitModelReader(doc);
        var placementService = new CollectorPlacementService(doc);
        var wallRayCasting = new WallRayCasting(doc);

        List<Apartment> apartments;
        try
        {
            apartments = reader.ReadApartments();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            message = ex.Message;
            TaskDialog.Show("PlumbingSystem - אבחון קירות-קולטן נכשל", ex.Message);
            return Result.Failed;
        }

        var wallProfileCache = new Dictionary<long, string>();
        Dictionary<long, List<string>> roomsByWallId = BuildRoomsByWallId(doc);

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Collector / Blocking Wall Type Discovery (temporary diagnostic, READ-ONLY) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Purpose: real Revit wall data (type, materials, width, structural) for every fixture->collector route -");
        sb.AppendLine("basis for the engineering decision on which wall categories a sewer pipe may penetrate on its way to the collector.");
        sb.AppendLine("Creates / changes / deletes NOTHING. Changes NO routing rule.");
        sb.AppendLine("Wall identification is fully dynamic (geometric) - no hard-coded Wall / Fixture / Collector IDs.");
        sb.AppendLine($"Collector-wall test mirrors DrawPipesCommand.FindCollectorWallPenetration: 2D distance <= Wall.Width/2 + {CollectorWallContainmentToleranceMeters:F2}m, WallKind.Basic only.");
        sb.AppendLine();

        List<string> warnings = reader.Warnings.ToList();
        if (warnings.Count > 0)
        {
            sb.AppendLine($"--- Model reader warnings ({warnings.Count}) ---");
            foreach (string w in warnings)
            {
                sb.AppendLine($"  {w}");
            }

            sb.AppendLine();
        }

        int routeCount = 0;
        foreach (Apartment apartment in apartments.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            List<CollectorPoint> rawCollectors;
            try
            {
                rawCollectors = CollectorLocator.Locate(apartment);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                sb.AppendLine($"### Apartment '{apartment.Id}' (Floor {apartment.FloorNumber}) - CollectorLocator failed: {ex.Message}");
                sb.AppendLine();
                continue;
            }

            Dictionary<string, ToiletFixture> fixturesById = apartment.Fixtures.ToDictionary(f => f.Id);

            foreach (CollectorPoint rawCollector in rawCollectors)
            {
                CollectorPoint snappedCollector;
                try
                {
                    (snappedCollector, _) = placementService.SnapToNearestWallEdge(rawCollector);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    sb.AppendLine($"### Apartment '{apartment.Id}' - collector '{rawCollector.Id}' snap failed: {ex.Message}");
                    sb.AppendLine();
                    continue;
                }

                foreach (string fixtureId in rawCollector.ConnectedFixtureIds)
                {
                    if (!fixturesById.TryGetValue(fixtureId, out ToiletFixture? fixture))
                    {
                        continue;
                    }

                    routeCount++;
                    AppendRouteDiagnostic(sb, doc, wallRayCasting, apartment, fixture, snappedCollector, wallProfileCache, roomsByWallId);
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine($"=== Done. {routeCount} fixture->collector route(s) inspected across {apartments.Count} apartment(s). ===");

        string path = Path.Combine(
            Path.GetTempPath(),
            $"PlumbingSystem_CollectorWallDiscovery_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    /// <summary>
    /// לכל מסלול אחד: מזהה דינאמית את קיר/י-הקולטן ואת קיר/י-החסימה של
    /// המסלול הישר, ומדפיס לכל קיר את הפרופיל המלא + את השאלה
    /// "האם <c>CollectorWallPenetration</c> יחול על הקיר החוסם הזה".
    /// </summary>
    private static void AppendRouteDiagnostic(
        StringBuilder sb,
        Document doc,
        WallRayCasting wallRayCasting,
        Apartment apartment,
        ToiletFixture fixture,
        CollectorPoint collector,
        Dictionary<long, string> wallProfileCache,
        Dictionary<long, List<string>> roomsByWallId)
    {
        sb.AppendLine(new string('=', 100));
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "ROUTE  Apartment={0} (Floor {1})  FixtureElementId={2}  CollectorId={3}",
            apartment.Id, apartment.FloorNumber, fixture.Id, collector.Id));
        sb.AppendLine(new string('=', 100));

        if (!long.TryParse(fixture.Id, out long fixtureIdValue)
            || doc.GetElement(new ElementId(fixtureIdValue)) is not FamilyInstance familyInstance)
        {
            sb.AppendLine("  Could not resolve FamilyInstance for this fixture Id - skipped.");
            sb.AppendLine();
            return;
        }

        ElementId levelId = familyInstance.LevelId;
        XYZ fixturePoint = RevitUnitConversion.ToRevitPoint(fixture.Location);
        XYZ collectorPoint = RevitUnitConversion.ToRevitPoint(collector.Location);
        ElementId? hostWallId = familyInstance.Host is Wall ? familyInstance.Host.Id : null;

        double directDistanceMeters = GeometryUtils.Distance2D(fixture.Location, collector.Location);
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  Fixture location : ({0:F4}, {1:F4}, {2:F4})m",
            fixture.Location.X, fixture.Location.Y, fixture.Location.Z));
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  Collector location (final, wall-snapped): ({0:F4}, {1:F4}, {2:F4})m",
            collector.Location.X, collector.Location.Y, collector.Location.Z));
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "  Direct (2D) fixture->collector distance: {0:F4}m  (4.0m hard limit)",
            directDistanceMeters));
        sb.AppendLine($"  Fixture host wall: {(hostWallId is null ? "(none / not a wall)" : hostWallId.Value.ToString(CultureInfo.InvariantCulture))}");
        sb.AppendLine();

        // --- (A) קיר/י הקולטן - זיהוי גיאומטרי דינאמי ---
        List<CollectorWallHit> collectorWalls = FindCollectorContainingWalls(doc, levelId, collectorPoint);
        List<ElementId> nearbyContextWalls = FindNearbyWalls(doc, levelId, collectorPoint, NearbyContextRadiusMeters);

        sb.AppendLine("  --- (A) Wall(s) that CONTAIN the collector point (dynamic geometric identification) ---");
        if (collectorWalls.Count == 0)
        {
            sb.AppendLine("      NONE found within Wall.Width/2 + tolerance (Basic walls). Collector is not embedded in any Basic wall body.");
        }
        else
        {
            foreach (CollectorWallHit hit in collectorWalls)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "      WallId={0}   collector-to-wall-centerline 2D distance={1:F4}m   (Wall.Width/2={2:F4}m)   {3}",
                    hit.WallId.Value, hit.Distance2DMeters, hit.HalfWidthMeters,
                    hit.WithinBasicRule ? "<-- MATCHES FindCollectorWallPenetration rule" : "(NOT WallKind.Basic - current code would ignore)"));
                sb.AppendLine(DescribeWallCached(doc, hit.WallId, wallProfileCache, roomsByWallId, "        "));
            }
        }

        List<long> collectorWallKeys = collectorWalls.Select(h => h.WallId.Value).ToList();
        List<ElementId> extraNearby = nearbyContextWalls.Where(w => !collectorWallKeys.Contains(w.Value)).ToList();
        if (extraNearby.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  --- (A') Other walls within {0:F2}m of the collector (context - corner neighbours, through-walls) ---",
                NearbyContextRadiusMeters));
            foreach (ElementId wallId in extraNearby)
            {
                sb.AppendLine($"      WallId={wallId.Value}");
                sb.AppendLine(DescribeWallCached(doc, wallId, wallProfileCache, roomsByWallId, "        "));
            }
        }

        sb.AppendLine();

        // --- (B) קיר/י החסימה של המסלול הישר - ray-casting אמיתי ---
        List<ElementId> blockingWalls = FindAllStraightBlockingWalls(wallRayCasting, fixturePoint, collectorPoint, levelId, hostWallId);

        sb.AppendLine("  --- (B) Wall(s) BLOCKING the straight fixture->collector route (real ray-casting, all crossings peeled) ---");
        if (blockingWalls.Count == 0)
        {
            sb.AppendLine("      NONE - the straight route is clear (this route is a simple STRAIGHT pipe, no penetration question).");
        }
        else
        {
            var collectorWallSet = new HashSet<long>(collectorWalls.Where(h => h.WithinBasicRule).Select(h => h.WallId.Value));
            foreach (ElementId wallId in blockingWalls)
            {
                bool isCollectorWall = collectorWallSet.Contains(wallId.Value);
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "      WallId={0}   {1}",
                    wallId.Value,
                    isCollectorWall
                        ? "*** this IS a collector-containing wall -> CollectorWallPenetration WOULD allow the last segment through it ***"
                        : "--- NOT a collector-containing wall -> CollectorWallPenetration would NOT apply; this wall stays a hard obstruction ---"));
                sb.AppendLine(DescribeWallCached(doc, wallId, wallProfileCache, roomsByWallId, "        "));
            }
        }

        sb.AppendLine();
    }

    /// <summary>
    /// זיהוי גיאומטרי-דינאמי של הקירות שגוף-הקיר שלהם מכיל את
    /// <paramref name="collectorPoint"/> - מרחק 2D מקו-המיקום ≤
    /// <c>Wall.Width/2 + <see cref="CollectorWallContainmentToleranceMeters"/></c>.
    /// שדה <see cref="CollectorWallHit.WithinBasicRule"/> מסמן אם הקיר
    /// גם <c>WallKind.Basic</c> (התנאי המדויק של <c>FindCollectorWallPenetration</c>).
    /// </summary>
    private static List<CollectorWallHit> FindCollectorContainingWalls(Document doc, ElementId levelId, XYZ collectorPoint)
    {
        double toleranceFeet = UnitUtils.ConvertToInternalUnits(CollectorWallContainmentToleranceMeters, UnitTypeId.Meters);
        var hits = new List<CollectorWallHit>();

        foreach (Wall wall in WallsOnLevel(doc, levelId))
        {
            if (wall.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line line)
            {
                continue;
            }

            double widthFeet;
            try
            {
                widthFeet = wall.Width;
            }
            catch (Exception)
            {
                continue;
            }

            IntersectionResult? projection = line.Project(collectorPoint);
            if (projection is null)
            {
                continue;
            }

            XYZ foot = projection.XYZPoint;
            double dx = foot.X - collectorPoint.X;
            double dy = foot.Y - collectorPoint.Y;
            double distanceFeet = Math.Sqrt((dx * dx) + (dy * dy));

            if (distanceFeet <= (widthFeet / 2.0) + toleranceFeet)
            {
                bool isBasic = false;
                try
                {
                    isBasic = wall.WallType?.Kind == WallKind.Basic;
                }
                catch (Exception)
                {
                }

                hits.Add(new CollectorWallHit(
                    wall.Id,
                    UnitUtils.ConvertFromInternalUnits(distanceFeet, UnitTypeId.Meters),
                    UnitUtils.ConvertFromInternalUnits(widthFeet / 2.0, UnitTypeId.Meters),
                    isBasic));
            }
        }

        return hits.OrderBy(h => h.Distance2DMeters).ToList();
    }

    /// <summary>קירות (כל סוג) שקו-המיקום שלהם עובר בתוך <paramref name="radiusMeters"/> מ-<paramref name="point"/> - הקשר בלבד.</summary>
    private static List<ElementId> FindNearbyWalls(Document doc, ElementId levelId, XYZ point, double radiusMeters)
    {
        double radiusFeet = UnitUtils.ConvertToInternalUnits(radiusMeters, UnitTypeId.Meters);
        var result = new List<ElementId>();

        foreach (Wall wall in WallsOnLevel(doc, levelId))
        {
            if (wall.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line line)
            {
                continue;
            }

            IntersectionResult? projection = line.Project(point);
            if (projection is null)
            {
                continue;
            }

            XYZ foot = projection.XYZPoint;
            double dx = foot.X - point.X;
            double dy = foot.Y - point.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) <= radiusFeet)
            {
                result.Add(wall.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// קולף קיר-אחרי-קיר מהקו הישר: מריץ <see cref="WallRayCasting.FindBlockingWallDetailed"/>,
    /// מוסיף את הקיר שנמצא לרשימת-ההחרגה (דרך <c>penetrableWallIds</c> עם
    /// מרחק אינסופי = החרגה מלאה), וחוזר - עד <see cref="MaxBlockingWallsPerRoute"/>
    /// או עד שאין עוד חסימות. כך מתקבלת רשימת **כל** הקירות שהקו הישר חוצה.
    /// </summary>
    private static List<ElementId> FindAllStraightBlockingWalls(
        WallRayCasting wallRayCasting, XYZ from, XYZ to, ElementId levelId, ElementId? hostWallId)
    {
        var excluded = new HashSet<ElementId>();
        if (hostWallId is not null)
        {
            excluded.Add(hostWallId);
        }

        var blockers = new List<ElementId>();
        for (int i = 0; i < MaxBlockingWallsPerRoute; i++)
        {
            WallRayCasting.BlockingWallHit? hit = wallRayCasting.FindBlockingWallDetailed(
                from, to, levelId, null, null, excluded, double.MaxValue);

            if (hit is null)
            {
                break;
            }

            blockers.Add(hit.Value.WallId);
            excluded.Add(hit.Value.WallId);
        }

        return blockers;
    }

    private static IEnumerable<Wall> WallsOnLevel(Document doc, ElementId levelId)
    {
        var filter = new LogicalAndFilter(
            new ElementCategoryFilter(BuiltInCategory.OST_Walls),
            new ElementLevelFilter(levelId));

        return new FilteredElementCollector(doc)
            .WherePasses(filter)
            .WhereElementIsNotElementType()
            .OfType<Wall>();
    }

    /// <summary>
    /// בונה מיפוי <c>WallId -&gt; רשימת "מספר-חדר (שם-חדר)"</c> מכל
    /// ה-Rooms התחומים במסמך (<c>Area &gt; 0</c>), פעם אחת להרצה. משמש
    /// לשדה "bounds rooms" בפרופיל-הקיר.
    /// </summary>
    private static Dictionary<long, List<string>> BuildRoomsByWallId(Document doc)
    {
        var map = new Dictionary<long, List<string>>();
        var options = new SpatialElementBoundaryOptions();

        foreach (Room room in new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>())
        {
            double area;
            try
            {
                area = room.Area;
            }
            catch (Exception)
            {
                continue;
            }

            if (area <= 0)
            {
                continue;
            }

            IList<IList<BoundarySegment>>? loops;
            try
            {
                loops = room.GetBoundarySegments(options);
            }
            catch (Exception)
            {
                continue;
            }

            if (loops is null)
            {
                continue;
            }

            string label = $"{room.Number} ({room.Name})";

            foreach (IList<BoundarySegment> loop in loops)
            {
                foreach (BoundarySegment segment in loop)
                {
                    ElementId id = segment.ElementId;
                    if (id == ElementId.InvalidElementId || doc.GetElement(id) is not Wall)
                    {
                        continue;
                    }

                    if (!map.TryGetValue(id.Value, out List<string>? list))
                    {
                        list = new List<string>();
                        map[id.Value] = list;
                    }

                    if (!list.Contains(label))
                    {
                        list.Add(label);
                    }
                }
            }
        }

        return map;
    }

    private static string DescribeWallCached(
        Document doc,
        ElementId wallId,
        Dictionary<long, string> cache,
        Dictionary<long, List<string>> roomsByWallId,
        string indent)
    {
        if (!cache.TryGetValue(wallId.Value, out string? described))
        {
            described = DescribeWall(doc, wallId, roomsByWallId);
            cache[wallId.Value] = described;
        }

        return string.Join(Environment.NewLine, described.Split('\n').Select(line => indent + line.TrimEnd('\r')));
    }

    /// <summary>
    /// מוציא את כל נתוני-הסיווג של קיר בודד: WallType (Id+שם+Kind+Function),
    /// עובי, מבני (דגל + StructuralUsage), Room-bounding, וכל שכבות ה-
    /// CompoundStructure (Function + עובי + שם-חומר + MaterialClass) עם
    /// זיהוי שכבת-בטון. כל קריאה עטופה (<see cref="Safe"/>) - נתון שלא
    /// נקרא מדווח במפורש, לא נבלע.
    /// </summary>
    private static string DescribeWall(Document doc, ElementId wallId, Dictionary<long, List<string>> roomsByWallId)
    {
        if (doc.GetElement(wallId) is not Wall wall)
        {
            return $"WallId={wallId.Value}: element not found or not a Wall.";
        }

        var sb = new StringBuilder();
        WallType? wallType = wall.WallType;

        sb.AppendLine($"WallId={wallId.Value}");
        sb.AppendLine($"  WallType   : Id={Safe(() => wallType!.Id.Value.ToString(CultureInfo.InvariantCulture))}  Name=\"{Safe(() => wallType!.Name)}\"");
        sb.AppendLine($"  Kind       : {Safe(() => wallType!.Kind.ToString())}");
        sb.AppendLine($"  Function   : {Safe(() => DescribeWallFunction(wallType!))}");
        sb.AppendLine($"  Width      : {Safe(() => $"{UnitUtils.ConvertFromInternalUnits(wall.Width, UnitTypeId.Meters):F4}m")}");
        sb.AppendLine($"  Structural (instance 'Structural' flag): {Safe(() => DescribeYesNo(wall, BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT))}");
        sb.AppendLine($"  StructuralUsage: {Safe(() => DescribeStructuralUsage(wall))}");
        sb.AppendLine($"  Room bounding: {Safe(() => DescribeYesNo(wall, BuiltInParameter.WALL_ATTR_ROOM_BOUNDING))}");

        List<string> rooms = roomsByWallId.GetValueOrDefault(wallId.Value) ?? new List<string>();
        sb.AppendLine($"  Bounds rooms: {(rooms.Count == 0 ? "(none found via Room.GetBoundarySegments)" : string.Join(" ; ", rooms))}");

        sb.AppendLine("  CompoundStructure layers:");
        sb.Append(Safe(() => DescribeCompoundStructure(doc, wallType!)));

        sb.AppendLine($"  WallPenetrationPolicy verdict: {Safe(() => DescribePenetrationVerdict(wall))}");

        return sb.ToString();
    }

    /// <summary>
    /// מריצה את <see cref="WallPenetrationPolicy.IsPenetrationAllowed"/>
    /// **האמיתי** (אותה הגדרה שהניתוב משתמש בה) על הקיר ומדפיסה את
    /// הפסיקה - כדי שאפשר יהיה לוודא, לפני הרצת "צייר צינורות", אילו
    /// קירות-קולטן יעברו את שער-ההרשאה ואילו יסומנו Manual Engineering.
    /// </summary>
    private static string DescribePenetrationVerdict(Wall wall)
    {
        bool allowed = WallPenetrationPolicy.IsPenetrationAllowed(wall, out string reason);
        return allowed
            ? $"ALLOWED (a collector in this wall may be reached through it) - {reason}"
            : $"FORBIDDEN (stays a hard obstruction -> a collector in this wall becomes Manual Engineering) - {reason}";
    }

    private static string DescribeWallFunction(WallType wallType)
    {
        Parameter? p = wallType.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
        if (p is null || !p.HasValue)
        {
            return "(FUNCTION_PARAM not available)";
        }

        int value = p.AsInteger();
        string name = value switch
        {
            0 => "Interior",
            1 => "Exterior",
            2 => "Foundation",
            3 => "Retaining",
            4 => "Soffit",
            5 => "Core-shaft",
            _ => "Unknown",
        };

        return $"{name} ({value})";
    }

    private static string DescribeYesNo(Element element, BuiltInParameter parameter)
    {
        Parameter? p = element.get_Parameter(parameter);
        if (p is null || !p.HasValue || p.StorageType != StorageType.Integer)
        {
            return "(parameter not available)";
        }

        return p.AsInteger() == 1 ? "YES" : "no";
    }

    private static string DescribeStructuralUsage(Wall wall)
    {
        Parameter? p = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
        if (p is null || !p.HasValue || p.StorageType != StorageType.Integer)
        {
            return "(WALL_STRUCTURAL_USAGE_PARAM not available)";
        }

        int value = p.AsInteger();
        string name = value switch
        {
            0 => "Non-bearing",
            1 => "Bearing",
            2 => "Shear",
            3 => "Structural combined (bearing + shear)",
            _ => "Unknown",
        };

        return $"{name} ({value})";
    }

    private static string DescribeCompoundStructure(Document doc, WallType wallType)
    {
        CompoundStructure? cs = wallType.GetCompoundStructure();
        if (cs is null)
        {
            return "    (no CompoundStructure on this WallType)" + Environment.NewLine;
        }

        IList<CompoundStructureLayer> layers = cs.GetLayers();
        if (layers.Count == 0)
        {
            return "    (CompoundStructure has 0 layers)" + Environment.NewLine;
        }

        var sb = new StringBuilder();
        bool anyConcrete = false;

        for (int i = 0; i < layers.Count; i++)
        {
            CompoundStructureLayer layer = layers[i];

            string materialName = "(no material)";
            string materialClass = "(n/a)";
            if (layer.MaterialId != ElementId.InvalidElementId && doc.GetElement(layer.MaterialId) is Material material)
            {
                materialName = material.Name;
                try
                {
                    materialClass = string.IsNullOrWhiteSpace(material.MaterialClass) ? "(empty)" : material.MaterialClass;
                }
                catch (Exception)
                {
                    materialClass = "(unreadable)";
                }
            }

            bool layerIsConcrete =
                materialName.Contains("concrete", StringComparison.OrdinalIgnoreCase)
                || materialName.Contains("בטון", StringComparison.Ordinal)
                || materialClass.Equals("Concrete", StringComparison.OrdinalIgnoreCase);
            anyConcrete |= layerIsConcrete;

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "    Layer {0}: function={1}  width={2:F4}m  material=\"{3}\"  materialClass={4}{5}",
                i + 1,
                layer.Function,
                UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Meters),
                materialName,
                materialClass,
                layerIsConcrete ? "  <-- CONCRETE" : string.Empty));
        }

        sb.AppendLine($"    => Contains a concrete layer: {(anyConcrete ? "YES" : "no")}");
        return sb.ToString();
    }

    /// <summary>עוטפת קריאת-נתון - מחזירה "(not available: ...)" במקום לזרוק/להיבלע.</summary>
    private static string Safe(Func<string> read)
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

    /// <summary>קיר שמכיל את מיקום הקולטן - ראו <see cref="FindCollectorContainingWalls"/>.</summary>
    private readonly record struct CollectorWallHit(
        ElementId WallId,
        double Distance2DMeters,
        double HalfWidthMeters,
        bool WithinBasicRule);
}
