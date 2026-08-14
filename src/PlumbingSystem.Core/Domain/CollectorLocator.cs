using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;

namespace PlumbingSystem.Core.Domain;

/// <summary>
/// מאתר את מיקומי הקולטן/ים הנדרשים לדירה אחת, לפי הכללים המאושרים:
/// כל אסלה חייבת להיות במרחק אופקי (2D) של עד <see cref="MaxDistanceMeters"/>
/// מקולטן כלשהו; אם קולטן בודד לא מכסה את כל האסלות, נדרשים קולטנים
/// נוספים. זו לוגיקה עסקית טהורה (אין כאן RevitAPI) - מיקום הקולטן
/// שמוחזר כאן הוא **מיקום האסלה** שממנה הוא "נגזר" (לא נקודה חדשה
/// על הקיר) - זהו מיקום-ביניים, לפני-הצמדה, לא המיקום ההנדסי הסופי.
/// **עדכון**: ה-TODO המקורי כאן (מיקום מדויק "צמוד לקצה קיר") **מומש
/// בפועל בשלב 6** - ראו <c>PlumbingSystem.Core.Domain.WallEdgeSnapper.SnapToNearestWallEdge</c>
/// (לוגיקת ההצמדה עצמה) ו-<c>PlumbingSystem.Revit.CollectorPlacementService.SnapToNearestWallEdge</c>
/// (הקורא, ברמת Revit, שמזין לה קירות אמיתיים) - זה לא TODO פתוח יותר.
/// שימו לב: ההצמדה הנוכחית ממקמת את הקולטן **בדיוק על** קואורדינטת
/// קצה-הקיר (מרווח-קלירנס 0), לא במרחק-ביטחון ממנו - זו התנהגות ידועה,
/// לא באג-סמוי, אך היא זו שגורמת בפועל לחלק מהמקרים ב-`MANUAL ENGINEERING
/// REQUIRED` בשלב 7 (ראו docs/step7.md והדוח `reports/ManualEngineeringReport_Floor2_2026-08-13.md`).
/// </summary>
public static class CollectorLocator
{
    /// <summary>
    /// מרחק אופקי מקסימלי (מטרים) בין אסלה לקולטן שלה - הגבול העליון
    /// האמיתי מתוך טווח 3-4 מ' (rules.md), נגזר מעובי מילוי פיזי
    /// (17-18 ס"מ) בפרויקט הזה. זו לא "תקרה רכה": בניגוד לקוטר צנרת
    /// (שאפשר להגדיל), עובי המילוי הוא קבוע גיאומטרי של הבניין - אין
    /// שום פתרון חלופי לחריגה ממנו מלבד קולטן נוסף.
    /// </summary>
    public const double MaxDistanceMeters = 4.0;

    /// <summary>
    /// מרחק *מועדף* (מטרים), לא תקרה נפרדת - הערך שהאלגוריתם שואף אליו
    /// מתוך הטווח התקין (3-4 מ') כשיש כמה פתרונות תקינים באותה מידה
    /// (אותו מספר קולטנים מינימלי). ה-Preference הזה ממומש ב-
    /// <see cref="Locate"/> על ידי מזעור סכום המרחקים בין כל אסלה
    /// לקולטן שלה, במקום לקבל כל מרחק בטווח כשווה-ערך.
    /// </summary>
    public const double PreferredDistanceMeters = 3.0;

    /// <summary>
    /// קידומת קבועה ל-Id של כל קולטן (למשל <c>"COL-5283771"</c>) - קבוע
    /// ציבורי (לא רק מחרוזת מוטבעת) כדי ש-<c>PlumbingSystem.Revit</c>
    /// (למשל בזיהוי/מחיקת קולטנים ישנים לפני יצירת חדשים) יוכל להתאים
    /// לזיהוי הזה בלי לשכפל את המחרוזת במקום שני.
    /// </summary>
    public const string CollectorIdPrefix = "COL-";

    /// <summary>
    /// מאתרת את קולטני הדירה. מבצעת חיפוש **ממצה** (לא greedy) על כל
    /// תת-הקבוצות האפשריות של אסלות-כמועמדות-למיקום-קולטן, החל מגודל
    /// 1 ומעלה: ברגע שנמצא הגודל המינימלי שבו קיימת תת-קבוצה שמכסה את
    /// כל האסלות (כל אסלה במרחק <see cref="MaxDistanceMeters"/> ממועמד
    /// כלשהו בתת-הקבוצה), נאספות *כל* תת-הקבוצות בגודל הזה שמכסות הכל,
    /// ומביניהן נבחרת זו שממזערת את סכום מרחקי ההקצאה (כל אסלה לקולטן
    /// הקרוב אליה ביותר בתוך התת-קבוצה). חיפוש ממצה ריאלי חישובית כאן
    /// כי גודל הבעיה חסום הנדסית (עד כ-26 אסלות לדירה, לפי חוק 8: 160
    /// FU / 6 FU לאסלה) - גם C(26,k) לכל k סביר רץ מיידית.
    /// </summary>
    /// <param name="apartment">הדירה שעבורה מאתרים קולטנים.</param>
    /// <returns>
    /// רשימת <see cref="CollectorPoint"/> - קולטן אחד לכל מועמד שנבחר,
    /// עם כל האסלות שהוקצו אליו ב-<see cref="CollectorPoint.ConnectedFixtureIds"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="apartment"/> הוא null.</exception>
    /// <exception cref="InvalidOperationException">
    /// אם באף אסלה בדירה לא מסומן <see cref="ToiletFixture.IsGuestBathroom"/> -
    /// זה לא אמור לקרות (כל דירה עם לפחות אסלה אחת אמורה להכיל בדיוק
    /// שירותים בודדים אחד, לפי שלב 4), ועדיף לזרוק שגיאה ברורה מאשר
    /// לנחש נקודת התחלה. שימו לב: שירותים בודדים הוא רק *מועמד* טבעי
    /// שתמיד קיים בתוך החיפוש הממצה (כמו כל אסלה אחרת) - הוא לא נבחר
    /// אוטומטית אם מועמד אחר ממזער טוב יותר את סכום המרחקים.
    /// </exception>
    public static List<CollectorPoint> Locate(Apartment apartment)
    {
        ArgumentNullException.ThrowIfNull(apartment);

        List<ToiletFixture> fixtures = apartment.Fixtures;

        if (!fixtures.Any(f => f.IsGuestBathroom))
        {
            throw new InvalidOperationException(
                $"דירה '{apartment.Id}' לא מכילה אף אסלה עם IsGuestBathroom=true - " +
                "אין נקודת התחלה מומלצת לאיתור קולטנים.");
        }

        int fixtureCount = fixtures.Count;

        for (int candidateSetSize = 1; candidateSetSize <= fixtureCount; candidateSetSize++)
        {
            List<int[]> coveringCombinations = GenerateIndexCombinations(fixtureCount, candidateSetSize)
                .Where(combination => Covers(combination, fixtures))
                .ToList();

            if (coveringCombinations.Count == 0)
            {
                continue;
            }

            int[] bestCombination = coveringCombinations
                .OrderBy(combination => TotalAssignmentDistance(combination, fixtures))
                .First();

            return BuildCollectorPoints(apartment, bestCombination, fixtures);
        }

        // בלתי-אפשרי במעשה: גודל == fixtureCount תמיד מכסה (כל אסלה מכסה
        // את עצמה במרחק 0), אז הלולאה תמיד מוצאת פתרון לפני שהיא מסתיימת.
        throw new InvalidOperationException(
            $"לא נמצא אף פתרון שמכסה את כל האסלות בדירה '{apartment.Id}' - מצב בלתי אפשרי.");
    }

    /// <summary>בודקת אם תת-הקבוצה (לפי אינדקסים) מכסה את כל האסלות בדירה.</summary>
    private static bool Covers(int[] candidateIndices, List<ToiletFixture> fixtures)
    {
        foreach (ToiletFixture fixture in fixtures)
        {
            bool isCovered = candidateIndices.Any(candidateIndex =>
                GeometryUtils.Distance2D(fixture.Location, fixtures[candidateIndex].Location) <= MaxDistanceMeters);

            if (!isCovered)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>סכום המרחקים מכל אסלה לקולטן (מועמד) הקרוב אליה ביותר בתת-הקבוצה.</summary>
    private static double TotalAssignmentDistance(int[] candidateIndices, List<ToiletFixture> fixtures)
    {
        double total = 0;

        foreach (ToiletFixture fixture in fixtures)
        {
            total += candidateIndices.Min(candidateIndex =>
                GeometryUtils.Distance2D(fixture.Location, fixtures[candidateIndex].Location));
        }

        return total;
    }

    /// <summary>
    /// בונה <see cref="CollectorPoint"/> לכל מועמד שנבחר, ומקצה כל אסלה
    /// (כולל מועמדים אחרים) לקולטן הקרוב אליה ביותר מבין הנבחרים.
    /// </summary>
    private static List<CollectorPoint> BuildCollectorPoints(
        Apartment apartment,
        int[] candidateIndices,
        List<ToiletFixture> fixtures)
    {
        Dictionary<int, List<string>> assignedFixtureIdsByCandidate =
            candidateIndices.ToDictionary(index => index, _ => new List<string>());

        foreach (ToiletFixture fixture in fixtures)
        {
            int nearestCandidateIndex = candidateIndices
                .OrderBy(candidateIndex => GeometryUtils.Distance2D(fixture.Location, fixtures[candidateIndex].Location))
                .First();

            assignedFixtureIdsByCandidate[nearestCandidateIndex].Add(fixture.Id);
        }

        var collectors = new List<CollectorPoint>();

        foreach (int candidateIndex in candidateIndices)
        {
            ToiletFixture candidateFixture = fixtures[candidateIndex];

            collectors.Add(new CollectorPoint(
                id: $"{CollectorIdPrefix}{candidateFixture.Id}",
                location: candidateFixture.Location,
                connectedApartmentIds: new List<string> { apartment.Id },
                connectedFixtureIds: assignedFixtureIdsByCandidate[candidateIndex]));
        }

        return collectors;
    }

    /// <summary>מייצרת את כל השילובים (ללא סדר, ללא חזרות) של <paramref name="size"/> אינדקסים מתוך 0..<paramref name="n"/>-1.</summary>
    private static IEnumerable<int[]> GenerateIndexCombinations(int n, int size)
    {
        int[] current = new int[size];
        return Generate(0, 0);

        IEnumerable<int[]> Generate(int start, int depth)
        {
            if (depth == size)
            {
                yield return (int[])current.Clone();
                yield break;
            }

            for (int i = start; i < n; i++)
            {
                current[depth] = i;
                foreach (int[] combination in Generate(i + 1, depth + 1))
                {
                    yield return combination;
                }
            }
        }
    }
}
