# PIPE Step 2 - שכבת הגדרות המשרד לצנרת

**סטטוס: הושלם. השכבה נוצרה - אבל בשלב זה הערכים עדיין לא חוברו ללוגיקת
ה-routing של Core (זה PIPE Step 3).**

> **עודכן ב-PIPE Step 4.1**: המפתח `Sewer pipe diameter mm` מקבל כעת גם
> **טווח** (`100-110`), לא רק מספר יחיד, וה-DTO חושף
> `SewerPipeDiameterRequirement` (טיפוס `PipeDiameterRequirement` של Core)
> לצד ה-`double SewerPipeDiameterMm` (מחושב, לתאימות-לאחור). ראו
> [pipe-step4-1-diameter-requirements.md](pipe-step4-1-diameter-requirements.md).
> כמו כן ה-`.csproj` **כן** עודכן מאז (תיקון deployment + עמידות בפני
> Revit-פתוח) - ראו הערה בסעיף "הפצה".

מסמך זה מתעד את **PIPE Step 2**: הרחבת מנגנון `OfficeConfig` הקיים כך
שהמשרד יוכל לקבוע גם את **קוטר צינור הביוב** ואת **שיפוע ברירת-המחדל**
בעריכת קובץ טקסט - בלי Visual Studio, בלי קוד מקור, בלי rebuild. הרקע
(למה הקוטר מגיע מהמשרד ולא מהמודל) ב-[pipe-step1-revit-pipe-investigation.md](pipe-step1-revit-pipe-investigation.md).

## מה כבר היה קיים לפני Step 2

- **`PlumbingSystem.OfficeConfig.txt`** - קובץ טקסט לצד ה-DLL, פורמט
  `Key = Value` (או רשימה מופרדת-פסיקים), `#` להערה, UTF-8. הכיל מפתח
  אחד: `Concrete wall = קיר בטון 20`.
- **[Config/OfficeConfig.cs](../src/PlumbingSystem.Revit/Config/OfficeConfig.cs)** -
  קורא גנרי: `GetValues(standardKey)` → רשימת מחרוזות (או ריקה). נטען
  פעם אחת (Lazy) ומוטמן לכל חיי ה-process של Revit. מאתר את הקובץ לפי
  `Assembly.Location` ואז `AppContext.BaseDirectory`.
- **[Config/WallPenetrationPolicy.cs](../src/PlumbingSystem.Revit/Config/WallPenetrationPolicy.cs)** -
  הצרכן היחיד של `OfficeConfig` (מפתח `"Concrete wall"`).
- הפילוסופיה המתועדת: **התוכנה קובעת מפתח סטנדרטי, המשרד מתאים את
  הערך** - בדיוק כמו ה-Shared Parameter `Is_Toilet`.

הגדרות הצנרת עצמן (קוטר, שיפוע) היו `const` ב-`PlumbingSystem.Core`
(`PipeRouteCalculator.PipeDiameterMm = 110`, `DefaultSlopePercent = 1.75`)
- **לא** ב-Config.

## מה נבנה ב-Step 2

### 1. הרחבה גנרית ל-`OfficeConfig`

`OfficeConfig.GetSingleValue(standardKey)` - מחזיר ערך **יחיד** (או
`null` אם המפתח/הקובץ חסרים). זורק `OfficeConfigException` אם למפתח מופו
**כמה** ערכים מופרדי-פסיק - כי מפתח סקלרי (קוטר/שיפוע) חייב להיות ערך
אחד, ופסיק בערך מספרי הוא כמעט תמיד מפריד-עשרוני שגוי (`1,75` במקום
`1.75`). `GetValues` הקיים לא שונה.

### 2. [Config/OfficeConfigException.cs](../src/PlumbingSystem.Revit/Config/OfficeConfigException.cs) (חדש)

חריגה ייעודית לערך חסר/לא-תקין בקובץ. ההודעה מנוסחת כך שמנהל-BIM / משתמש
משרד יתקן בעצמו: שם-ההגדרה + הבעיה + נתיב-הקובץ בפועל + שורת-תיקון
לדוגמה. **אין `fallback` שקט** - עדיף כשל ברור.

### 3. [Config/PipingOfficeSettings.cs](../src/PlumbingSystem.Revit/Config/PipingOfficeSettings.cs) (חדש)

ה-DTO + ה-loader. `PipingOfficeSettings.Load()` קורא דרך
`OfficeConfig.GetSingleValue`, מבצע parsing + validation, ומחזיר DTO
טיפוסי. יש גם `internal Load(Func<string,string?>)` - נקודת-הפרדה
לבדיקה, בלי תלות בקובץ אמיתי.

**נכון ל-PIPE Step 4.1** ה-DTO חושף: `SewerPipeDiameterRequirement`
(טיפוס `PipeDiameterRequirement` - מדויק / טווח), `DefaultSlopePercent`
(`double`), ו-`SewerPipeDiameterMm` (`double` מחושב = `NominalMm` של
הדרישה, לתאימות-לאחור עם PIPE Step 3). *(ב-Step 2 המקורי היו שני
`double` בלבד: `SewerPipeDiameterMm` + `DefaultSlopePercent`.)*

**גבול-שכבות מכוון**: המחלקה יושבת ב-`PlumbingSystem.Revit`, **לא**
ב-`PlumbingSystem.Core`. Core הוא לוגיקה טהורה ואסור שיהיה תלוי
ב-RevitAPI או בקובץ-הגדרות של שכבת-Revit.

### 4. שני מפתחות חדשים ב-`PlumbingSystem.OfficeConfig.txt`

```
Sewer pipe diameter mm = 110
Default slope percent = 1.75
```

עם הערות-הסבר בקובץ עצמו (איך לערוך, שהערכים הם נתוני-Config ולא קבועים,
שאין fallback שקט, שיש להשתמש בנקודה עשרונית). הערכים משקפים **בדיוק** את
המצב שהיה `const` בקוד - שינוי אפס בהתנהגות.

## מנגנון ה-validation

| קלט בקובץ | תוצאה |
|---|---|
| קוטר תקין (`110`, `101.6`, `100-110` [Step 4.1], עם רווחים) | מתקבל |
| שיפוע תקין (`1.75`, `2.0`, עם רווחים) | מתקבל |
| מפתח חסר / שורה ריקה / ערך ריק | `OfficeConfigException` - "חסר או ריק... הוסף שורה כגון..." |
| ערך לא-מספרי/לא-טווח (`abc`) | `OfficeConfigException` - "אינו דרישת-קוטר תקינה..." |
| `NaN` / `Infinity` | `OfficeConfigException` (נתפס ב-`double.IsFinite`) |
| קוטר `0` או שלילי | `OfficeConfigException` - "קוטר חייב להיות מספר חיובי" |
| טווח הפוך (`110-100`) [Step 4.1] | `OfficeConfigException` - "הגבול התחתון גדול מהעליון" |
| שיפוע `0`, שלילי, או `>= 100` | `OfficeConfigException` - "אינו שיפוע תקין - מספר חיובי באחוזים, קטן מ-100" |
| פסיק עשרוני (`1,75`) | `OfficeConfigException` - "מכילה כמה ערכים מופרדים בפסיק... השתמש בנקודה" |

**תקרת ה-100 על השיפוע היא בדיקת-שפיות בלבד** (שיפוע ≥ 100% = 45°, לא
שיפוע-ביוב-בכבידה סביר - כמעט תמיד טעות-הקלדה). **הטווח ההנדסי המדויק
1.5%-2.0% (חוק 2) עדיין נאכף במקום אחר - `PipeRouteCalculator`** - ולא
משתנה כאן. כלומר שתי שכבות: שפיות-קונפיגורציה (`0 < x < 100`) + טווח
הנדסי (`1.5-2.0` ב-Core, על השיפוע ה**מחושב** בפועל).

## איך המשרד עורך את ההגדרות

פותחים בעורך-טקסט רגיל (Notepad / VS Code) את:

```
%AppData%\Autodesk\Revit\Addins\2027\PlumbingSystem.OfficeConfig.txt
```

משנים את הערך אחרי ה-`=`, שומרים. התוסף קורא את הקובץ בזמן ריצה. **אין
צורך ב-Visual Studio, בקוד מקור, ב-`.csproj` או ב-rebuild.**

## הפצה

`PlumbingSystem.OfficeConfig.txt` מוגדר ב-[PlumbingSystem.Revit.csproj](../src/PlumbingSystem.Revit/PlumbingSystem.Revit.csproj):
`CopyToOutputDirectory` + Target `CopyAddinToRevit` (מעתיק לתיקיית
ה-Add-ins של Revit אחרי כל build).

*ב-Step 2 עצמו לא נדרש שינוי ב-`.csproj` (רק תוכן הקובץ הורחב).* **מאז
ה-`.csproj` כן עודכן** (לא כחלק מ-Step 2): (א) `CopyAddinToRevit` מעתיק
כעת גם את `PlumbingSystem.Core.dll` (לא רק `Revit.dll`) - קודם Revit
היה טוען Core ישן ונופל עם `Could not load type`; (ב) `ContinueOnError="WarnAndContinue"`
על ה-`Copy` - build לא נכשל יותר כש-Revit פתוח ונועל את ה-DLL-ים,
מודפסת אזהרה במקום.

## מה הושלם ומה מתוכנן

**הושלם**: שכבת ההגדרות המלאה (קריאה, parsing, validation, DTO, הפצה).
`dotnet build` + `dotnet test` עברו (62/62 באותו זמן).

**מתוכנן - PIPE Step 3**:

> **חשוב: Step 2 יצר את שכבת ההגדרות, אבל בשלב זה הערכים עדיין לא חוברו
> בפועל לאלגוריתם ה-routing של Core.** `PipeRouteCalculator` המשיך לקרוא
> את הקוטר/שיפוע מ-`const` משלו. `PipingOfficeSettings` היה API מוכן,
> **בלי קורא בפועל** בנתיב יצירת-הצינורות. החיבור עצמו הוא PIPE Step 3 -
> ראו [pipe-step3-office-config-connected-to-routing.md](pipe-step3-office-config-connected-to-routing.md).
