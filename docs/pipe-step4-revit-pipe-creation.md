# PIPE Step 4 - יצירת Revit Pipe אמיתי (הוכחת-API מבודדת)

**סטטוס: הקוד נכתב ומתקמפל מול RevitAPI 2027. ההרצה בפועל (`Pipe.Create`
במודל חי) דורשת Revit ונבדקת ידנית - ראו "תוצאות הבדיקות".**

> **עודכן ב-PIPE Step 4.1** ([pipe-step4-1-diameter-requirements.md](pipe-step4-1-diameter-requirements.md)):
> דרישת-הקוטר יכולה כעת להיות **מספר מדויק או טווח** (`PipeDiameterRequirement`),
> לא `double` יחיד. `PipeModelReadiness` בוחר את **הגודל הקיים הקטן ביותר**
> שעומד בדרישה (`SelectedDiameterMm`), והסבילות המדויקת ירדה מ-±0.5 מ"מ
> (הנדסי) ל-`1e-6` מ"מ (floating-point בלבד). הפרטים למטה מתארים את המבנה
> הכללי - החתימות המדויקות במסמך Step 4.1.

מסמך זה מתעד את **PIPE Step 4**: הוכחה מבודדת שאפשר ליצור `Pipe` אמיתי
של Revit MEP באמצעות `Pipe.Create`, בקוטר שמגיע מהזרימה שנבנתה ב-PIPE
Step 3 (קובץ-ההגדרות המשרדי). **אין** כאן route שלם, אין אינטגרציה עם
`DrawPipesCommand`, אין Fittings/Elbows.

## 1. מטרת Step 4

להוכיח את ה-API והדרישות של `Pipe.Create` **לפני** שמשלבים יצירת Pipes
בזרימת STARTARC (זה Step 5). ספציפית:

- לוודא אילו `PipingSystemType` / `PipeType` / `Segment` נדרשים.
- לוודא ש-`Pipe.Create` יוצר Pipe אמיתי (לא DirectShape).
- לוודא שהקוטר מגיע מ-`PlumbingSystem.OfficeConfig.txt` → `PipingOfficeSettings` →
  `double` typed → הצינור - **בלי `110` קשיח ב-Step 4**.
- לוודא שאם הקוטר אינו נתמך במודל - **אין fallback** - מתקבלת שגיאת
  Model Readiness מפורשת.

## 2. מה נבדק / נבנה

| רכיב | קובץ |
|---|---|
| החלטת מוכנות-מודל (טהור, בר-בדיקה) | [PipeModelReadiness.cs](../src/PlumbingSystem.Core/Domain/PipeModelReadiness.cs) (Core) |
| שליפת הנתונים מ-Revit + עזרי ElementId | [PipeModelReadinessInspector.cs](../src/PlumbingSystem.Revit/Mep/PipeModelReadinessInspector.cs) |
| פקודת-הניסוי `Pipe.Create` | [CreateTestPipeCommand.cs](../src/PlumbingSystem.Revit/Commands/CreateTestPipeCommand.cs) (כפתור **"צינור-ניסוי (Step 4)"**) |
| בדיקות-יחידה למוכנות | [PipeModelReadinessTests.cs](../tests/PlumbingSystem.Core.Tests/Domain/PipeModelReadinessTests.cs) |

## 3. `PipingSystemType`

הפקודה שולפת `FilteredElementCollector(doc).OfClass(typeof(PipingSystemType))`
ומסננת לפי `t.SystemClassification == MEPSystemClassification.Sanitary` -
**enum סטנדרטי, לא שם**, בדיוק כמו PIPE Step 1. נבחר הטיפוס הראשון (סדר
א"ב) המסווג Sanitary. אם אין אף אחד - המודל אינו מוכן.

## 4. `PipeType`

שולפת `OfClass(typeof(PipeType))`. לכל טיפוס: קוראת את הקטרים שהוא תומך
בהם בפועל (סעיף 5) ומעבירה ל-Core. "PipeType שאפשר להשתמש בו ליצירה" =
כזה שיש לו `Segment` שתומך בקוטר הנדרש. נבחר הראשון (סדר א"ב) שעונה על כך.

## 5. `Segment`

לכל `PipeType`: `pipeType.RoutingPreferenceManager` →
`GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments)` → לכל כלל
`GetRule(...).MEPPartId` → `doc.GetElement(id) as Autodesk.Revit.DB.Plumbing.PipeSegment`
→ `segment.GetSizes()` → `size.NominalDiameter` → המרה ל-מ"מ. עטוף
ב-`try/catch` פר-טיפוס. **זו אותה בדיקה של PIPE Step 1** - כאן היא
מוצאת לשירות משותף (`PipeModelReadinessInspector`) במקום להישאר בתוך
`DiscoverPipingTypesCommand` בלבד.

ההשוואה בין דרישת-הקוטר לגודל על ה-Segment (PIPE Step 4.1):
`PipeDiameterRequirement.Matches` - למדויק, סבילות `1e-6` מ"מ
(floating-point בלבד); לטווח, `[min, max]` כולל. כשכמה גדלים עומדים
בדרישה נבחר **הקטן ביותר** (`PipeModelReadinessResult.SelectedDiameterMm`).

## 6. `Pipe.Create`

```csharp
Pipe.Create(doc, systemTypeId, pipeTypeId, level.Id, startPoint, endPoint);
```

- `systemTypeId` / `pipeTypeId` - נבחרו על ידי בדיקת-המוכנות.
- `level.Id` - מהתצוגה הפעילה (`ActiveView.GenLevel`), או ה-Level הנמוך
  ביותר במודל.
- `startPoint` - המשתמש בוחר בקליק (`uidoc.Selection.PickPoint`) - נקודה
  בטוחה במקום פנוי. `endPoint` = קבוע יחסית: `start + 2.0 מ'` בכיוון +X,
  באותו Z. (הצינור הוא הוכחת-API, לא route - לכן קצר, אופקי, ולא מבוסס
  נתוני אסלה/קולטן.)
- אחרי היצירה: `RBS_PIPE_DIAMETER_PARAM` מוגדר ל-`readiness.SelectedDiameterMm`
  (הגודל הקיים שנבחר - **לא** גבול-טווח ולא הדרישה הגולמית; PIPE Step 4.1),
  `doc.Regenerate()`, והקוטר **נקרא בחזרה** מהצינור לאימות.
- Comments + Mark מוגדרים ל-`"STEP4-TESTPIPE (...)"` - **לא** קידומת
  `"PIPE-"` של STARTARC.
- הכל בתוך `Transaction` אחד; כשל → `RollBack` + דוח שגיאה מפורש.

## 7. כיצד הקוטר מגיע מה-OfficeConfig

```
PlumbingSystem.OfficeConfig.txt   (Sewer pipe diameter mm = 110  |  = 101.6  |  = 100-110)
  → PipingOfficeSettings.Load().SewerPipeDiameterRequirement   (PipeDiameterRequirement, שכבת Revit)
  → PipeModelReadinessInspector.Evaluate(doc, requirement)     (בדיקת מוכנות + בחירת הגודל הקטן ביותר שעומד בדרישה)
  → RBS_PIPE_DIAMETER_PARAM.Set( ConvertToInternalUnits(readiness.SelectedDiameterMm, mm) )   (על הצינור שנוצר)
```

אין `110` קשיח ב-Step 4. שינוי הקובץ ל-`100-110` → הניסוי בודק מוכנות
לטווח, בוחר את הגודל הקיים הקטן ביותר בטווח (למשל `4"` = 101.6 מ"מ),
ומנסה ליצור צינור בגודל הזה. ראו [pipe-step4-1-diameter-requirements.md](pipe-step4-1-diameter-requirements.md).

## 8. מה קורה כשהקוטר אינו נתמך

`PipeModelReadiness.Evaluate` מחזיר `IsReady = false` **בלי לבחור קוטר
חלופי**. `CreateTestPipeCommand` מציג `TaskDialog` עם הדוח, כותב קובץ-דוח,
ומחזיר `Result.Failed`. הדוח מפרט:

- הקוטר שהוגדר במשרד.
- כל `PipeType` שנבדק + הקטרים שהוא תומך בהם.
- שאין `Segment` שתומך בקוטר הנדרש (ו/או שאין טיפוס Sanitary).
- שיש להגדיר `PipeSegment` בקוטר הזה במודל / ב-Template המשרד
  (Manage → MEP Settings → Segments and Sizes).
- "STARTARC לא בוחר קוטר חלופי אוטומטית - הקוטר נקבע במשרד, והמודל צריך
  להיות מוגדר בהתאם."

**זה בדיוק הממצא של PIPE Step 1**: בשני המודלים שנבדקו אז, אף `PipeType`
לא תמך ב-110 מ"מ - כלומר בפועל `CreateTestPipeCommand` יעצור שם בשגיאת
Model Readiness עד שיוגדר Segment מתאים.

## 9. תוצאות הבדיקות

### אוטומטי (`dotnet build` / `dotnet test`)

| | תוצאה |
|---|---|
| `dotnet build PlumbingSystem.sln` | ✅ 0 errors, 0 CS warnings (2 × `MSB3277` קיימות מראש). **הקומפילציה מול RevitAPI 2027 מאשרת שחתימות `Pipe.Create`, `RoutingPreferenceManager`, `RBS_PIPE_DIAMETER_PARAM`, `MEPSystemClassification.Sanitary` נכונות.** |
| `dotnet test` | ✅ **131/131** (86 אחרי Step 4 → **+45** ב-Step 4.1: `PipeDiameterRequirementTests` + הרחבת `PipeModelReadinessTests`) |

**בדיקות Core אוטומטיות מכסות את Test 3** (הפירוט המלא ב-[pipe-step4-1-diameter-requirements.md](pipe-step4-1-diameter-requirements.md) §11):
- Sanitary + גודל שעומד בדרישה → `IsReady`, `SelectedDiameterMm` = הגודל.
- אף גודל לא עומד בדרישה → `IsReady=false`, `SelectedDiameterMm=null`,
  הדוח מכיל "לא בוחר קוטר חלופי".
- אין טיפוס Sanitary → `IsReady=false`.
- טווח עם כמה התאמות → נבחר הקטן ביותר.
- דרישה לא-חוקית → `FormatException` / `OfficeConfigException`.

### ידני (דורש Revit + מודל - **לא ניתן להרצה אוטומטית כאן**)

| Test | מה לבדוק | איך |
|---|---|---|
| **Test 1** | מודל עם Segment שתומך בקוטר → `Pipe.Create` מצליח, Pipe אמיתי נוצר, קוטר נכון | פתח מודל שבו PipeType כולל Segment ל-110 מ"מ, לחץ **"צינור-ניסוי (Step 4)"**, בחר נקודה. ה-`TaskDialog` והדוח מציגים ElementId, סוג, וקוטר-בפועל שנקרא מהצינור. |
| **Test 2** | שינוי `Sewer pipe diameter mm` לערך אחר שנתמך במודל → הצינור מקבל את הקוטר החדש | ערוך את הקובץ (למשל ל-`160`), הרץ שוב, ודא שהדוח מראה `Applied diameter ≈ 160 mm`. |
| **Test 3** | קוטר שאינו ב-Segment → אין fallback, שגיאת Model Readiness | ערוך לקוטר לא-קיים (למשל `111`), הרץ. אמור להתקבל `TaskDialog` "המודל אינו מוכן" + `Result.Failed`, **בלי** יצירת צינור. (הלוגיקה כבר מכוסה אוטומטית ב-Core.) |
| **Test 4** | האלמנט שנוצר הוא Revit `Pipe`, לא DirectShape | הדוח מדפיס `Runtime type: Autodesk.Revit.DB.Plumbing.Pipe` ו-`Category: Pipes (is OST_PipeCurves = True)`. אפשר גם לבחור את האלמנט ולראות ב-Properties שהוא Pipe בקטגוריית Pipes. |

## 10. מה הוכח בהצלחה

- **הקומפילציה** מאשרת שה-API של `Pipe.Create` והדרישות שלו
  (`systemTypeId`, `pipeTypeId`, `levelId`, נקודות) תואמים למה שתוכנן.
- **בדיקת מוכנות-המודל** עובדת מקצה-לקצה ברמת-הלוגיקה: הקוטר מקובץ-
  ההגדרות → החלטה "מוכן/לא-מוכן" → דוח מפורש, **בלי fallback** (9 בדיקות
  אוטומטיות).
- **הזרימה של הקוטר** (Step 3) מחוברת גם לנתיב יצירת-ה-Pipe: `PipingOfficeSettings`
  → readiness → `RBS_PIPE_DIAMETER_PARAM`.
- הצינור הניסיוני מסומן ומופרד מצינורות STARTARC; כל הרצה מחליפה את הקודם.

## 11. מה עדיין לא בוצע

- **הרצת `Pipe.Create` בפועל במודל חי** - Tests 1/2/4 ידניים (אין Revit
  ב-CI). ייתכן שיתגלה שצריך לטפל אחרת בהגדרת הקוטר (למשל אם
  `RBS_PIPE_DIAMETER_PARAM` הוא read-only בגרסה/הקשר מסוימים - אז הכשל
  יהיה מפורש ב-`RollBack`, לא שקט).
- **אינטגרציה עם `DrawPipesCommand`** - **לא בוצעה.** זה PIPE Step 5.
- **Fittings / Elbows / Connectors** בנקודות-פנייה - לא בוצעו.
- **בחירת נקודות מנתוני אסלה/קולטן** - לא בוצעה (Step 4 משתמש בנקודה
  שהמשתמש בוחר + היסט קבוע).
- מגוון PipeType מרובה / בחירה חכמה יותר מ"הראשון בסדר א"ב".

## במפורש - מה לא שונה ב-Step 4

- **לא בוצעה אינטגרציה עם `DrawPipesCommand`** - הקובץ לא נגע.
- **לא בוצעו Fittings.**
- **לא בוצעו Elbows / Connectors.**
- **לא שונה ה-routing** (straight / detour / staggered / wall obstruction /
  collector logic / מגבלת 4 מ' / חישוב שיפוע / זוויות / `PipeRouteCalculator`).
- **ה-`DirectShape` הקיים עדיין קיים ופעיל** - `DrawPipesCommand` ממשיך
  לצייר DirectShape בדיוק כמו קודם.
- **לא שונה Connection Inspector.**
- **לא שונה `DeleteExistingPipes`** - `CreateTestPipeCommand` מנקה את
  צינורות-הניסוי שלו לבד, לפי סימון נפרד.
