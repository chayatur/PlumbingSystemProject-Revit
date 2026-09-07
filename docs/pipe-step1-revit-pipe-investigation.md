# PIPE Step 1 - חקירת מעבר ל-Revit MEP Pipe אמיתי

**סטטוס: חקירה בלבד - הושלמה. לא נוצר `Pipe` אמיתי, לא שונתה לוגיקת הניתוב.**

מסמך זה מסכם את **PIPE Step 1** בשרשרת המעבר מ-`DirectShape` (המימוש
הנוכחי, שלב 7) לצינור `Pipe` אמיתי של Revit MEP. הפירוט המלא והחי של
הממצאים נמצא ב-[pipe-mep-investigation.md](pipe-mep-investigation.md);
כאן מרוכזים מה נבדק, איך, ומה המסקנה הארכיטקטונית שממנה נגזר PIPE Step 2.

## מה נבנה

**`DiscoverPipingTypesCommand.cs`** (`src/PlumbingSystem.Revit/Commands/`,
כפתור **"אבחון סוגי-צנרת"**) - פקודת-אבחון **זמנית**,
`[Transaction(TransactionMode.ReadOnly)]`, שלא יוצרת/משנה/מוחקת שום
אלמנט. כותבת דוח-קריאה-בלבד לקובץ טקסט זמני ופותחת אותו. להסרה יחד עם
המחלקה כשהחקירה תיסגר (רשומה ככפתור-אבחון זמני ב-[App.cs](../src/PlumbingSystem.Revit/App.cs)).

המטרה שהוגדרה במפורש: **לא** "מה יש בפרויקט הזה", אלא האם קיים מנגנון
**גנרי, לא-תלוי-שם/ElementId** לבחור אוטומטית `PipingSystemType`/`PipeType`
מתאימים ל-STARTARC בכל קובץ Revit.

## מה נבדק, ואיך

### 1. `PipingSystemType` - סיווג לפי enum, לא לפי שם

`FilteredElementCollector(doc).OfClass(typeof(PipingSystemType))` - לכל
טיפוס-מערכת מדווחים `Name`, `SystemClassification` (ה-enum
`MEPSystemClassification`), ו-`ElementId`. **האות הגנרי הנבדק**:
`MEPSystemType.SystemClassification == MEPSystemClassification.Sanitary` -
enum סטנדרטי מובנה ב-Revit API, לא מחרוזת-שם חופשית. טיפוס מתאים לביוב
מזוהה כך בלי תלות באיך שהוא נקרא בפועל ("ביוב" / "Sanitary" / כל דבר).
הפקודה סופרת כמה טיפוסים מסווגים בפועל כ-`Sanitary`.

### 2. `PipeType` → `RoutingPreferenceManager` → Segment Rules → קטרים בפועל

`FilteredElementCollector(doc).OfClass(typeof(PipeType))` - לכל `PipeType`:

- `pipeType.RoutingPreferenceManager` →
  `GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments)` → מעבר על
  `GetRule(RoutingPreferenceRuleGroupType.Segments, i)`.
- לכל כלל: `rule.MEPPartId` → `doc.GetElement(segmentId) as Autodesk.Revit.DB.Plumbing.PipeSegment`.
- `segment.GetSizes()` → לכל `MEPSize`: `UnitUtils.ConvertFromInternalUnits(size.NominalDiameter, UnitTypeId.Millimeters)`.
- נבדק אם **מישהו** מהקטרים המוגדרים בפועל שווה ל-110 מ"מ (בסבילות
  `DiameterToleranceMm = 0.5` מ"מ - קטרים מדווחים לפעמים בעיגול-פנימי
  של Revit).

עטוף ב-`try/catch` **פר-טיפוס**, כדי שטיפוס אחד עם routing-preferences
חריג לא יפיל את כל הדוח.

### 3. Revit `Autodesk.Revit.DB.Plumbing.PipeSegment` מול Core `PipeSegment`

הבחנה קריטית שנעשתה במפורש בקוד: ה-`PipeSegment` שנבדק כאן הוא הטיפוס
של **Revit** (`Autodesk.Revit.DB.Plumbing.PipeSegment` - הגדרת-קטלוג של
חומר + טווח קטרים במודל), **לא** ה-`PipeSegment` של
[PlumbingSystem.Core](../src/PlumbingSystem.Core/Models/PipeSegment.cs)
(מקטע-מסלול גיאומטרי: נקודת התחלה/סוף, קוטר, שיפוע). שני טיפוסים שונים
לגמרי עם אותו שם - הבדיקה נוגעת רק בראשון.

### 4. קוטר-היעד

`TargetDiameterMm = 110.0` - **לא** מיובא מ-Core בכוונה (זו פקודת-חקירה
עצמאית), אבל תואם במפורש ל-`PipeRouteCalculator.PipeDiameterMm`.

## הממצאים (שני מודלי-Revit אמיתיים שונים)

הפקודה הורצה על שני מודלים שונים. בשניהם, אותו ממצא:

| אות גנרי | תוצאה |
|---|---|
| `PipingSystemType` עם `SystemClassification == Sanitary` | **קיים** לפחות אחד - הבחירה הגנרית של System Type ישימה |
| `PipeType`/`PipeSegment` שתומך בפועל בקוטר 110 מ"מ | **לא נמצא אף אחד** - נבדק דרך `RoutingPreferenceManager` → `PipeSegment.GetSizes()` האמיתיים, לא הנחה |

**זו נקודת-כשל אמיתית, לא תיאורטית**: הבחירה הגנרית של PipeType בקוטר
הנדרש אינה ישימה כיום באף אחד מהמודלים שנבדקו.

## המסקנה הארכיטקטונית

1. **דרישת הקוטר של STARTARC (110 מ"מ) מגיעה מהגדרת המשרד**, לא מהמודל.
   זהו כלל-אפיון קבוע (כמו שאר חוקי STARTARC) - לא נגזר מ-Revit ואסור
   שיסוט בגללו.
2. **מודל ה-Revit צריך לספק `PipeType`/`Segment` שתומך בקוטר המבוקש.**
   אם אין - זו בעיית **מוכנות-מודל** (Model Readiness), לא סיבה לקוד
   לבחור.
3. **אין לבצע `fallback` שקט לקוטר אחר** (100 / 125 מ"מ וכו'). אם הקוטר
   הנדרש אינו נתמך - כשל ברור ומפורש עם הנחיה למנהל-BIM להגדיר Segment
   מתאים, לא ניחוש.

הפרדת-האחריות הזו היא בדיוק מה ש-PIPE Step 2 מימש: שכבת-הגדרות משרדית
חיצונית שנותנת את "הקוטר הנדרש", בנפרד מהשאלה "האם המודל תומך בו".

## מה הושלם ומה מתוכנן

**הושלם**:
- פקודת-האבחון `DiscoverPipingTypesCommand` (ReadOnly) + הרצתה על שני מודלים.
- תיעוד הממצאים וההחלטות הפתוחות ב-[pipe-mep-investigation.md](pipe-mep-investigation.md).
- המסקנה הארכיטקטונית שלמעלה - שהובילה ל-PIPE Step 2.

**מתוכנן (לא בוצע)** - ראו [pipe-mep-investigation.md](pipe-mep-investigation.md)
סעיפים 3-8:
- מבחן-API מבודד: מה קורה בפועל כשמגדירים `Diameter` של `Pipe` לערך
  שאינו בקטלוג ה-`Segment` (מקבל כמו-שהוא / מיישר / דוחה) - **דורש בדיקה
  אמיתית ב-Revit, לא הנחה**.
- "בדיקת מוכנות מודל" (Model Readiness Check) לפני יצירת Pipe אמיתי.
- `Pipe.Create` בפועל, בחירת `PipeType`/`PipingSystemType`/`Level`,
  Fittings/Elbows בנקודות-פנייה - **PIPE Step 4 ואילך**.

**שום קוד של לוגיקת ניתוב לא שונה ב-Step 1.**
