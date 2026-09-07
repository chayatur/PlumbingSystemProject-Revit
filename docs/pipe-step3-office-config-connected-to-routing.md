# PIPE Step 3 - חיבור הגדרות המשרד ל-routing

> **עודכן ב-PIPE Step 4.1**: `PipingOfficeSettings` חושף כעת
> `SewerPipeDiameterRequirement` (מדויק / טווח). ה-`double SewerPipeDiameterMm`
> ששרשרת Step 3 משתמשת בו (למטה) **נשאר** - הוא מחושב מ-`NominalMm` של
> הדרישה (הקוטר המדויק, או קצה-תחתון של טווח). `DrawPipesCommand` **לא
> שונה**. ראו [pipe-step4-1-diameter-requirements.md](pipe-step4-1-diameter-requirements.md).

**סטטוס: הושלם.** קוטר הצינור והשיפוע - שהיו `const` ב-`PlumbingSystem.Core` -
מגיעים כעת מקובץ-ההגדרות המשרדי אל תוך יצירת מסלולי-הצינור בפועל.

**מה Step 3 לא עשה** (במפורש): לא `Pipe.Create`, לא בחירת
`PipeType`/`PipeSegment`/`PipingSystemType`, לא Fittings/Elbows/Connectors,
לא שינוי ב-`DirectShape` / visualization / deletion logic / Connection
Inspector, ו**לא שינוי באלגוריתם הניתוב עצמו** (straight / detour /
2×45° / staggered-Y / wall-obstruction / מגבלת 4 מ' / חישובי זווית /
`WallPenetrationPolicy`). כל אלה נשארו זהים בית-אחר-בית - השינוי היחיד
הוא **מקור** הקוטר/שיפוע.

## ה-data flow החדש

```
PlumbingSystem.OfficeConfig.txt        (משרד עורך בעורך טקסט)
  │   Sewer pipe diameter mm = 110
  │   Default slope percent = 1.75
  ▼
OfficeConfig.GetSingleValue(key)        (שכבת Revit - קורא גנרי + טיפול בפסיק)
  ▼
PipingOfficeSettings.Load()             (שכבת Revit - parsing + validation)
  │   → PipingOfficeSettings.SewerPipeDiameterMm, .DefaultSlopePercent  (double-ים)
  │      (ב-Step 4.1: SewerPipeDiameterMm = NominalMm של SewerPipeDiameterRequirement)
  ▼
DrawPipesCommand.Execute()              (שכבת Revit - נטען פעם אחת בתחילת ההרצה)
  │   → new PipeRouteParameters { DiameterMm = …, SlopePercent = … }
  ▼
PipeRouteCalculator.Calculate / CalculateDetour / CalculateStaggeredDetour   (Core)
  │   (נשלח כפרמטר דרך BuildRoute → TryBuildDetour / TryBuildStaggeredDetour /
  │    BuildManualEngineeringStubs)
  ▼
PipeSegment { DiameterMm, SlopePercent, … }   (Core - הפלט, בלי שינוי במבנה)
```

`PipingOfficeSettings` נטען **פעם אחת** ב-`Execute` (לא פר-מקטע),
מומר ל-`PipeRouteParameters` **בשכבת-Revit**, ומועבר כמו-שהוא ל-Core.

## מה השתנה - קבצי קוד

### `PlumbingSystem.Core` (הליבה - עדיין ללא תלות ב-Revit / config)

**חדש: [Domain/PipeRouteParameters.cs](../src/PlumbingSystem.Core/Domain/PipeRouteParameters.cs)** -
`record` עם `DiameterMm` + `SlopePercent` (שני `double`). זהו ה"סוג הפשוט"
ש-Core מקבל מהשכבה שמעליו. Core לא יודע על `OfficeConfig` / קובץ / נתיב /
RevitAPI / `PipingOfficeSettings` - רק על שני מספרים.
- בדיקת-שפיות בסיסית ב-`init` (חיובי וסופי, אחרת `ArgumentOutOfRangeException`).
- `PipeRouteParameters.Default` = `{ 110, 1.75 }` (הערכים ההיסטוריים
  שהיו `const`) - משמש רק את ה-overload-ים חסרי-הפרמטר (בדיקות-יחידה
  ופקודות-אבחון זמניות), **לא** את נתיב יצירת-הצינורות בפועל.

**שונה: [Domain/PipeRouteCalculator.cs](../src/PlumbingSystem.Core/Domain/PipeRouteCalculator.cs)** -
לכל אחת מ-`Calculate` / `CalculateDetour` / `CalculateStaggeredDetour`
נוסף פרמטר **אופציונלי אחרון** `PipeRouteParameters? parameters = null`
(`null` → `PipeRouteParameters.Default`, לשמירת תאימות-לאחור מלאה).
בגוף כל שיטה: `DefaultSlopePercent` → `parameters.SlopePercent`,
`PipeDiameterMm` → `parameters.DiameterMm`. **שום שינוי אחר** - כל
הגיאומטריה, הזוויות, פתרון-ההצטלבות, מגבלת ה-4 מ', בחירת-הצד, ובדיקת
טווח-השיפוע 1.5%-2.0% (חוק 2) - זהים.
- ה-`const PipeDiameterMm` / `DefaultSlopePercent` / `MinSlopePercent` /
  `MaxSlopePercent` **נשארו** - עדיין משמשים את `PipeRouteParameters.Default`,
  את בדיקת-הטווח ההנדסי, את הבדיקות, ואת שכבת-Revit (תצוגה/דוח).

### `PlumbingSystem.Revit`

**שונה: [Config/OfficeConfig.cs](../src/PlumbingSystem.Revit/Config/OfficeConfig.cs)** -
נוסף `GetSingleValue` (למעשה כבר ב-PIPE Step 2; נשאר כמו שהוא).

**שונה: [Commands/DrawPipesCommand.cs](../src/PlumbingSystem.Revit/Commands/DrawPipesCommand.cs)**:
- `Execute`: טוען `PipingOfficeSettings.Load()` פעם אחת (אחרי בחירת-
  ההיקף, לפני קריאת המודל), בונה `PipeRouteParameters`. `OfficeConfigException`
  (ערך חסר/לא-תקין) → `TaskDialog` + `Result.Failed` - **לא** המשך עם
  ערך-ברירת-מחדל.
- `BuildRoute`, `TryBuildDetour`, `TryBuildStaggeredDetour`,
  `BuildManualEngineeringStubs` - נוסף להם פרמטר `PipeRouteParameters routeParameters`,
  המועבר הלאה ל-`PipeRouteCalculator` ולבניית מקטעי-הגדם.
- **כל** קריאה ל-`Calculate` / `CalculateDetour` / `CalculateStaggeredDetour`
  בפקודה עודכנה - אין מקטע שנשאר על 110/1.75 קשיח.
- דוח: `DiameterMm=` בדוח נקרא כעת מהמקטע שנוצר בפועל
  (`RouteDiameterMm(pipe)`), לא מ-`const` - כדי שהדוח יהיה אמין גם כשהקוטר
  בקובץ שונה מ-110.

**גבול מכוון שלא נגעו בו**: `HalfWidthFeet` (חצי-רוחב **תיבת-התצוגה**
של ה-`DirectShape`) עדיין נגזר מ-`PipeRouteCalculator.PipeDiameterMm` (110).
Step 3 במפורש לא נוגע ב-visualization/DirectShape. הערך ההנדסי
(`PipeSegment.DiameterMm`, מה שיזין בעתיד `Pipe.Create`) - כן מגיע
מהקובץ. אם הקוטר בקובץ ישונה, המקטעים והדוח ישקפו זאת מיד; גודל התיבה
המצוירת יישאר 110 מ"מ עד ששלב עתידי יחווט גם אותו.

### בדיקות

**שונה: [tests/.../Domain/PipeRouteCalculatorTests.cs](../tests/PlumbingSystem.Core.Tests/Domain/PipeRouteCalculatorTests.cs)** -
נוספו 5 בדיקות: ברירת-מחדל (110/1.75), פרמטרים מותאמים מגיעים לכל
מקטע (`Calculate` / `CalculateDetour` / `CalculateStaggeredDetour`),
ושיפוע מחוץ ל-1.5%-2.0% עדיין זורק (חוק 2 לא נעקף).

**חדש: [tests/.../Domain/PipeRouteParametersTests.cs](../tests/PlumbingSystem.Core.Tests/Domain/PipeRouteParametersTests.cs)** -
10 בדיקות: `Default` = 110/1.75, ערכים תקינים נשמרים, קוטר/שיפוע
לא-חיובי או לא-סופי → `ArgumentOutOfRangeException`.

## כיצד Core נשאר עצמאי מ-Revit / config

- `PipeRouteParameters` הוא `record` של שני `double` בלבד. אין בו `using`
  ל-RevitAPI, ל-`PlumbingSystem.Revit`, או ל-IO.
- `PlumbingSystem.Core.csproj` עדיין ללא שום `ProjectReference` /
  `Reference` ל-Revit, ו-`net10.0` (לא `-windows`). מתקמפל ונבדק על
  מכונה בלי Revit מותקן.
- ההמרה `PipingOfficeSettings` → `PipeRouteParameters` קורית **בשכבת-Revit**
  (`DrawPipesCommand.Execute`). Core מקבל רק את התוצאה.
- `PipeRouteParameters.Default` מפנה ל-`const`-ים של `PipeRouteCalculator`
  שבתוך Core עצמו - לא ל-`OfficeConfig`.

## כיצד הערכים מגיעים בפועל ל-`PipeRouteCalculator`

`PipeRouteCalculator.Calculate(fixture, collector, parameters)`:
`parameters.SlopePercent` מחליף את `DefaultSlopePercent` בחישוב ה-Z-drop,
ו-`parameters.DiameterMm` מחליף את `PipeDiameterMm` בבניית ה-`PipeSegment`.
אותו דבר ב-detour וב-staggered (שם השיפוע נכנס ל-Z-drop של כל `leg`
והקוטר לכל `PipeSegment`). בדיקת הטווח 1.5%-2.0% על השיפוע ה**מחושב**
נשארת בדיוק כפי שהייתה - אם `parameters` נושא שיפוע מחוץ לטווח,
`Calculate` זורק `InvalidOperationException` ברור (חוק 2 נאכף, לא נעקף).

## בדיקות שבוצעו ותוצאות

### build / tests

| | תוצאה |
|---|---|
| `dotnet build PlumbingSystem.sln` | ✅ Build succeeded - 0 errors, 2 warnings (`MSB3277`, קיימות מראש, קשורות ל-RevitAPI) |
| `dotnet test` (PlumbingSystem.Core.Tests) | ✅ **77/77 עוברים** (היו 62 לפני Step 3; +15 חדשות) |

### בדיקה פונקציונלית (קובץ ההגדרות האמיתי → `PipingOfficeSettings`)

הורצה מול הקובץ האמיתי ומול העתקים מוזרקים, כל אחד ב-process נפרד:

| מקרה | קלט | תוצאה |
|---|---|---|
| בדיקה 1 | ברירת מחדל | `diameter=110  slope=1.75` ✅ |
| בדיקה 2 | `Sewer pipe diameter mm = 100` | `diameter=100` ✅ (לא נשאר 110) |
| בדיקה 3 | `Default slope percent = 2.0` | `slope=2.0` ✅ |
| בדיקה 4a | שורת-הקוטר הוסרה | `OfficeConfigException` עם שם-הקובץ ושורת-התיקון ✅ |
| בדיקה 4b | `Sewer pipe diameter mm = abc` | `OfficeConfigException` "אינו דרישת-קוטר תקינה" ✅ (הנוסח עודכן ב-Step 4.1) |
| בדיקה 4c | `Default slope percent = 1,75` | `OfficeConfigException` "השתמש בנקודה" ✅ |

**אין `fallback` שקט ל-110 / 1.75** בשום מקרה שבו הערך בקובץ שונה או
לא-תקין.

### בדיקה פונקציונלית (הפרמטר → מסלול Core)

בדיקות-היחידה ב-`PipeRouteCalculatorTests` מוכיחות ש-`PipeRouteParameters { DiameterMm = 100, SlopePercent = 2.0 }`
מגיע לכל `PipeSegment` של מסלול ישר, מסלול-עוקף (שני legs), ומסלול
Y-מדורג (שלושה מקטעים) - `DiameterMm == 100`, `SlopePercent == 2.0` -
ולא הערכים הקשיחים.

**הערה על כיסוי**: החוליה `DrawPipesCommand.Execute` (`PipingOfficeSettings.Load()`
→ `new PipeRouteParameters` → threading דרך `BuildRoute`) מכוסה על ידי
קומפילציה + code review + שתי הבדיקות הפונקציונליות שמשיקות משני צדדיה
(קובץ→DTO, ו-DTO→מסלול) - אין host-בדיקות לשכבת-Revit בפרויקט (במכוון,
כדי ש-CI ירוץ בלי Revit מותקן).

## דוגמה: שינוי קוטר ושיפוע דרך קובץ ההגדרות

עריכת `%AppData%\Autodesk\Revit\Addins\2027\PlumbingSystem.OfficeConfig.txt`:

```
Sewer pipe diameter mm = 100
Default slope percent = 2.0
```

שמירה, הרצת **"צייר צינורות"** מחדש - כל מקטע צינור שייווצר יקבל
`DiameterMm = 100` ו-`SlopePercent = 2.0` (וירידת-ה-Z תחושב לפי 2.0%).
אין צורך ב-Visual Studio / rebuild. להחזרת המצב: `110` ו-`1.75`.

(אם `Default slope percent` יוגדר מחוץ ל-1.5%-2.0%, ההרצה תיעצר עם
הודעה ברורה מ-`PipeRouteCalculator` - חוק 2 עדיין נאכף.)

## מה שלא שונה - במפורש

- **לוגיקת ה-routing לא שונתה**: straight / detour / 2×45° / staggered-Y /
  wall-obstruction / collector-distance / מגבלת 4 מ' / חישובי זווית /
  גיאומטריית המסלול / מבנה `PipeSegment` / `WallPenetrationPolicy` - כולם
  בית-אחר-בית זהים.
- **לא בוצע `Pipe.Create`** ולא נוצרו Revit Pipes אמיתיים.
- **`DirectShape` וה-visualization לא שונו** - אותם solids, אותם
  Materials/צבעים/Overrides, אותה תיבת-תצוגה. גם `deletion logic` ו-
  Connection Inspector לא נגעו.

## מה נשאר ל-PIPE Step 4

- מבחן-API מבודד: מה Revit עושה בפועל כשמגדירים `Diameter` של `Pipe`
  לערך שאינו בקטלוג ה-`Segment` (ראו [pipe-mep-investigation.md](pipe-mep-investigation.md) §3).
- "בדיקת מוכנות מודל": לוודא שקיים `PipeType`/`Segment` שתומך בקוטר
  שהוגדר בקובץ, **לפני** יצירת Pipe - כשל ברור אם לא (ראו PIPE Step 1).
- `Pipe.Create` בפועל: פתרון `PipeType` / `PipingSystemType` / `Level`,
  צריכת אותו פלט `PipeSegment` מ-Core (הגיאומטריה כבר גמורה).
- Fittings / Elbows / Connectors בנקודות-פנייה (החלטת-זווית טרם הוכרעה).
- עדכון סינון-קטגוריה (`OST_GenericModel` → גם/במקום `OST_PipeCurves`)
  ב-`DeleteExistingPipes` וב-Connection Inspector.
- חיווט קוטר-התצוגה (`HalfWidthFeet`) לקובץ-ההגדרות (אם יוחלט שצריך).
