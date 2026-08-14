using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using PlumbingSystem.Core.Domain;
using PlumbingSystem.Core.Geometry;
using PlumbingSystem.Core.Models;
using PlumbingSystem.Revit.Config;

namespace PlumbingSystem.Revit;

/// <summary>
/// קורא נתונים אמיתיים מהמסמך הפעיל של Revit ובונה מהם את מודל
/// הדומיין (<see cref="Apartment"/>/<see cref="ToiletFixture"/> מ-
/// PlumbingSystem.Core). זו השכבה היחידה שמכירה גם את RevitAPI וגם
/// את Core - כל לוגיקת ההחלטה הטהורה (בחירת שירותים בודדים מתוך
/// מרחקים) חיה ב-Core וניתנת לבדיקה שם (<see cref="GuestBathroomSelector"/>);
/// המחלקה הזו אוספת נתונים מ-Revit (FamilyInstance, Room, Level) *וגם*
/// מחשבת את המרחק בפועל בין כל זוג (אסלה, אלמנט רטוב) - כולל בדיקת
/// חסימת קיר (<c>WallRayCasting.FindBlockingWall</c>, שהופרדה
/// לשם ב-שלב 7 כדי לשמש גם את בדיקת-החסימה של צינורות - ראו
/// docs/step7.md) שחייבת RevitAPI (<see cref="ReferenceIntersector"/>)
/// ולכן לא יכולה לשבת ב-Core. הבדיקה
/// מחריגה במפורש את ה-Host wall של כל אלמנט (הקיר שעליו הוא מותקן,
/// רלוונטי לאסלות "wall_hung") - בלי זה, כל אלמנט תלוי-קיר היה "פוגע"
/// מיידית בקיר המארח שלו עצמו בכל כיוון, ונראה חסום מכל דבר אחר תמיד.
/// אומת ידנית מול המודל (דירה '1133', WallId=5074867) שהבדיקה מזהה
/// קירות אמיתיים ולא ארטיפקטים. אין כאן שום unit test כי אי אפשר להריץ
/// את המחלקה הזו בלי Revit בפועל.
/// </summary>
public class RevitModelReader
{
    private readonly Document _document;
    private readonly List<string> _warnings = new();
    private readonly Dictionary<string, List<string>> _selectionExplanations = new();
    private readonly WallRayCasting _wallRayCasting;

    /// <summary>יוצר קורא לנתוני המסמך הנתון.</summary>
    public RevitModelReader(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _wallRayCasting = new WallRayCasting(document);
    }

    /// <summary>
    /// אזהרות שנאספו במהלך <see cref="ReadApartments"/> האחרונה - למשל
    /// אסלות בלי LocationPoint, או בלי Room ("No Room found").
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// הסבר קריא-לאדם, לכל דירה (מפתח = Apartment.Id), על המרחקים
    /// שהובילו לבחירת השירותים הבודדים - כולל אילו זוגות נחסמו על ידי
    /// קיר, ובאיזה ElementId של קיר - למטרות אימות ידני בדוח.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SelectionExplanations =>
        _selectionExplanations.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);

    /// <summary>
    /// קוראת את כל האסלות ששם המשפחה שלהן ב-<see cref="FixtureFamilyNames.ToiletFamilyNames"/>
    /// מהמסמך הפעיל, מקבצת אותן לפי Room.Number (=Apartment.Id), ולכל דירה מזהה איזו
    /// אסלה היא "שירותים בודדים" לפי מרחק מהאלמנטים הרטובים באותו Room -
    /// תוך התעלמות מזוגות שקיר חוסם ביניהם (ראו <c>WallRayCasting.FindBlockingWall</c>).
    /// </summary>
    /// <returns>רשימת הדירות שנמצאו, כל אחת עם האסלות ששייכות אליה.</returns>
    /// <exception cref="System.InvalidOperationException">
    /// מועברת הלאה מ-<see cref="GuestBathroomSelector.Select"/> אם דירה
    /// מסוימת נמצאת במצב שלא אמור לקרות (0 אלמנטים רטובים "פנויים" עם
    /// יותר מאסלה אחת, או תיקו במרחק המקסימלי) - ראו שם. גם מועלית אם
    /// אין אף View3D במסמך (נדרש לבדיקת חסימת הקיר).
    /// </exception>
    public List<Apartment> ReadApartments()
    {
        _warnings.Clear();
        _selectionExplanations.Clear();

        List<FixtureWithRoom> toiletsWithRoom = CollectFixturesWithRoom(
            fi => fi.Symbol?.Family?.Name is string familyName
                && FixtureFamilyNames.ToiletFamilyNames.Contains(familyName),
            "Toilet");

        List<FixtureWithRoom> wetFixturesWithRoom = CollectFixturesWithRoom(
            fi => FixtureFamilyNames.WetFixtureKeywords.Any(keyword =>
                (fi.Symbol?.Family?.Name ?? string.Empty).Contains(keyword, StringComparison.Ordinal)),
            "Wet fixture");

        var apartments = new List<Apartment>();

        foreach (IGrouping<string, FixtureWithRoom> group in toiletsWithRoom.GroupBy(t => t.RoomNumber))
        {
            string apartmentId = group.Key;
            List<FixtureWithRoom> toiletsInApartment = group.ToList();

            List<FixtureWithRoom> wetFixturesInApartment = wetFixturesWithRoom
                .Where(w => w.RoomNumber == apartmentId)
                .ToList();

            var toiletsWithDistances = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>();
            var blockedDetailsByToiletId = new Dictionary<string, List<(string WetFixtureId, long BlockingWallId)>>();

            foreach (FixtureWithRoom toilet in toiletsInApartment)
            {
                var distances = new List<double>(wetFixturesInApartment.Count);
                var blockedDetails = new List<(string WetFixtureId, long BlockingWallId)>();

                foreach (FixtureWithRoom wet in wetFixturesInApartment)
                {
                    double aerialDistance = GeometryUtils.Distance2D(toilet.Location, wet.Location);

                    ElementId? blockingWallId = _wallRayCasting.FindBlockingWall(
                        RevitUnitConversion.ToRevitPoint(toilet.Location),
                        RevitUnitConversion.ToRevitPoint(wet.Location),
                        toilet.Element.LevelId,
                        toilet.Element.Host?.Id,
                        wet.Element.Host?.Id);

                    if (blockingWallId is not null)
                    {
                        blockedDetails.Add((wet.Id, blockingWallId.Value));
                        // זוג חסום-קיר לא נחשב "קרוב", גם אם המרחק האווירי
                        // שלו קטן - ראו התיעוד על GuestBathroomSelector.
                        distances.Add(double.MaxValue);
                    }
                    else
                    {
                        distances.Add(aerialDistance);
                    }
                }

                toiletsWithDistances.Add((toilet.Id, distances));
                blockedDetailsByToiletId[toilet.Id] = blockedDetails;
            }

            IReadOnlyList<GuestBathroomSelector.Result> selection =
                GuestBathroomSelector.Select(toiletsWithDistances);

            RecordSelectionExplanation(apartmentId, selection, wetFixturesInApartment.Count, blockedDetailsByToiletId);

            Dictionary<string, GuestBathroomSelector.Result> selectionById =
                selection.ToDictionary(r => r.FixtureId);

            List<ToiletFixture> fixtures = toiletsInApartment
                .Select(t => new ToiletFixture(
                    id: t.Id,
                    location: t.Location,
                    apartmentId: apartmentId,
                    isGuestBathroom: selectionById[t.Id].IsGuestBathroom))
                .ToList();

            int floorNumber = TryGetFloorNumber(toiletsInApartment[0].Element) ?? 0;

            apartments.Add(new Apartment(apartmentId, floorNumber, fixtures));
        }

        return apartments;
    }

    /// <summary>
    /// שורת מידע פנימית: אלמנט Revit + מיקומו ב-Core (<see cref="Point3D"/>)
    /// + מספר ה-Room שבו הוא נמצא. משמש גם לאסלות וגם לאלמנטים רטובים,
    /// כי איסוף+בדיקת Room זהה לשניהם.
    /// </summary>
    private readonly record struct FixtureWithRoom(FamilyInstance Element, string Id, Point3D Location, string RoomNumber);

    /// <summary>
    /// אוספת FamilyInstance-ים מ-OST_PlumbingFixtures שעונים על
    /// <paramref name="familyPredicate"/>, ומצרפת לכל אחד את ה-Room
    /// שבנקודת מיקומו. אלמנטים בלי LocationPoint או בלי Room נכנסים
    /// ל-<see cref="Warnings"/> ולא נכללים בתוצאה - לפי הדרישה לדלג
    /// עליהם ולא לנחש.
    /// </summary>
    private List<FixtureWithRoom> CollectFixturesWithRoom(
        Func<FamilyInstance, bool> familyPredicate,
        string logDescription)
    {
        List<FamilyInstance> instances = new FilteredElementCollector(_document)
            .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Where(familyPredicate)
            .ToList();

        var result = new List<FixtureWithRoom>();

        foreach (FamilyInstance instance in instances)
        {
            XYZ? point = (instance.Location as LocationPoint)?.Point;
            if (point is null)
            {
                _warnings.Add($"{logDescription} ElementId={instance.Id.Value} has no LocationPoint - skipped.");
                continue;
            }

            Room? room = _document.GetRoomAtPoint(point);
            if (room is null)
            {
                _warnings.Add($"{logDescription} ElementId={instance.Id.Value} has no Room (\"No Room found\") - skipped.");
                continue;
            }

            result.Add(new FixtureWithRoom(instance, instance.Id.Value.ToString(), RevitUnitConversion.ToCorePoint(point), room.Number));
        }

        return result;
    }

    /// <summary>
    /// ניסיון להפוך את שם ה-Level (חופשי לגמרי, למשל "קומה 2" או
    /// "Level 2") למספר קומה. מחזיר null אם אין ספרות בשם - עדיף
    /// null מפורש על ניחוש שגוי.
    /// </summary>
    private int? TryGetFloorNumber(Element element)
    {
        if (_document.GetElement(element.LevelId) is not Level level)
        {
            return null;
        }

        string digitsOnly = new(level.Name.Where(char.IsDigit).ToArray());
        return int.TryParse(digitsOnly, out int floorNumber) ? floorNumber : null;
    }

    private void RecordSelectionExplanation(
        string apartmentId,
        IReadOnlyList<GuestBathroomSelector.Result> selection,
        int totalWetFixturesInApartment,
        IReadOnlyDictionary<string, List<(string WetFixtureId, long BlockingWallId)>> blockedDetailsByToiletId)
    {
        var lines = new List<string>();

        if (selection.Count == 1)
        {
            lines.Add($"Only one toilet (ElementId={selection[0].FixtureId}) - automatically the guest bathroom (no comparison needed).");
        }
        else
        {
            foreach (GuestBathroomSelector.Result result in selection.OrderByDescending(r => r.MinDistanceToWetFixture))
            {
                string marker = result.IsGuestBathroom ? "GUEST BATHROOM (max)" : "regular";
                List<(string WetFixtureId, long BlockingWallId)> blockedDetails =
                    blockedDetailsByToiletId.GetValueOrDefault(result.FixtureId) ?? new();

                string blockedNote = blockedDetails.Count > 0
                    ? " (" + string.Join("; ", blockedDetails.Select(b =>
                        $"wet-fixture {b.WetFixtureId} blocked by WallId={b.BlockingWallId}")) + ")"
                    : string.Empty;

                string distanceText = result.MinDistanceToWetFixture == double.MaxValue
                    ? $"no reachable wet fixture (all {totalWetFixturesInApartment} blocked by walls)"
                    : $"{result.MinDistanceToWetFixture:F4}";

                lines.Add(
                    $"Toilet ElementId={result.FixtureId}: min distance to nearest wet fixture = " +
                    $"{distanceText} -> {marker}{blockedNote}");
            }
        }

        _selectionExplanations[apartmentId] = lines;
    }
}
