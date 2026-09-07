# PIPE Step 5 - מסלולים ישרים כ-Revit Pipe אמיתי

**סטטוס: הקוד נכתב ומתקמפל מול RevitAPI 2027. 131/131 בדיקות עוברות.
ההרצה בפועל ב-Revit (`Pipe.Create` במודל חי) טרם בוצעה - ראו "בדיקות".**

מסמך זה מתעד את **PIPE Step 5**: העברת **המסלולים הישרים התקינים בלבד**
מיצירת `DirectShape` ליצירת `Autodesk.Revit.DB.Plumbing.Pipe` אמיתי.

---

## 1. מה השתנה, ומה נשאר DirectShape

השינוי הוא **בשכבת יצירת האלמנט ב-Revit בלבד**. `PipeRouteCalculator`
וחוקי ה-routing לא נגעו (ראו §7).

לכל route, לפי השדות שכבר בפלט של `DrawPipesCommand.BuildRoute` (בלי שום
חישוב חדש):

| route | תנאי | מה נוצר |
|---|---|---|
| **STRAIGHT תקין** | `!RequiresManualEngineering` **וגם** `segments.Count == 1` **וגם** `BlockingWallId is null` | **`Pipe` אמיתי** (`OST_PipeCurves`) — חדש ב-Step 5 |
| **OBSTRUCTED** | `segments.Count == 1` אבל `BlockingWallId != null` | `DirectShape` (כחול) — ללא שינוי |
| **DETOUR** | `segments.Count == 2`, לא-manual | `DirectShape` (כחול) — ללא שינוי |
| **STAGGERED Y** | `segments.Count == 3` | `DirectShape` (כחול) — ללא שינוי |
| **MANUAL_ENGINEERING** | `RequiresManualEngineering == true` | `DirectShape` (כתום) + `TextNote` — ללא שינוי |

מסלול ישר-אך-חסום (`OBSTRUCTED`) **נשאר DirectShape** - הוא לא עבר את
החסימה ואין מסלול מאומת. אין ליצור עבורו `Pipe` "אמיתי" שחוצה קיר.

**Fittings / Elbows / Connectors לא נוצרים** - לכן כל מסלול מרובה-מקטעים
נשאר DirectShape. זה **PIPE Step 6**.

---

## 2. ה-data flow החדש

```
DrawPipesCommand.Execute
  │  PipingOfficeSettings.Load().SewerPipeDiameterRequirement           (Step 4.1)
  │  PipeModelReadinessInspector.Evaluate(doc, requirement)             (פעם אחת, לפני ה-Transaction)
  │    → readiness.IsReady == false  →  TaskDialog(Report) + Result.Failed   (בלי לצייר כלום)
  │    → readiness.IsReady == true   →  { SelectedDiameterMm, SanitarySystemTypeName, MatchingPipeTypeName }
  │  FindSanitarySystemTypeId / FindPipeTypeId  →  systemTypeId, pipeTypeId
  │  מפת floorNumber → Level  (fallback: activeView.GenLevel, ואז הנמוך)
  ▼
  לכל route:  BuildRoute(...)  →  (segments, requiresManualEngineering, blockingWallId, ...)   [ללא שינוי]
  ▼
  isCleanStraightRoute ?
     כן  →  RealPipeFactory.Create(doc, systemTypeId, pipeTypeId, level.Id, segments[0], SelectedDiameterMm, routeId)
     לא  →  CreatePipeElement(...)  →  DirectShape   [ללא שינוי]
```

---

## 3. שער מוכנות-מודל

ב-`DrawPipesCommand.Execute`, **פעם אחת**, אחרי טעינת ה-`PipingOfficeSettings`
ולפני ה-`Transaction`:

- `PipeModelReadinessInspector.Evaluate(doc, diameterRequirement)`.
- `IsReady == false` → `TaskDialog` עם `readiness.Report` + `Result.Failed`.
  **לא נוצר שום דבר** (לא `Pipe`, לא `DirectShape`). אין fallback.
- `IsReady == true` → נשמרים `SelectedDiameterMm`, שם-ה-PipeType, שם-ה-SystemType,
  ומתורגמים ל-`ElementId` (`FindPipeTypeId` / `FindSanitarySystemTypeId`).
- לא רץ מחדש לכל route.

> **הערה חשובה (שינוי-התנהגות)**: השער חוסם את **כל** ההרצה אם המודל
> אינו מוכן, גם אם בפועל כל המסלולים היו יוצאים DETOUR/STAGGERED (DirectShape).
> זו הדרישה המפורשת של Step 5. במודל בלי `PipingSystemType` מסווג
> `Sanitary` או בלי Segment שעומד בדרישת-הקוטר - "צייר צינורות" ייעצר
> עד שמנהל-BIM יגדיר Segment מתאים (Manage → MEP Settings → Segments and Sizes).

---

## 4. PipeType / SystemType / קוטר / Level / Start-End

| מרכיב | מקור |
|---|---|
| `PipingSystemType` | `readiness.SanitarySystemTypeName` → `PipeModelReadinessInspector.FindSanitarySystemTypeId` (`SystemClassification == Sanitary`, ראשון בא"ב) |
| `PipeType` | `readiness.MatchingPipeTypeName` → `FindPipeTypeId` (ה-PipeType שכולל את הגודל הנבחר, ראשון בא"ב) |
| **קוטר** | `readiness.SelectedDiameterMm` — הגודל הקיים **הקטן ביותר** שעומד בדרישת-הקוטר של המשרד (`RBS_PIPE_DIAMETER_PARAM`). **לא** גבול-טווח, לא הדרישה הגולמית. |
| `levelId` | מפת `floorNumber → Level` לפי `apartment.FloorNumber` (אותו פענוח `RevitModelReader.TryGetFloorNumber` כמו בכל הזרימה); fallback: `activeView.GenLevel` ואז ה-Level הנמוך. |
| Start / End | `PipeSegment.StartPoint` / `.EndPoint` (Core → `RevitUnitConversion.ToRevitPoint`, מטרים→feet) |
| שיפוע / Z | **מהנקודות עצמן** — ה-`Z` של `EndPoint` כבר מגלם את הירידה שחישב `PipeRouteCalculator`. אין הגדרת פרמטר-שיפוע. |

**ה-Level אינו משנה את גובה הצינור** - הוא Reference Level בלבד; הגיאומטריה
נקבעת מ-`Pipe.Create(..., start, end)`.

---

## 5. זיהוי, מחיקה, Connection Inspector

**זהות**: ל-`Pipe` אמיתי אי-אפשר לקבוע `Element.Name` (הוא שם ה-`PipeType`).
לכן `RealPipeFactory` קובע `Comments` **וגם** `Mark` = `routeId`
(`PIPE-{fixtureId}-COL-{collectorId}` — **אותו פורמט**, לא שונה).

**`DrawPipesCommand.DeleteExistingPipes`** — סורק כעת שתי קטגוריות:
`OST_GenericModel` (DirectShape, כמו קודם) **וגם** `OST_PipeCurves`
(`Pipe` אמיתי). הזיהוי (`IsExistingPipe`) לפי `Comments`/`Mark` בקידומת
`PIPE-` — עובד לשני הסוגים. הרצה חוזרת מוחקת את שניהם, בלי כפילויות.
`OST_PipeFitting` לא נסרק (Fittings לא נוצרים עדיין).

**`ElementRelationshipLookup`** (Connection Inspector) — `AllPipes` סורק
כעת גם `OST_PipeCurves` (זיהוי לפי `Mark`/`Comments` "PIPE-", לא לפי
`Name`). `TryDescribe` מזהה `Pipe` אמיתי נבחר לפי קטגוריה + `Mark`.
`ParsePipe` נופל ל-`Comments` אם `Mark` חסר. הקשר `Pipe → Collector`
עובד ללא שינוי בפורמט ה-routeId.

---

## 6. מראה

`Pipe` אמיתי **לא** מקבל Material כחול/כתום ולא View Override — מקבל את
המראה של ה-`PipeType` של Revit. ה-DirectShapes (DETOUR/STAGGERED/OBSTRUCTED/MANUAL)
ממשיכים לקבל את הצבע/Override הקיים.

---

## 7. מה **לא** שונה

- **`PlumbingSystem.Core` - אפס שינוי.** `PipeRouteCalculator`,
  `PipeModelReadiness`, `PipeDiameterRequirement`, `PipeSegment`,
  `PipeRouteParameters` - לא נגעו (git diff מאשר).
- **חוקי ה-routing**: `BuildRoute` וכל העוזרים (`TryBuildDetour`,
  `TryBuildStaggeredDetour`, `BuildManualEngineeringStubs`,
  `FindObstructingWall`, `FindCollectorWallPenetration`, `GetWallSegment`),
  מפל-6-הניסיונות, חוק 4 מ', בדיקות-זווית, חישוב Z, `WallRayCasting`,
  `WallPenetrationPolicy` - **ללא שינוי**. Step 5 רק **צורך** את הפלט.
- זיהוי אסלות/קולטנים, התאמת אסלה→קולטן.
- `Transaction` / `RollBack` / Progress / `ScopeSelector` / error handling.
  כשל ב-`Pipe.Create` → `catch` הקיים → `tx.RollBack()` + `Result.Failed`,
  **בלי fallback ל-DirectShape**.
- Fittings / Elbows / Connectors - לא נוספו.

---

## 8. אילו קבצים שונו

| קובץ | שינוי |
|---|---|
| `src/PlumbingSystem.Revit/Mep/RealPipeFactory.cs` | **חדש** - `Create(...)`: `Pipe.Create` + קוטר + `Comments`/`Mark`. לא יוצר DirectShape. |
| `src/PlumbingSystem.Revit/Commands/DrawPipesCommand.cs` | שער-מוכנות ב-`Execute`; מפת `floorNumber→Level`; הסתעפות STRAIGHT-תקין → `RealPipeFactory`; `DrawnPipe` + `IsRealPipe`/`RealPipeDiameterMm`; `DeleteExistingPipes` סורק גם `OST_PipeCurves`; דוח: שורת `Element: Pipe/DirectShape` + קוטר-בפועל + מונה. `using RevitPipe = ...Plumbing.Pipe` (alias, למניעת התנגשות `PipeSegment`). |
| `src/PlumbingSystem.Revit/Inspector/ElementRelationshipLookup.cs` | `AllPipes` + `OST_PipeCurves`; `HasPipeRouteMark`; `TryDescribe` מזהה Pipe נבחר; `ParsePipe` נופל ל-Comments. |
| `docs/pipe-step5-*.md`, `docs/pipe-mep-investigation.md` | תיעוד. |

**לא שונה:** `GenerateClientReportCommand.cs` (הפרסר סובלני לשורות נוספות;
`Kind=` / `DiameterMm=\d+` / `obstruction=` נשמרו). `PipingOfficeSettings.cs`,
`PipeModelReadinessInspector.cs`, `CreateTestPipeCommand.cs` - נעשה בהם
שימוש-חוזר כמו-שהם.

---

## 9. בדיקות

### אוטומטי

| | תוצאה |
|---|---|
| `dotnet build PlumbingSystem.sln` | ✅ 0 errors, 0 CS warnings |
| `dotnet test` | ✅ **131 / 131** (ללא שינוי - Step 5 הוא שכבת-Revit, אין Core חדש לבדוק) |

**לא נוספו בדיקות-יחידה** - כל הלוגיקה הניתנת-לבדיקה (readiness, בחירת-גודל,
דרישת-קוטר) כבר מכוסה מ-Step 4/4.1; החדש ב-Step 5 הוא `Pipe.Create` וקטגוריות
Revit, שאין להם test host (CI בלי Revit).

### ידני (דורש Revit + מודל) - **טרם בוצע**

1. להריץ **"צייר צינורות"**.
2. לוודא שה-readiness עובר (אחרת - לתקן את המודל, Segment ביוב בטווח).
3. מסלול ישר תקין → נבחר ב-Revit → מזוהה כ-**Pipe** (Properties: קטגוריה Pipes), לא Generic Model.
4. הקוטר = `SelectedDiameterMm` (הדוח מציג `DiameterMm=` + `Element: Pipe`).
5. Start/End במקומות שחושבו; השיפוע תואם לגיאומטריה (הפרש Z).
6. DETOUR / STAGGERED / OBSTRUCTED / MANUAL - עדיין DirectShape, בדיוק כמו לפני Step 5.
7. הרצה חוזרת של "צייר צינורות" - ה-Pipes וה-DirectShapes הקודמים נמחקים, אין כפילויות.
8. Connection Inspector - בחירת Pipe אמיתי / הקולטן שלו → מציג את הקשר.
9. "דוח לקוח (HTML)" - אין regression.

---

## 10. מה נשאר ל-PIPE Step 6

- **Fittings / Elbows / Wye / Connectors** בנקודות-פנייה → מסלולי
  DETOUR / STAGGERED / Y כ-`Pipe` אמיתי מרובה-מקטעים. תלוי בהכרעת
  לוגיקת-הזווית (ראו `docs/pipe-mep-investigation.md` §8.2 - טרם הוכרעה).
- החלטה על `MANUAL_ENGINEERING` (נשאר DirectShape? - מומלץ כן).
- `OST_PipeFitting` ב-`DeleteExistingPipes` / Connection Inspector (כשיהיו Fittings).
- אולי: מראה/הכללה של `Pipe` אמיתי בדוח הלקוח (כרגע רק "Element: Pipe").
