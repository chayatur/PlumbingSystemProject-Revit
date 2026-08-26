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
/// פקודת אבחון **זמנית, ReadOnly** - לא חלק מהפיצ'ר הסופי, לא נוגעת בשום
/// אלמנט קיים ולא יוצרת חדשים. נועדה לבדוק שאלה שהניסוי הגיאומטרי-טהור
/// (Scratchpad "CollectorSetbackExperiment", בלי RevitAPI) לא הצליח לענות
/// עליה באמינות: אם מזיזים את הקולטן מרחק-הולך-וגדל **לאורך** הקיר החוסם
/// (הרחק מהפינה שאליה הוא צמוד היום), באיזה מרחק (אם בכלל) מסלול-עוקף
/// כלשהו (מתוך 28 הצירופים הרגילים) מפסיק להיחסם?
/// </summary>
/// <remarks>
/// **למה לא מספיק הניסוי ב-Scratchpad**: הניסוי הגיאומטרי-טהור מדמה את
/// הקיר החוסם כמלבן פשוט (קו-מרכז + עובי אמיתי) - אבל בדיקת-שפיות (השוואה
/// מול הדוח-האמיתי מ-2026-08-20) הוכיחה ש**כל 4** המקרים (B/C/D/E) נותנים
/// "עבר בהצלחה" שגוי במלבן הפשוט הזה כשבפועל Revit מדווח "עדיין חסום" -
/// בדיוק אותה מגבלה שכבר תועדה בחקירת 45°/90° (חיבור-קיר-שני בפינה
/// שהמלבן הפשוט לא מכיר). הפקודה הזו **לא בונה שום מודל-קיר בעצמה** - היא
/// קוראת ל-<see cref="WallRayCasting.FindBlockingWallDetailed"/> האמיתי
/// (אותה שיטת ray-casting בדיוק שמשמשת את "צייר צינורות" בפועל) על כל
/// מקטע של כל ניסיון - כך שקיר-שכן אמיתי בפינה **כן** יתגלה, כי הוא באמת
/// שם במודל, לא משנה אם הוא נכלל במלבן-ההנחה שלי או לא.
/// </remarks>
/// <remarks>
/// **מה נשאר בלי-שינוי**: לא נוגעת ב-<c>WallEdgeSnapper.cs</c>,
/// <c>PipeRouteCalculator.cs</c> או <c>DrawPipesCommand.cs</c> - קוראת
/// להם (Core: <see cref="PipeRouteCalculator.CalculateDetour"/>/
/// <see cref="PipeRouteCalculator.CalculateStaggeredDetour"/>) בלי לשנות
/// דבר בהם. מיקום-הקולטן "המוזז" הוא ערך-זמני-לבדיקה-בלבד - לא נכתב
/// לשום מקום, לא הופך לברירת-מחדל.
/// </remarks>
[Transaction(TransactionMode.ReadOnly)]
public class CollectorSetbackDiagnosticCommand : IExternalCommand
{
    /// <summary>
    /// ElementId-ים של 4 האסלות הידועות כ"מקרי-פינה" (corner proximity
    /// 0.0000m בדוח-האבחון האמיתי מ-2026-08-20) - מקרים B/C/D/E. מקרה A
    /// (דירה 1131) הוצא בכוונה - הכישלון שלו הוא מגבלת-אורך-מסלול
    /// (4.0 מ'), לא פינה, אז הזזת-קולטן-לאורך-קיר היא ניסוי לא-רלוונטי שם.
    /// </summary>
    private static readonly HashSet<string> TargetFixtureIds = new() { "5284278", "5295055", "5283870", "5283989" };

    /// <summary>זהה ל-<c>StaggeredCrossoverLengthCandidatesMeters</c> ב-DrawPipesCommand.cs - לא שוכפל בכוונה, רק הועתק (אין נגישות חוצת-מחלקות ל-private static שם).</summary>
    private static readonly double[] CrossoverLengthCandidatesMeters = { 0.10, 0.20, 0.30, 0.50, 0.75, 1.00 };

    /// <summary>
    /// מרחקי-היסט מועמדים (מטרים) - סדרה עולה, מפסיקה בהצלחה הראשונה,
    /// אותו עיקרון בדיוק כמו <c>StaggeredCrossoverLengthCandidatesMeters</c>.
    /// חורגת בכוונה הרבה מעבר ל-15% (הגבול המומלץ להחלטה בפועל) - כדי
    /// לדעת אם יש **בכלל** פתרון גיאומטרי, גם אם הוא ירוחק-מדי-מהפינה
    /// מכדי לאשר אותו בפועל.
    /// </summary>
    private static readonly double[] SetbackCandidatesMeters =
        { 0.02, 0.04, 0.06, 0.08, 0.10, 0.12, 0.14, 0.16, 0.18, 0.20, 0.25, 0.30, 0.35, 0.40, 0.50, 0.60, 0.70, 0.80, 1.00, 1.20, 1.50, 1.80 };

    private const double RecommendedCapPercent = 15.0;

    /// <summary>
    /// קוראת את המודל (ReadOnly), מאתרת את 4 מקרי-הפינה הידועים
    /// (<see cref="TargetFixtureIds"/>), ולכל אחד מריצה את חיפוש-ההיסט
    /// המדורג (<see cref="AppendCaseDiagnostic"/>) - כותבת דוח לקובץ טקסט
    /// שנפתח אוטומטית.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה, כולל המסמך הפעיל.</param>
    /// <param name="message">לא בשימוש - הפקודה מציגה TaskDialog בעצמה אם היא נכשלת.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> לאחר כתיבת הדוח ופתיחתו.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document doc = commandData.Application.ActiveUIDocument.Document;
        var reader = new RevitModelReader(doc);
        var wallRayCasting = new WallRayCasting(doc);
        var placementService = new CollectorPlacementService(doc);

        var sb = new StringBuilder();
        sb.AppendLine("=== PlumbingSystem - Collector Setback Diagnostic (temporary, READ-ONLY) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Uses REAL Revit ray-casting (WallRayCasting.FindBlockingWallDetailed) for every leg of every");
        sb.AppendLine("attempt - not a hand-modeled thick-wall rectangle - so any real second/adjacent wall at the");
        sb.AppendLine("corner is correctly seen, unlike the pure-geometry scratchpad experiment that preceded this.");
        sb.AppendLine("This command creates, deletes, and modifies NOTHING - it only reads and ray-casts.");
        sb.AppendLine();
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "15% recommended-cap reasoning: the hard rule is \"close to the edge of the wall, not the middle,\" " +
            "for any wall length. 15% of a typical short partition wall ({0}-{1}m here) is a small, still-at-the-" +
            "corner nudge while leaving 2x headroom before it would look central. The search below intentionally " +
            "goes PAST 15%, for information, to report whether a solution exists at all - even one that would be " +
            "rejected as too far into the wall.",
            "1.5", "4"));
        sb.AppendLine();

        List<Apartment> apartments;
        try
        {
            apartments = reader.ReadApartments();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TaskDialog.Show("PlumbingSystem - אבחון-היסט-קולטן נכשל", ex.Message);
            return Result.Failed;
        }

        int matchedCount = 0;
        foreach (Apartment apartment in apartments)
        {
            List<CollectorPoint> rawCollectors = CollectorLocator.Locate(apartment);

            foreach (ToiletFixture fixture in apartment.Fixtures.Where(f => TargetFixtureIds.Contains(f.Id)))
            {
                matchedCount++;
                CollectorPoint? rawCollector = rawCollectors.FirstOrDefault(c => c.ConnectedFixtureIds.Contains(fixture.Id));
                if (rawCollector is null)
                {
                    sb.AppendLine($"Fixture {fixture.Id}: no collector found via CollectorLocator - skipped.");
                    continue;
                }

                (CollectorPoint snappedCollector, _) = placementService.SnapToNearestWallEdge(rawCollector);
                AppendCaseDiagnostic(sb, doc, wallRayCasting, fixture, snappedCollector);
            }
        }

        if (matchedCount == 0)
        {
            sb.AppendLine("None of the 4 target fixture ElementIds (5284278, 5295055, 5283870, 5283989) were found in " +
                "the current model read - they may have been renumbered, or the model may have changed since " +
                "2026-08-20. Re-run \"Discover Model\" / \"Draw Pipes\" first to find the current corner-case fixture IDs.");
        }

        string path = Path.Combine(Path.GetTempPath(), $"PlumbingSystem_CollectorSetbackDiagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Result.Succeeded;
    }

    /// <summary>
    /// עבור אסלה אחת (אחד מ-4 מקרי-הפינה): מזהה את הקיר החוסם בפועל
    /// (ray-casting אמיתי על המסלול הישר), קובעת לאיזה קצה-קיר הקולטן
    /// צמוד היום (הפינה) והכיוון הרחק ממנה, ואז עוברת על
    /// <see cref="SetbackCandidatesMeters"/> - בכל היסט מריצה מחדש את כל
    /// 28 הצירופים (2 צדדים × 2 ייחוסים × (עוקף-דו-מקטעי + 6 אורכי-ביניים
    /// ל-Y-מדורג)), עם קולטן-מוזז **זמני** (לא נכתב לשום מקום), ובודקת
    /// כל מקטע דרך ray-casting אמיתי. עוצרת בהיסט-הראשון-שמצליח.
    /// </summary>
    private static void AppendCaseDiagnostic(
        StringBuilder sb,
        Document doc,
        WallRayCasting wallRayCasting,
        ToiletFixture fixture,
        CollectorPoint snappedCollector)
    {
        sb.AppendLine($"==========================================================================================");
        sb.AppendLine($"FixtureElementId={fixture.Id}  ApartmentId={fixture.ApartmentId}  CollectorId={snappedCollector.Id}");
        sb.AppendLine($"==========================================================================================");

        if (!long.TryParse(fixture.Id, out long fixtureIdValue) || doc.GetElement(new ElementId(fixtureIdValue)) is not FamilyInstance familyInstance)
        {
            sb.AppendLine("  Could not resolve FamilyInstance for this fixture Id - skipped.");
            sb.AppendLine();
            return;
        }

        XYZ straightFrom = RevitUnitConversion.ToRevitPoint(fixture.Location);
        XYZ straightTo = RevitUnitConversion.ToRevitPoint(snappedCollector.Location);
        WallRayCasting.BlockingWallHit? straightHit = wallRayCasting.FindBlockingWallDetailed(
            straightFrom, straightTo, familyInstance.LevelId, familyInstance.Host?.Id, null);

        if (straightHit is null)
        {
            sb.AppendLine("  Straight route is NOT currently blocked (unexpected for a known corner case - model may have changed). Skipped.");
            sb.AppendLine();
            return;
        }

        ElementId blockingWallId = straightHit.Value.WallId;
        if (doc.GetElement(blockingWallId) is not Wall wall || wall.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line wallLine)
        {
            sb.AppendLine($"  Blocking wall {blockingWallId.Value} is not a straight Line - cannot test setback along it. Skipped.");
            sb.AppendLine();
            return;
        }

        var wallSegment = new WallEdgeSnapper.WallSegment(
            blockingWallId.Value.ToString(CultureInfo.InvariantCulture),
            RevitUnitConversion.ToCorePoint(wallLine.GetEndPoint(0)),
            RevitUnitConversion.ToCorePoint(wallLine.GetEndPoint(1)));

        double distToStart = GeometryUtils.Distance2D(snappedCollector.Location, wallSegment.Start);
        double distToEnd = GeometryUtils.Distance2D(snappedCollector.Location, wallSegment.End);
        Point3D corner = distToStart <= distToEnd ? wallSegment.Start : wallSegment.End;
        Point3D awayEnd = distToStart <= distToEnd ? wallSegment.End : wallSegment.Start;
        double wallLength = GeometryUtils.Distance2D(wallSegment.Start, wallSegment.End);
        double unitX = (awayEnd.X - corner.X) / wallLength;
        double unitY = (awayEnd.Y - corner.Y) / wallLength;
        double cornerProximity = Math.Min(distToStart, distToEnd);

        sb.AppendLine($"  Blocking wall: {blockingWallId.Value}  from ({wallSegment.Start.X:F4},{wallSegment.Start.Y:F4}) to ({wallSegment.End.X:F4},{wallSegment.End.Y:F4})  length={wallLength:F4}m");
        sb.AppendLine($"  Collector today (setback=0): ({snappedCollector.Location.X:F4},{snappedCollector.Location.Y:F4})  corner proximity={cornerProximity:F4}m");
        sb.AppendLine($"  15% marker = {0.15 * wallLength * 100:F1}cm  |  50% (middle) = {0.50 * wallLength * 100:F1}cm");
        sb.AppendLine();

        (double Setback, string Description)? firstSolved = null;

        foreach (double setback in SetbackCandidatesMeters)
        {
            if (setback >= wallLength - 0.05)
            {
                break;
            }

            var shiftedLocation = new Point3D(corner.X + (setback * unitX), corner.Y + (setback * unitY), snappedCollector.Location.Z);
            var shiftedCollector = new CollectorPoint(snappedCollector.Id, shiftedLocation, snappedCollector.ConnectedApartmentIds, snappedCollector.ConnectedFixtureIds);

            (int cleared, int stillBlocked, int tooLong, int rejected, string? clearedDescription) = RunAllAttempts(
                doc, wallRayCasting, familyInstance, fixture, shiftedCollector, wallSegment);

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  setback={0,6:F1}cm ({1,5:F1}%):  cleared={2}  still-blocked={3}  too-long={4}  rejected={5}",
                setback * 100, setback / wallLength * 100, cleared, stillBlocked, tooLong, rejected));

            if (cleared > 0 && firstSolved is null && clearedDescription is not null)
            {
                firstSolved = (setback, clearedDescription);
                break; // stop at the first setback that solves it - gradual search, same principle as StaggeredCrossoverLengthCandidatesMeters.
            }
        }

        if (firstSolved is (double solvedSetback, string desc))
        {
            double pct = solvedSetback / wallLength * 100;
            string verdict = pct <= RecommendedCapPercent
                ? $"WITHIN the {RecommendedCapPercent:F0}% recommended cap"
                : $"EXCEEDS the {RecommendedCapPercent:F0}% recommended cap";
            sb.AppendLine();
            sb.AppendLine($"  ==> FIRST SOLVED (real ray-casting, all legs clear) at setback={solvedSetback * 100:F1}cm ({pct:F1}% of wall length) - {verdict}");
            sb.AppendLine($"      via: {desc}");
        }
        else
        {
            double maxSearched = SetbackCandidatesMeters.Where(s => s < wallLength - 0.05).DefaultIfEmpty(0).Max();
            sb.AppendLine();
            sb.AppendLine($"  ==> NOT SOLVED even at the max searched setback ({maxSearched * 100:F1}cm = {maxSearched / wallLength * 100:F1}% of wall length)");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// מריצה את כל 28 הצירופים (2-leg detour × 2 צדדים × 2 ייחוסים, +
    /// Y-מדורג × 6 אורכי-ביניים × 2 צדדים × 2 ייחוסים) על גיאומטריה נתונה
    /// (כולל <paramref name="collector"/> שיכול להיות מוזז-לבדיקה), בודקת
    /// כל מקטע דרך ray-casting אמיתי, ומחזירה ספירות + תיאור הניסיון
    /// הראשון שעבר (אם היה).
    /// </summary>
    private static (int Cleared, int StillBlocked, int TooLong, int Rejected, string? FirstClearedDescription) RunAllAttempts(
        Document doc,
        WallRayCasting wallRayCasting,
        FamilyInstance familyInstance,
        ToiletFixture fixture,
        CollectorPoint collector,
        WallEdgeSnapper.WallSegment wallSegment)
    {
        int cleared = 0, stillBlocked = 0, tooLong = 0, rejected = 0;
        string? firstClearedDescription = null;

        foreach (bool useWallRef in new[] { false, true })
        {
            string refLabel = useWallRef ? "wall direction" : "fixture-collector line (D)";

            foreach (bool oppositeSide in new[] { false, true })
            {
                string sideLabel = oppositeSide ? "opposite" : "preferred";

                Classify(
                    TryBuild(() => PipeRouteCalculator.CalculateDetour(fixture, collector, wallSegment, oppositeSide, useWallRef)),
                    $"2-leg detour | side={sideLabel} | reference={refLabel}");

                foreach (double crossover in CrossoverLengthCandidatesMeters)
                {
                    Classify(
                        TryBuild(() => PipeRouteCalculator.CalculateStaggeredDetour(fixture, collector, wallSegment, crossover, oppositeSide, useWallRef)),
                        $"Staggered-Y | crossover={crossover:F2}m | side={sideLabel} | reference={refLabel}");
                }
            }
        }

        return (cleared, stillBlocked, tooLong, rejected, firstClearedDescription);

        void Classify((IReadOnlyList<PipeSegment>? Segments, bool TooLong) result, string description)
        {
            if (result.Segments is null)
            {
                if (result.TooLong)
                {
                    tooLong++;
                }
                else
                {
                    rejected++;
                }

                return;
            }

            bool blocked = false;
            foreach (PipeSegment segment in result.Segments)
            {
                XYZ from = RevitUnitConversion.ToRevitPoint(segment.StartPoint);
                XYZ to = RevitUnitConversion.ToRevitPoint(segment.EndPoint);
                if (wallRayCasting.FindBlockingWallDetailed(from, to, familyInstance.LevelId, familyInstance.Host?.Id, null) is not null)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                stillBlocked++;
            }
            else
            {
                cleared++;
                firstClearedDescription ??= description;
            }
        }
    }

    /// <summary>
    /// עוטפת קריאה ל-<see cref="PipeRouteCalculator.CalculateDetour"/>/
    /// <see cref="PipeRouteCalculator.CalculateStaggeredDetour"/> ומתרגמת
    /// חריגה ל-null+דגל (במקום להפיל את כל הריצה) - מבחינה בין "יותר מדי
    /// ארוך" (הודעת-השגיאה המדויקת מ-<see cref="CollectorLocator.MaxDistanceMeters"/>)
    /// לבין "גיאומטריה לא-תקינה (אחורה)", אותו עיקרון בדיוק כמו
    /// <c>TryBuildDetour</c>/<c>TryBuildStaggeredDetour</c> ב-DrawPipesCommand.cs.
    /// </summary>
    private static (IReadOnlyList<PipeSegment>? Segments, bool TooLong) TryBuild(Func<IReadOnlyList<PipeSegment>> build)
    {
        try
        {
            return (build(), false);
        }
        catch (InvalidOperationException ex)
        {
            bool tooLong = ex.Message.Contains("חורג מהמגבלה הפיזית הקשיחה", StringComparison.Ordinal);
            return (null, tooLong);
        }
    }
}
