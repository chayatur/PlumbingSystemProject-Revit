# PIPE Step 4.1 - דרישת קוטר: מספר מדויק או טווח

**סטטוס: הושלם.** מנגנון דרישת/בחירת הקוטר בלבד. לא שונתה לוגיקת ניתוב,
לא בוצע `Pipe.Create` חדש מעבר לקיים ב-Step 4, ה-`DirectShape` וה-
visualization לא נגעו.

מסמך זה מרחיב את [pipe-step4-revit-pipe-creation.md](pipe-step4-revit-pipe-creation.md):
עד עכשיו STARTARC דרש **קוטר יחיד קשיח** (`Sewer pipe diameter mm = 110`).
כעת דרישת-הקוטר של המשרד יכולה להיות **מספר מדויק** או **טווח**.

---

## 1. למה עברנו מקוטר יחיד לתמיכה במספר/טווח

הדרישות המשרדיות לצנרת מגיעות בשתי צורות:

* **קוטר מדויק** - "אסלות מתחברות ל-Ø101.6 מ"מ".
* **טווח** - "אסלות: 100-110 מ"מ" (כל גודל בטווח מקובל).

הדוגמה שהובילה לשינוי: הדרישה המשרדית לאסלות היא **100-110 מ"מ**. במודל
Revit שנבדק קיים `PipeSegment` עם Nominal **4"**, שה-Revit API מחזיר
כ-**101.6 מ"מ**. `101.6` נמצא בתוך `100-110`, ולכן הוא עומד בדרישה - אבל
המנגנון הישן (קוטר יחיד `110`) היה פוסל אותו.

---

## 2. `4"` מול `101.6 מ"מ` - ולמה `4"` עומד בדרישה `100-110`

`4"` הוא **תווית-תצוגה** של Revit. הערך המספרי:

```
1 inch  ==  25.4 mm   (בהגדרה)
4 inch  ==  101.6 mm
```

STARTARC **לא** משווה לפי הטקסט המוצג (`"4\""`, `"4.0\""`, `"DN100"` וכו') -
הוא משווה לפי **הערך המספרי מ-Revit API, אחרי המרה למ"מ**:

```
Revit Nominal 4"
   │  MEPSize.NominalDiameter  (יחידות פנימיות של Revit = feet)
   ▼
UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters)
   ▼
101.6 mm   (בפועל 101.59999999999998 בגלל floating-point)
   ▼
PipeDiameterRequirement.Matches(101.6)  →  100 ≤ 101.6 ≤ 110  →  true
```

הקוד: [PipeModelReadinessInspector.ReadSupportedDiametersMm](../src/PlumbingSystem.Revit/Mep/PipeModelReadinessInspector.cs)
(שכבת Revit) → [PipeDiameterRequirement.Matches](../src/PlumbingSystem.Core/Domain/PipeDiameterRequirement.cs) (Core).

---

## 3. איך STARTARC ממיר ל-mm

בשכבת-Revit בלבד (`PipeModelReadinessInspector`):

```csharp
segment.GetSizes()
    .Select(size => UnitUtils.ConvertFromInternalUnits(size.NominalDiameter, UnitTypeId.Millimeters))
```

כל ההשוואה הלוגית ב-Core מתבצעת **במילימטרים** - Core לא מכיר feet, לא
מכיר את יחידות-התצוגה של המשתמש, ולא את RevitAPI.

---

## 4. איך מוגדר קוטר מדויק

בקובץ `PlumbingSystem.OfficeConfig.txt`:

```
Sewer pipe diameter mm = 101.6
```

פירוש: המודל חייב להכיל גודל-Segment של **בדיוק 101.6 מ"מ**.

**"בדיוק" זה בדיוק** - לא טווח הנדסי. הסבילות היחידה היא
`PipeDiameterRequirement.FloatToleranceMm = 1e-6` מ"מ (ננומטר), שנועדה
**רק** לספוג שגיאת-ביט מהמרת `feet → mm` של Revit (למשל `4"` שנקרא
כ-`101.59999999999998`). היא **אינה** גורמת ל-`101.6` להתאים ל-`100`,
`101.5`, `102` או `110`.

---

## 5. איך מוגדר טווח

```
Sewer pipe diameter mm = 100-110
```

פירוש: כל גודל-Segment קיים עם `100 ≤ size ≤ 110` עומד בדרישה (הקצוות
כלולים). המפריד הוא **מקף** (`-`), הגבול התחתון קודם.

* אותו מפתח (`Sewer pipe diameter mm`) - לא מפתח חדש מקביל.
* `Sewer pipe diameter mm = 110` (מספר יחיד) ממשיך לעבוד בדיוק כמו קודם -
  זו דרישה מדויקת. **תאימות לאחור מלאה.**
* טווח מנוון `100-100` = דרישה מדויקת.

---

## 6. איך נבחר גודל קיים, ומהו כלל-הבחירה כשיש כמה התאמות

[PipeModelReadiness.Evaluate](../src/PlumbingSystem.Core/Domain/PipeModelReadiness.cs)
אוסף את **כל** הזוגות `(PipeType, גודל)` שהגודל בהם עומד בדרישה, ואז:

> **כלל הבחירה (דטרמיניסטי, מתועד): הגודל הקיים הקטן ביותר שעומד
> בדרישה.** כשכמה `PipeType`-ים כוללים את הגודל שנבחר - הראשון בסדר א"ב.

הנימוק: לא לבחור קוטר גדול יותר מהנדרש ללא צורך.

**דוגמה** (טווח `100-160`):

| Revit Segment | בתוך `100-160`? |
|---|---|
| 76.2 | לא |
| 101.6 | **כן** |
| 127 | כן |
| 152.4 | כן |

הבחירה: **101.6** (הקטן ביותר מבין 101.6 / 127 / 152.4).

עבור **דרישה מדויקת** יש רק ערך אחד שמתאים (`101.6`), אז "הקטן ביותר" =
אותו ערך. כלל אחד, אחיד לשתי הצורות.

הגודל שנבחר מוחזר ב-`PipeModelReadinessResult.SelectedDiameterMm`, וזה
מה ש-`CreateTestPipeCommand` שם על ה-`Pipe` שנוצר
(`RBS_PIPE_DIAMETER_PARAM`) - **לא** גבול-הטווח, לא הדרישה הגולמית.

---

## 7. מה קורה כשאין התאמה - ולמה אין fallback

אם אף גודל-Segment קיים אינו עומד בדרישה (ו/או אין `PipingSystemType`
מסווג Sanitary):

```
PipeModelReadinessResult.IsReady = false
```

והדוח (`Report`, מוצג ב-`TaskDialog` ובקובץ) מפרט:

* מה הייתה הדרישה (`100-110 מ"מ (טווח)` / `101.6 מ"מ (מדויק)`).
* כל `PipeType` שנבדק + הקטרים שלו במ"מ, עם `✓` ליד גדלים שעומדים בדרישה.
* מדוע אף אחד לא עומד בדרישה (אין Sanitary / אין גודל מתאים).
* שיש להגדיר `PipeSegment` מתאים במודל / ב-Template המשרד
  (Manage → MEP Settings → Segments and Sizes).

**אין:**

* עיגול אוטומטי לגודל אחר.
* בחירת הגודל הקרוב ביותר.
* בחירת `PipeType` אחר עם קוטר שאינו מתאים.
* שינוי דרישת המשרד.
* יצירת Size חדש אוטומטית.
* שינוי הגדרות Revit.

הדרישה נקבעת במשרד; המודל צריך להיות מוגדר בהתאם. (ראו
[pipe-step1-revit-pipe-investigation.md](pipe-step1-revit-pipe-investigation.md) -
בשני המודלים שנבדקו שם אף `PipeType` לא תמך ב-110, כלומר `CreateTestPipeCommand`
ייעצר בשגיאת Model Readiness עד שיוגדר Segment.)

---

## 8. מה קורה כשמשרד אחר נותן מספר מדויק / טווח

זה בדיוק מה שהמנגנון הגנרי מטפל בו - **אין הבדל בקוד בין משרדים**, רק
בשורה בקובץ:

| המשרד כותב | המשמעות | הבחירה מהמודל |
|---|---|---|
| `Sewer pipe diameter mm = 110` | מדויק 110 | Segment של 110 מ"מ (בדיוק), אחרת - לא מוכן |
| `Sewer pipe diameter mm = 101.6` | מדויק 101.6 | Segment של 101.6 מ"מ (`4"`), אחרת - לא מוכן |
| `Sewer pipe diameter mm = 100-110` | טווח | הגודל הקטן ביותר ב-[100,110]; אם רק `4"` קיים → 101.6 |
| `Sewer pipe diameter mm = 100-160` | טווח רחב | הגודל הקטן ביותר ב-[100,160] |

---

## 9. דוגמאות מלאות מקובץ ההגדרות

```
# קוטר מדויק (תאימות לאחור - המצב ההיסטורי של STARTARC):
Sewer pipe diameter mm = 110

# קוטר מדויק אחר:
Sewer pipe diameter mm = 101.6

# טווח - הדרישה המשרדית לאסלות:
Sewer pipe diameter mm = 100-110

# שגיאות (כולן → OfficeConfigException עם הסבר, בלי fallback):
Sewer pipe diameter mm =            # ריק
Sewer pipe diameter mm = abc        # לא מספר / לא טווח
Sewer pipe diameter mm = 110-100    # min > max
Sewer pipe diameter mm = 1,75       # פסיק - יש להשתמש בנקודה
Sewer pipe diameter mm = 0          # לא חיובי
```

`Default slope percent` לא השתנה: מספר יחיד, `0 < x < 100`.

---

## 10. הארכיטקטורה - Core נשאר עצמאי

```
PlumbingSystem.OfficeConfig.txt
    │  "100-110"  (מחרוזת)
    ▼
OfficeConfig.GetSingleValue                     [שכבת Revit]
    ▼
PipingOfficeSettings.ParseDiameterRequirement   [שכבת Revit - עוטף FormatException ב-OfficeConfigException]
    ▼
PipeDiameterRequirement  (Exact / Range)        ← ייצוג-דומיין של Core (double-ים בלבד)
    ▼
PipeModelReadinessInspector.Evaluate(doc, req)  [שכבת Revit - שולף Segments/SystemTypes מ-Revit]
    ▼
PipeModelReadiness.Evaluate(req, names, sizes)  [Core טהור - כלל-הבחירה + הדוח]
    ▼
PipeModelReadinessResult  { IsReady, SelectedDiameterMm, Report, ... }
    ▼
CreateTestPipeCommand → Pipe.Create + RBS_PIPE_DIAMETER_PARAM = SelectedDiameterMm   [שכבת Revit]
```

* `PipeDiameterRequirement` + `PipeModelReadiness` יושבים ב-`PlumbingSystem.Core`,
  ללא שום `using` ל-RevitAPI / `OfficeConfig` / IO. מקבלים `double`-ים
  ומחרוזות-שם בלבד.
* `PlumbingSystem.Core.csproj` נשאר `net10.0` (לא `-windows`), בלי
  `Reference` ל-Revit. מתקמפל ונבדק בלי Revit מותקן.
* ההמרה `feet → mm` וההמרה `מחרוזת → PipeDiameterRequirement` קורות
  **בשכבת-Revit** (`PipeModelReadinessInspector` / `PipingOfficeSettings`).

---

## 11. בדיקות שבוצעו

### אוטומטי

| | תוצאה |
|---|---|
| `dotnet build PlumbingSystem.sln` | ✅ 0 errors, 0 CS warnings |
| `dotnet test` | ✅ **131 / 131** (היו 86 אחרי Step 4; +45) |

בדיקות-יחידה חדשות:

* **[PipeDiameterRequirementTests.cs](../tests/PlumbingSystem.Core.Tests/Domain/PipeDiameterRequirementTests.cs)** -
  ניתוח מחרוזת (מדויק / טווח / רווחים / טווח-מנוון), שגיאות פורמט
  (ריק, `abc`, `100-`, `-110`, `100--110`, `110-100`, `1,75`, `100,110`,
  `0`, `NaN`), `Matches` למדויק (כולל float-noise של `4"`) ולטווח (קצוות
  כלולים), `Describe`.
* **[PipeModelReadinessTests.cs](../tests/PlumbingSystem.Core.Tests/Domain/PipeModelReadinessTests.cs)** -
  10 התרחישים מהמפרט:

  | # | דרישה | Revit | תוצאה |
  |---|---|---|---|
  | 1 | מדויק 101.6 | 101.6 | PASS, נבחר 101.6 |
  | 2 | מדויק 101.6 | רק 110 | FAIL, בלי fallback |
  | 3 | טווח 100-110 | 101.6 | PASS |
  | 4 | טווח 100-110 | 88.9 | FAIL |
  | 5 | טווח 100-110 | 127 | FAIL |
  | 6 | טווח 100-160 | 101.6, 127, 152.4 | PASS, **נבחר 101.6** |
  | 7 | טווח 100-110 | שום התאמה | FAIL, הדוח מפרט מה נמצא |
  | 8 | הגדרה לא חוקית | - | `FormatException` (→ `OfficeConfigException` בשכבת Revit) |
  | 9 | פסיק עשרוני `1,75` / `100,110` | - | ההתנהגות הקיימת נשמרת: `OfficeConfig` דוחה ערך-פסיק לפני הפירוש |
  | 10 | `Sewer pipe diameter mm = 110` | 100/110/125 | PASS כדרישה מדויקת (תאימות לאחור) |

### פונקציונלי (מול קובץ ההגדרות האמיתי + `PipingOfficeSettings` + `PipeModelReadiness`)

הורץ ב-process נפרד לכל מקרה, מול `PlumbingSystem.OfficeConfig.txt` האמיתי:

| קלט בקובץ | תוצאה |
|---|---|
| `= 110` (ברירת מחדל) | `Exact 110`, `IsExact=True` ✅ |
| `= 101.6` | `Exact 101.6` ✅ |
| `= 100-110` | `Range [100,110]`, `Nominal=100` ✅ |
| `= abc` | `OfficeConfigException` עם הסבר + שורת-תיקון ✅ |
| `= 100,110` | `OfficeConfigException` (פסיק, דרך `GetSingleValue` - התנהגות קיימת) ✅ |
| `= 110-100` | `OfficeConfigException` "הגבול התחתון גדול מהעליון" ✅ |
| שורת-הקוטר הוסרה | `OfficeConfigException` "חסרה או ריקה" ✅ |
| **סעיף 13**: `= 100-110`, מודל עם `4" = 101.6` | **`IsReady = True`, `SelectedDiameterMm = 101.6`** ✅ |

### ידני (דורש Revit + מודל)

| Test | מה לבדוק |
|---|---|
| 1 | מודל עם Segment שעומד בדרישה → `Pipe.Create` מצליח, קוטר נכון (הרץ **"צינור-ניסוי (Step 4)"**). |
| 2 | שנה `Sewer pipe diameter mm` לערך אחר שנתמך → הצינור מקבל אותו. |
| 3 | קוטר/טווח שאין לו התאמה → `TaskDialog` "המודל אינו מוכן", `Result.Failed`, בלי יצירת צינור. (מכוסה גם אוטומטית ב-Core.) |
| 4 | הדוח מדפיס `Runtime type: Autodesk.Revit.DB.Plumbing.Pipe` ו-`Category: Pipes` - זה `Pipe` אמיתי, לא `DirectShape`. |
| **13** | `Sewer pipe diameter mm = 100-110` + מודל עם `PipeSegment` Nominal `4"` → **Model Readiness = READY**, הדוח/הדיאלוג מראים "גודל קיים שנבחר: 101.6 מ"מ". |

**חשוב (deployment)**: כדי שהגרסה החדשה תיטען, יש **לסגור ולפתוח מחדש את
Revit** (Revit נועל את ה-DLL-ים בתיקיית ה-Add-ins בזמן ריצה). ה-Target
`CopyAddinToRevit` עודכן שלא **להכשיל** את ה-build כש-Revit פתוח - מודפסת
אזהרה במקום שגיאה.

---

## 12. מה לא שונה בשלב הזה

* **לוגיקת ה-routing** - `PipeRouteCalculator`, straight / detours /
  staggered / wall-obstruction / collector logic / מגבלת 4 מ' / חישוב
  שיפוע / זוויות / `PipeSegment` geometry - **לא נגעו.**
* **`DrawPipesCommand`** - הקובץ לא שונה. ה-`double SewerPipeDiameterMm`
  נשאר על `PipingOfficeSettings` (מחזיר את `NominalMm` = הקוטר המדויק, או
  קצה-תחתון של טווח) כדי שהחוליה של PIPE Step 3
  (`PipeRouteParameters.DiameterMm` → `DirectShape`) תמשיך לעבוד ללא שינוי.
* **`DirectShape` / visualization / Materials / overrides** - לא נגעו.
* **`DeleteExistingPipes`** - לא נגע (`CreateTestPipeCommand` מנקה את
  צינורות-הניסוי שלו לבד, לפי סימון `STEP4-TESTPIPE`).
* **Connection Inspector** - לא נגע.
* **Fittings / Elbows / Connectors** - לא נוספו.
* **`WallRayCasting` / `WallPenetrationPolicy`** - לא נגעו.

השינוי היחיד הוא מנגנון דרישת/בחירת הקוטר ובדיקת התאמתו לגדלים הקיימים.

---

## 13. איך זה מתחבר ל-Step 5

Step 5 (**לא בוצע**) יחליף את ה-`DirectShape` ב-`Pipe` אמיתי בתוך
`DrawPipesCommand`. הבסיס שנבנה כאן:

* `DrawPipesCommand.Execute` כבר טוען `PipingOfficeSettings` (PIPE Step 3).
  ב-Step 5 הוא יטען את `SewerPipeDiameterRequirement`, יריץ פעם אחת את
  `PipeModelReadinessInspector.Evaluate(doc, requirement)` **לפני**
  הלולאה, ואם `!IsReady` - יעצור עם הדוח (כמו `CreateTestPipeCommand`),
  בלי לצייר כלום.
* אם `IsReady` - `SelectedDiameterMm` + `MatchingPipeTypeName` +
  `SanitarySystemTypeName` יזינו את `Pipe.Create` לכל מקטע-מסלול, במקום
  `DirectShape`. הגיאומטריה (נקודות התחלה/סוף) כבר מגיעה מ-`PipeRouteCalculator`
  ללא שינוי.
* `PipeModelReadinessInspector` ו-`PipeModelReadiness` נבנו כשירות משותף
  בדיוק כדי ש-Step 5 ישתמש בהם בלי שכפול.

**עצור. Step 5 הוא שלב נפרד.**
