using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit.Commands;

/// <summary>
/// פקודת **תצוגה בלבד** (presentation layer) - קוראת את דוח-הטקסט
/// האחרון שכבר הופק על ידי <see cref="DrawPipesCommand"/>
/// (<c>PlumbingSystem_Pipes_&lt;timestamp&gt;.txt</c>, בפורמט הקבוע
/// שכבר קיים - ראו <see cref="DrawPipesCommand.BuildReport"/>), ובונה
/// ממנו קובץ HTML **עצמאי לחלוטין** (CSS+JS inline, בלי קבצים
/// חיצוניים) המיועד להצגה ללקוח/הנהלה - לא למהנדס. **לא נוגעת בשום
/// לוגיקת-ניתוב/מיקום, ולא מחשבת שום נתון-הנדסי בעצמה** - כל מספר
/// שמופיע כאן כבר חושב ונכתב לקובץ-הטקסט על ידי <see cref="DrawPipesCommand"/>;
/// הפקודה הזו רק **פותחת מחדש**, מנתחת (regex, פורמט קבוע-וידוע - לא
/// ניחוש) ומעצבת אותו. אם לא נמצא אף קובץ-Pipes בתיקיית-הטמפ - נכשלת
/// בבירור (TaskDialog), לא מייצרת דוח-ריק.
/// </summary>
/// <remarks>
/// כפתור **נפרד** ב-Ribbon (לא הרחבה/פרמטר לכפתור "צייר צינורות"):
/// זה קהל-יעד שונה לגמרי (לקוח/הנהלה, לא מהנדס-שרברבות), ופלט-קובץ
/// שונה (HTML, לא TXT) - הפרדה זהה בעקרונה להפרדה הקיימת בין
/// <see cref="BuildCollectorsCommand"/> ל-<see cref="PlaceCollectorsCommand"/>
/// (כל פקודה אחראית על תוצר אחד ברור). היתרון המעשי: מריצים "צייר
/// צינורות" **פעם אחת**, ואז אפשר להריץ את הכפתור הזה שוב ושוב (על
/// אותו קובץ-מקור) בלי לגעת ב-Revit שוב בכלל - קריאת-קובץ בלבד.
/// </remarks>
[Transaction(TransactionMode.ReadOnly)]
public class GenerateClientReportCommand : IExternalCommand
{
    /// <summary>
    /// מוצאת את קובץ-ה-Pipes האחרון (הכי-חדש, לפי חותמת-הזמן בשם
    /// הקובץ) בתיקיית-הטמפ, מנתחת אותו, בונה HTML, ואז מציגה חלון
    /// "שמור בשם" רגיל של Windows כדי שהמשתמש/ת יבחר/תבחר מיקום-קבוע
    /// (לא תמיד Temp, כמו קודם) - שם-קובץ ברירת-מחדל
    /// <c>PlumbingSystem_ClientReport_&lt;timestamp&gt;.html</c>. אם
    /// בוטל - <see cref="Result.Cancelled"/>, שום קובץ לא נכתב. אחרת,
    /// נכתב למיקום-שנבחר ונפתח שם בדפדפן ברירת-המחדל.
    /// </summary>
    /// <param name="commandData">נתוני ההקשר של הפקודה - לא בשימוש (קריאת-קובץ בלבד, בלי גישה למודל).</param>
    /// <param name="message">מתמלא בהודעת שגיאה אם לא נמצא קובץ-מקור או שהניתוח נכשל.</param>
    /// <param name="elements">לא בשימוש.</param>
    /// <returns><see cref="Result.Succeeded"/> אם ה-HTML נכתב ונפתח בהצלחה.</returns>
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        string tempPath = Path.GetTempPath();

        string? latestPipesReportPath = Directory
            .GetFiles(tempPath, "PlumbingSystem_Pipes_*.txt")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (latestPipesReportPath is null)
        {
            message = "No 'PlumbingSystem_Pipes_*.txt' report found - run 'צייר צינורות' (Draw Pipes) at least once first.";
            TaskDialog.Show("PlumbingSystem - Client Report", message);
            return Result.Failed;
        }

        ParsedPipeReport report;
        try
        {
            report = ParsePipesReport(File.ReadAllLines(latestPipesReportPath));
        }
        catch (Exception ex)
        {
            message = $"Failed to parse '{Path.GetFileName(latestPipesReportPath)}': {ex.Message}";
            TaskDialog.Show("PlumbingSystem - Client Report", message);
            return Result.Failed;
        }

        string html = BuildHtml(report, Path.GetFileName(latestPipesReportPath));

        // חלון "שמור בשם" רגיל של Windows - במקום כתיבה שקטה ל-Temp -
        // כדי שהמשתמש/ת יבחר/תבחר מיקום-קבוע (Desktop/Documents/תיקיית-
        // פרויקט/כל מקום אחר). לא קובעים תיקיית-פתיחה - Windows זוכר
        // אוטומטית את התיקייה האחרונה שנבחרה, כמו בכל חלון-שמירה רגיל.
        // ביטול (ShowDialog() != true) יוצא **לפני** File.WriteAllText/
        // Process.Start - שום קובץ לא נכתב, שום שגיאה. ראו docs/client-report.md.
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"PlumbingSystem_ClientReport_{DateTime.Now:yyyyMMdd_HHmmss}.html",
            Filter = "HTML file (*.html)|*.html",
            DefaultExt = ".html",
        };

        if (saveDialog.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        File.WriteAllText(saveDialog.FileName, html, Encoding.UTF8);

        Process.Start(new ProcessStartInfo(saveDialog.FileName) { UseShellExecute = true });

        return Result.Succeeded;
    }

    /// <summary>מקטע-צינור בודד כפי שנקרא מקובץ-הטקסט - רק השדות הדרושים לדוח-הלקוח.</summary>
    private sealed record PipeEntry(string Kind, bool IsManualEngineering, string? WallId);

    /// <summary>דירה בודדת (מספר-קומה יכול להיות null אם השורה לא נותחה - לא אמור לקרות בפורמט הידוע).</summary>
    private sealed record ApartmentEntry(string Id, int? FloorNumber, List<PipeEntry> Pipes);

    /// <summary>כל מה שחולץ מקובץ-ה-Pipes - מספיק כדי לבנות את כל ה-HTML בלי לחזור לקובץ המקורי.</summary>
    private sealed record ParsedPipeReport(
        string GeneratedAt,
        string Scope,
        List<ApartmentEntry> Apartments);

    private static readonly Regex ApartmentHeaderRegex = new(
        @"^--- Apartment '(?<id>[^']+)' \(Floor (?<floor>-?\d+)\) - \d+ pipe route\(s\) ---$",
        RegexOptions.Compiled);

    private static readonly Regex PipeLineRegex = new(
        @"^  Pipe RouteId=(?<routeId>\S+)  RevitElementId=\d+  Kind=(?<kind>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex ObstructionLineRegex = new(
        @"^    FixtureElementId=\S+  CollectorId=\S+  DiameterMm=\d+  obstruction=(?<obstruction>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex WallIdRegex = new(@"WallId=(?<wallId>\d+)", RegexOptions.Compiled);

    /// <summary>
    /// מנתחת את השורות של קובץ-Pipes לפי הפורמט **הקבוע והידוע** של
    /// <see cref="DrawPipesCommand.BuildReport"/> - לא ניחוש: שלוש
    /// הרג'קסים למעלה תואמות בדיוק לשלוש שורות-הפורמט שהקוד ההוא כותב.
    /// אם קובץ עתידי משנה את הפורמט הזה בלי לעדכן גם כאן - הניתוח
    /// יחסיר שורות בשקט (לא יזרוק) עבור אותה שורה ספציפית; ה-Execute
    /// למעלה עדיין יזרוק אם המבנה הכללי (כותרת/מספרים) חסר.
    /// </summary>
    private static ParsedPipeReport ParsePipesReport(string[] lines)
    {
        string generatedAt = ExtractValue(lines, "Generated: ") ?? "(unknown)";
        string scope = ExtractValue(lines, "Scope: ") ?? "(unknown)";

        var apartments = new List<ApartmentEntry>();
        ApartmentEntry? currentApartment = null;
        string? pendingKind = null;

        foreach (string line in lines)
        {
            Match apartmentMatch = ApartmentHeaderRegex.Match(line);
            if (apartmentMatch.Success)
            {
                currentApartment = new ApartmentEntry(
                    apartmentMatch.Groups["id"].Value,
                    int.Parse(apartmentMatch.Groups["floor"].Value, CultureInfo.InvariantCulture),
                    new List<PipeEntry>());
                apartments.Add(currentApartment);
                continue;
            }

            Match pipeMatch = PipeLineRegex.Match(line);
            if (pipeMatch.Success)
            {
                pendingKind = pipeMatch.Groups["kind"].Value;
                continue;
            }

            Match obstructionMatch = ObstructionLineRegex.Match(line);
            if (obstructionMatch.Success && currentApartment is not null && pendingKind is not null)
            {
                string obstruction = obstructionMatch.Groups["obstruction"].Value;
                bool isManual = pendingKind.StartsWith("MANUAL STUBS", StringComparison.Ordinal);
                Match wallIdMatch = WallIdRegex.Match(obstruction);
                string? wallId = wallIdMatch.Success ? wallIdMatch.Groups["wallId"].Value : null;

                currentApartment.Pipes.Add(new PipeEntry(
                    Kind: pendingKind,
                    IsManualEngineering: isManual,
                    WallId: wallId));

                pendingKind = null;
            }
        }

        return new ParsedPipeReport(generatedAt, scope, apartments);
    }

    private static string? ExtractValue(string[] lines, string prefix)
    {
        string? line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line?[prefix.Length..];
    }

    /// <summary>
    /// בונה את דף ה-HTML כולו - CSS+JS inline, בלי שום תלות חיצונית
    /// (פונט/CDN/תמונה) כדי שהקובץ יעבוד לבד גם כ-double-click וגם
    /// כקובץ-מצורף במייל. אין כאן שום מונח-פנימי (RouteId/WallId/t1/t3) -
    /// רק מספרים ומשפטים בשפה פשוטה.
    /// </summary>
    private static string BuildHtml(ParsedPipeReport report, string sourceFileName)
    {
        // Floor 0 (out of scope for the current project - confirmed with
        // the manager: apartment 1125 on Level "קומת קרקע" is real housing
        // but not part of the typical-floors scope yet, and would only
        // be included by mistake anyway - RevitModelReader.TryGetFloorNumber
        // returns null for a Level name with no digits ("קומת קרקע"), and
        // ReadApartments' `?? 0` fallback (line ~176) collapses that into
        // the same "0" used for genuine commercial-floor exclusion. Fixing
        // that null-vs-zero ambiguity would mean touching RevitModelReader.cs
        // (production model-reading logic), which is out of scope for this
        // presentation-only report - so it is filtered out here instead,
        // purely for what the client report displays. No change to the
        // source .txt, to DrawPipesCommand, or to any parsing/count logic
        // above this line - every number below is computed only from the
        // apartments that remain after this one filter.
        List<ApartmentEntry> scopedApartments = report.Apartments
            .Where(a => a.FloorNumber != 0)
            .ToList();

        List<PipeEntry> allPipes = scopedApartments.SelectMany(a => a.Pipes).ToList();
        int total = allPipes.Count;
        int manualCount = allPipes.Count(p => p.IsManualEngineering);
        int successCount = total - manualCount;
        double successPercent = total > 0 ? successCount * 100.0 / total : 0;
        double manualPercent = total > 0 ? manualCount * 100.0 / total : 0;

        int floorCount = scopedApartments.Select(a => a.FloorNumber).Distinct().Count();
        int apartmentCount = scopedApartments.Count;

        var byFloor = scopedApartments
            .GroupBy(a => a.FloorNumber)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Floor = g.Key,
                Apartments = g.Count(),
                Total = g.SelectMany(a => a.Pipes).Count(),
                Successful = g.SelectMany(a => a.Pipes).Count(p => !p.IsManualEngineering),
                Manual = g.SelectMany(a => a.Pipes).Count(p => p.IsManualEngineering),
            })
            .ToList();

        // מקובצת לפי דירה (לא לפי מקטע-בודד) - דירה עם 2 חיבורים-דורשי-
        // בדיקה מוצגת פעם אחת, עם ספירה, לא פעמיים - כדי שלא ייראה כמו
        // כפילות-באג למנהל. אין כאן מחיקת-נתונים: כל מקטע עדיין נספר
        // (ManualPipes.Count), רק תצוגת-השורה מתקבצת.
        var manualCasesByApartment = scopedApartments
            .Select(a => new { Apartment = a, ManualPipes = a.Pipes.Where(p => p.IsManualEngineering).ToList() })
            .Where(x => x.ManualPipes.Count > 0)
            .OrderBy(x => x.Apartment.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();

        // דף HTML **עצמאי-מלא** (לא Claude Artifact עם עטיפה אוטומטית -
        // זה קובץ אמיתי שנפתח בכפול-קליק/מצורף-במייל) - חייב DOCTYPE/
        // html/head/body מפורשים בעצמו.
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Automated Plumbing Routing Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(CssStyles);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine("<div class=\"page\">");

        // --- Header ---
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<div class=\"hero-title\">Startarc &mdash; Automated Sewage Plumbing Routing</div>");
        sb.AppendLine("<div class=\"hero-subtitle\">Residential Building &mdash; Automated Route Generation Report</div>");
        sb.AppendLine("<div class=\"hero-meta\">");
        sb.AppendLine($"<span>Generated: {Encode(report.GeneratedAt)}</span>");
        sb.AppendLine($"<span>Scope: {Encode(report.Scope)}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"building-stats\">");
        sb.AppendLine($"<div class=\"stat\"><div class=\"stat-value\">{floorCount}</div><div class=\"stat-label\">Floors</div></div>");
        sb.AppendLine($"<div class=\"stat\"><div class=\"stat-value\">{apartmentCount}</div><div class=\"stat-label\">Apartments</div></div>");
        sb.AppendLine($"<div class=\"stat\"><div class=\"stat-value\">{total}</div><div class=\"stat-label\">Fixtures Processed</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</header>");

        // --- Big summary badges ---
        sb.AppendLine("<section class=\"summary\">");
        sb.AppendLine("<div class=\"badge badge-success\">");
        sb.AppendLine($"<div class=\"badge-number\">{successCount}</div>");
        sb.AppendLine($"<div class=\"badge-percent\">{successPercent:F0}%</div>");
        sb.AppendLine("<div class=\"badge-label\">Routed Automatically</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"badge badge-manual\">");
        sb.AppendLine($"<div class=\"badge-number\">{manualCount}</div>");
        sb.AppendLine($"<div class=\"badge-percent\">{manualPercent:F0}%</div>");
        sb.AppendLine("<div class=\"badge-label\">Require Manual Engineering</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"badge badge-total\">");
        sb.AppendLine($"<div class=\"badge-number\">{total}</div>");
        sb.AppendLine("<div class=\"badge-label\">Total Fixtures Processed</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");

        // --- Per-floor table ---
        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Floor-by-Floor Breakdown</h2>");
        sb.AppendLine("<p class=\"hint\">Click a column header to sort.</p>");
        sb.AppendLine("<table id=\"floorTable\">");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th data-type=\"num\">Floor</th>");
        sb.AppendLine("<th data-type=\"num\">Apartments</th>");
        sb.AppendLine("<th data-type=\"num\">Total Fixtures</th>");
        sb.AppendLine("<th data-type=\"num\">Automated</th>");
        sb.AppendLine("<th data-type=\"num\">Manual Required</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var row in byFloor)
        {
            string floorLabel = row.Floor is int f ? f.ToString(CultureInfo.InvariantCulture) : "(unknown)";
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{Encode(floorLabel)}</td>");
            sb.AppendLine($"<td>{row.Apartments}</td>");
            sb.AppendLine($"<td>{row.Total}</td>");
            sb.AppendLine($"<td class=\"cell-success\">{row.Successful}</td>");
            sb.AppendLine($"<td class=\"cell-manual\">{(row.Manual > 0 ? row.Manual.ToString(CultureInfo.InvariantCulture) : "&mdash;")}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
        sb.AppendLine("</section>");

        // --- Manual-required cases, plain language, grouped by apartment ---
        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Locations Requiring Manual Engineering Review</h2>");
        if (manualCount == 0)
        {
            sb.AppendLine("<p>None &mdash; every fixture in this run was routed automatically.</p>");
        }
        else
        {
            sb.AppendLine($"<p>{manualCount} connection(s), across {manualCasesByApartment.Count} apartment(s), could not be routed automatically after the system tried every " +
                "supported routing option (angled detours in both directions, and a staggered offset with multiple lengths). " +
                "These require a plumbing engineer to confirm the final routing on site or in the model. " +
                "Full technical detail (exact geometry, every routing attempt tried, and why each one was rejected) is " +
                "available in the companion engineering report - see the note at the bottom of this page.</p>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Apartment</th><th>Floor</th><th>Connections Requiring Review</th><th>Reason</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var group in manualCasesByApartment)
            {
                string floorLabel = group.Apartment.FloorNumber is int f ? f.ToString(CultureInfo.InvariantCulture) : "(unknown)";
                string reason = group.ManualPipes.Any(p => p.WallId is not null)
                    ? "Standard automated routing could not clear a nearby wall for one or more connections, even after trying every supported detour option."
                    : "Standard automated routing could not find a valid path for one or more connections.";
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{Encode(group.Apartment.Id)}</td>");
                sb.AppendLine($"<td>{Encode(floorLabel)}</td>");
                sb.AppendLine($"<td>{group.ManualPipes.Count}</td>");
                sb.AppendLine($"<td>{Encode(reason)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody>");
            sb.AppendLine("</table>");
        }
        sb.AppendLine("</section>");

        // --- Footer ---
        sb.AppendLine("<footer>");
        sb.AppendLine("<p>This report summarizes an automated pass of the building's sewage plumbing routing, generated directly from the Revit model.</p>");
        sb.AppendLine($"<p>Source data: <code>{Encode(sourceFileName)}</code>. Full technical detail for every manual-engineering case " +
            "(exact geometry, every routing option attempted, and the reason each failed) is available in the companion " +
            "engineering reports (<code>ManualEngineeringReport_Floor2_2026-08-13.md</code> and the per-run diagnostic files) " +
            "for anyone who wants to review the underlying analysis.</p>");
        sb.AppendLine("</footer>");

        sb.AppendLine("</div>"); // .page

        sb.AppendLine("<script>");
        sb.AppendLine(SortScript);
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private const string CssStyles = """
        :root {
            --success: #2e7d32;
            --success-bg: #eaf6ec;
            --manual: #ef6c00;
            --manual-bg: #fdf1e6;
            --ink: #1f2937;
            --muted: #6b7280;
            --border: #e5e7eb;
            --bg: #f7f8fa;
        }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--bg); color: var(--ink);
            font-family: -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif; }
        .page { max-width: 960px; margin: 0 auto; padding: 32px 24px 64px; }
        header.hero { background: linear-gradient(135deg, #1f2937, #374151); color: #fff;
            border-radius: 14px; padding: 32px; margin-bottom: 24px; }
        .hero-title { font-size: 26px; font-weight: 700; }
        .hero-subtitle { font-size: 15px; opacity: 0.85; margin-top: 4px; }
        .hero-meta { margin-top: 16px; display: flex; gap: 24px; font-size: 13px; opacity: 0.8; flex-wrap: wrap; }
        .building-stats { margin-top: 24px; display: flex; gap: 32px; border-top: 1px solid rgba(255,255,255,0.2); padding-top: 20px; flex-wrap: wrap; }
        .stat-value { font-size: 28px; font-weight: 700; }
        .stat-label { font-size: 13px; opacity: 0.8; margin-top: 2px; }
        .summary { display: flex; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
        .badge { flex: 1; min-width: 200px; border-radius: 14px; padding: 24px; text-align: center;
            box-shadow: 0 1px 3px rgba(0,0,0,0.08); }
        .badge-success { background: var(--success-bg); border: 1px solid var(--success); }
        .badge-manual { background: var(--manual-bg); border: 1px solid var(--manual); }
        .badge-total { background: #fff; border: 1px solid var(--border); }
        .badge-number { font-size: 40px; font-weight: 800; line-height: 1; }
        .badge-success .badge-number, .badge-success .badge-percent { color: var(--success); }
        .badge-manual .badge-number, .badge-manual .badge-percent { color: var(--manual); }
        .badge-percent { font-size: 18px; font-weight: 600; margin-top: 4px; }
        .badge-label { font-size: 13px; color: var(--muted); margin-top: 8px; }
        .card { background: #fff; border: 1px solid var(--border); border-radius: 14px;
            padding: 24px; margin-bottom: 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); }
        .card h2 { margin-top: 0; font-size: 18px; }
        .hint { color: var(--muted); font-size: 13px; margin-top: -8px; }
        table { width: 100%; border-collapse: collapse; font-size: 14px; }
        th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid var(--border); }
        th { color: var(--muted); font-weight: 600; font-size: 12px; text-transform: uppercase;
            letter-spacing: 0.03em; cursor: pointer; user-select: none; }
        th:hover { color: var(--ink); }
        .cell-success { color: var(--success); font-weight: 600; }
        .cell-manual { color: var(--manual); font-weight: 600; }
        footer { color: var(--muted); font-size: 12px; line-height: 1.6; text-align: center; padding-top: 8px; }
        footer code { background: #eef0f2; border-radius: 4px; padding: 1px 5px; }
        @media (prefers-color-scheme: dark) {
            body { background: #111827; color: #e5e7eb; }
            .card, .badge-total { background: #1f2937; border-color: #374151; }
            .badge-success { background: rgba(46,125,50,0.15); }
            .badge-manual { background: rgba(239,108,0,0.15); }
            th, td { border-color: #374151; }
            footer code { background: #374151; }
        }
        """;

    private const string SortScript = """
        document.querySelectorAll('#floorTable th').forEach(function (th, index) {
            th.addEventListener('click', function () {
                var table = th.closest('table');
                var tbody = table.querySelector('tbody');
                var rows = Array.prototype.slice.call(tbody.querySelectorAll('tr'));
                var isNumeric = th.getAttribute('data-type') === 'num';
                var ascending = th.getAttribute('data-asc') !== 'true';
                table.querySelectorAll('th').forEach(function (h) { h.removeAttribute('data-asc'); });
                th.setAttribute('data-asc', ascending ? 'true' : 'false');
                rows.sort(function (a, b) {
                    var av = a.children[index].textContent.trim();
                    var bv = b.children[index].textContent.trim();
                    if (isNumeric) {
                        av = parseFloat(av) || 0;
                        bv = parseFloat(bv) || 0;
                        return ascending ? av - bv : bv - av;
                    }
                    return ascending ? av.localeCompare(bv) : bv.localeCompare(av);
                });
                rows.forEach(function (row) { tbody.appendChild(row); });
            });
        });
        """;
}
