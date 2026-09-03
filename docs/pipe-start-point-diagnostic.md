# בדיקת אבחון: האם `fixture.Location` (במקום MEP Connector) הוא הגורם ל"גדמים" בצינורות

**סטטוס: בדיקת אבחון בלבד. שום קוד לא שונה. אלגוריתם הניתוב לא שונה.
הבדיקה לא הושלמה - חסרים נתוני Revit חיים (ראו סעיף 6). המסמך מתעד
את מה שכן ניתן היה לקבוע מקריאת-קוד בלבד, ואת מה שחסר כדי להכריע.**

---

## 1. מטרת הבדיקה

לבדוק האם השימוש הנוכחי ב-`ToiletFixture.Location` (נקודת ה-`LocationPoint`
של ה-`FamilyInstance` של האסלה) כנקודת-המוצא של כל מקטע צינור - במקום
נקודת-היציאה האמיתית של הביוב (`MEPModel` Connector) - יכול להיות הגורם
ל"גדמים" (stubs) שנראים בצינורות היוצאים מאסלות.

הבדיקה **אינה** מציעה תיקון ואינה מבצעת refactor - היא רק אוספת ראיות.

---

## 2. המימוש הקיים לפני הבדיקה

נקודת-המוצא של כל מקטע צינור היא `fixture.Location` **בדיוק כפי שהיא**,
לאורך כל שרשרת-העיבוד, בלי שום התייחסות ל-Connector:

| שלב | קובץ | מה קורה |
|---|---|---|
| קריאת האסלה | [RevitModelReader.cs:267](../src/PlumbingSystem.Revit/RevitModelReader.cs#L267), [:281](../src/PlumbingSystem.Revit/RevitModelReader.cs#L281) | `XYZ? point = (instance.Location as LocationPoint)?.Point;` → נשמר כ-`ToiletFixture.Location` (X, Y **ו-Z גולמי**). |
| מסלול ישר | [PipeRouteCalculator.cs:177](../src/PlumbingSystem.Core/Domain/PipeRouteCalculator.cs#L177) | `startPoint: fixture.Location` - מילולית. |
| מסלול-עוקף / Y-מדורג | [PipeRouteCalculator.cs:342](../src/PlumbingSystem.Core/Domain/PipeRouteCalculator.cs#L342), [:482](../src/PlumbingSystem.Core/Domain/PipeRouteCalculator.cs#L482) | `startPoint: fixture.Location`. |
| גדמי "דורש תכנון ידני" | [DrawPipesCommand.cs:548-553](../src/PlumbingSystem.Revit/Commands/DrawPipesCommand.cs#L548-L553) | `startPoint: fixture.Location`; כיוון הגדם = `(collector.Location - fixture.Location)` מנורמל. |
| בדיקת חסימת-קיר | [RevitModelReader.cs](../src/PlumbingSystem.Revit/RevitModelReader.cs), `WallRayCasting` | הקרן נורית מ-`fixture.Location` ל-`collector.Location` - דו-ממדית (X,Y בלבד). |

**ה-Z** של נקודת-המוצא הוא ה-Z הגולמי של `LocationPoint` של האסלה
(לרוב נקודת-ההצבה של המשפחה, לא גובה-מפלס-היציאה של הצינור).

---

## 3. מה נבדק לגבי `MEP Connector` - ממצא ברמת-הקוד

חיפוש מלא במאגר (`MEPModel`, `ConnectorManager`, `Connector`):

- **הקוד היחיד שקורא Connectors של אסלה הוא
  [DiscoverFixtureSignatureCommand.cs](../src/PlumbingSystem.Revit/Commands/DiscoverFixtureSignatureCommand.cs)**
  - פקודת-אבחון ReadOnly זמנית, לא-מקושרת בכלל לניתוב.
- **שום דבר בשרשרת-הניתוב** (`RevitModelReader`, `PipeRouteCalculator`,
  `DrawPipesCommand`, `CollectorLocator`, `CollectorPlacementService`,
  `WallRayCasting`) אינו ניגש ל-`MEPModel`/Connector - אף פעם.
- Connection Inspector ([ElementRelationshipLookup.cs](../src/PlumbingSystem.Revit/Inspector/ElementRelationshipLookup.cs))
  קורא רק מחרוזות `Mark` ו-`LocationPoint` (ל-Room lookup) - **אין בו
  גיאומטריית Connector**.

מסקנת-ביניים: המנוע **אין לו מועמד אחר** לנקודת-מוצא - הוא מעולם לא
קורא Connector, ולכן נקודת-ההתחלה של כל צינור/גדם היא `fixture.Location`
מילולית, **ללא תלות במודל** - זו עובדת-קוד, לא ממצא-מדידה.

---

## 4. מה שנקבע מקריאת-קוד בלבד (בלי Revit)

### 4.1 האם הגדם מתחיל מ-`fixture.Location`?

**כן - ודאי, מוכח מהקוד.** ([DrawPipesCommand.cs:550](../src/PlumbingSystem.Revit/Commands/DrawPipesCommand.cs#L550)).
ה-DirectShape נבנה ישירות מנקודות ה-`PipeSegment`, בלי שום הצמדה
ל-Connector בשום שלב.

### 4.2 מהם ה"גדמים" האלה בפועל?

ב-5 המקרים המתועדים בקומה 2
([reports/ManualEngineeringReport_Floor2_2026-08-13.md](../reports/ManualEngineeringReport_Floor2_2026-08-13.md)),
הגדמים הם **סמנים ויזואליים מכוונים** שמצייר `BuildManualEngineeringStubs`
**רק** אחרי שכל 28 חלופות-העקיפה נכשלו - לא תקלה, ולא תוצר-לוואי של
נקודת-המוצא. כל גדם באורך 0.2 מ', יוצא מ-`fixture.Location` לכיוון
הקולטן, ובמכוון אינו מחובר לגדם שמנגד.

### 4.3 כיוון הגדם מול כיוון-היציאה של האסלה

כיוון הגדם המצויר = הכיוון האווירי `fixture.Location → collector.Location`
בלבד ([DrawPipesCommand.cs:530-531](../src/PlumbingSystem.Revit/Commands/DrawPipesCommand.cs#L530-L531)).
הוא **אינו** נגזר מ-`Connector.CoordinateSystem.BasisZ` ואין לו שום קשר
לכיוון שבו הביוב באמת יוצא מהאסלה. השוואה מספרית מול `BasisZ` דורשת
נתוני Revit (סעיף 6).

### 4.4 ציר ה-Z

בכל 5 המקרים בדוח, `fixture.Location.Z == 4.7000` == ה-Z של הקולטן.
מכיוון שבדיקת חסימת-הקיר היא **דו-ממדית** (X,Y), הפרש-Z אפשרי בין
`fixture.Location` ל-Connector של הביוב **לא יכול, כשלעצמו, להסביר
גדם שנוצר מחסימת-קיר**. הפרש-Z כזה כן רלוונטי לריאליזם של השיפוע
ושל גובה-החיבור, אך לא לעצם ה"גדם".

---

## 5. שלושת המקרים - נתונים ככל שיש כרגע

הנתונים בטבלאות נלקחו מ-
[reports/ManualEngineeringReport_Floor2_2026-08-13.md](../reports/ManualEngineeringReport_Floor2_2026-08-13.md)
(הרצת Revit אמיתית, 2026-08-13). ערכי "Stub Start/End" חושבו
דטרמיניסטית מ-`BuildManualEngineeringStubs` על אותם קלטים - **לא**
נמדדו בהרצה נפרדת. "Sewer Connector Origin/BasisZ" = **טרם נאסף**
(ראו סעיף 6).

### מקרה A - דירה 1131, אסלה 5284771 (הקולטן **לא** בפינה)

| שדה | ערך |
|---|---|
| `fixture.Location` | `(-142.3740, -78.5158, 4.7000)` |
| Sewer Connector Origin | **לא זמין - נדרשת הרצת Revit** |
| Sewer Connector BasisZ | **לא זמין** |
| Stub Start (מהקוד) | `(-142.3740, -78.5158, 4.7000)` = `fixture.Location` |
| Stub End (מהקוד) | `≈ (-142.5740, -78.5170, 4.7000)` (0.2 מ' לכיוון הקולטן) |
| מיקום הקולטן (סופי) | `(-145.8721, -78.5370, 4.7000)` |
| קיר חוסם | `5109157`, מרחק קולטן-לפינה **2.5947 מ'** |
| `XY distance` (Location↔Connector) | **לא ניתן לחשב - Connector Origin חסר** |
| `Z difference` (Location↔Connector) | **לא ניתן לחשב** |

### מקרה B - דירה 1132, אסלה 5284278 (קולטן בפינה, קיר כמעט-ניצב)

| שדה | ערך |
|---|---|
| `fixture.Location` | `(-143.1015, -67.9558, 4.7000)` |
| Sewer Connector Origin | **לא זמין - נדרשת הרצת Revit** |
| Sewer Connector BasisZ | **לא זמין** |
| Stub Start (מהקוד) | `(-143.1015, -67.9558, 4.7000)` = `fixture.Location` |
| Stub End (מהקוד) | `≈ (-142.9172, -68.0334, 4.7000)` (0.2 מ' לכיוון הקולטן) |
| מיקום הקולטן (סופי) | `(-141.4414, -68.6545, 4.7000)` |
| קיר חוסם | `5074863`, מרחק קולטן-לפינה **0.0000 מ'**, זווית קיר-מול-קו **87.96°** |
| `XY distance` (Location↔Connector) | **לא ניתן לחשב - Connector Origin חסר** |
| `Z difference` (Location↔Connector) | **לא ניתן לחשב** |

### מקרה D - דירה 1133, אסלה 5283870 (קולטן בפינה, המקרה הכי-קרוב-לפתרון)

| שדה | ערך |
|---|---|
| `fixture.Location` | `(-137.5999, -70.3299, 4.7000)` |
| Sewer Connector Origin | **לא זמין - נדרשת הרצת Revit** |
| Sewer Connector BasisZ | **לא זמין** |
| Stub Start (מהקוד) | `(-137.5999, -70.3299, 4.7000)` = `fixture.Location` |
| Stub End (מהקוד) | `≈ (-137.6826, -70.1478, 4.7000)` (0.2 מ' לכיוון הקולטן) |
| מיקום הקולטן (סופי) | `(-138.0306, -69.3808, 4.7000)` |
| קיר חוסם | `5074866`, מרחק קולטן-לפינה **0.0000 מ'**. הניסיון הכי-קרוב פגע בקיר **3.8 ס"מ** לפני סוף מקטע-הביניים. |
| `XY distance` (Location↔Connector) | **לא ניתן לחשב - Connector Origin חסר** |
| `Z difference` (Location↔Connector) | **לא ניתן לחשב** |

---

## 6. מה חסר כדי להכריע (למה הבדיקה לא הושלמה)

הבדיקה דורשת **נתוני Connector חיים** של 5 אסלות ספציפיות
(`5284771`, `5284278`, `5295055`, `5283870`, `5283989`) - נתונים
שקיימים **רק בזמן ריצה מול Revit** ומעולם לא נאספו/נשמרו בפרויקט.
לא ניתן להפיק אותם מקריאת-קוד או מהדוחות הקיימים.

### מה הפקודות הקיימות **כן** נותנות

| נתון מבוקש | פקודה קיימת | מכסה? |
|---|---|---|
| `fixture.Location` לכל אסלה | `DiscoverModelCommand` ("אבחון מודל") / הדוח הקיים | **כן** |
| נקודת-מוצא הצינור / הגדם | דוח `DrawPipesCommand` + `..._ManualEngineeringDiagnostics_*.txt` | **כן** (= `fixture.Location`) |
| נקודת-סיום הגדם | אותו דוח + חישוב דטרמיניסטי מהקוד | **כן** |
| Connector `Origin`/`BasisZ`/`ConnectorType`/`Domain`/`Shape`/`Radius` | `DiscoverFixtureSignatureCommand` ("אבחון חתימת-פיקסצ'ר") | **חלקית** - ראו מגבלות למטה |

### מגבלות `DiscoverFixtureSignatureCommand` לצורך הבדיקה הזו

1. הוא מקבץ לפי `Family+Type` ובודק **מופע-נציג אחד בלבד** (`instances[0]`) -
   [DiscoverFixtureSignatureCommand.cs:101-108](../src/PlumbingSystem.Revit/Commands/DiscoverFixtureSignatureCommand.cs#L101-L108).
   `Connector.Origin` הוא קואורדינטת-עולם מוחלטת ולכן **שונה לכל מופע**
   (וגם מושפע מסיבוב/שיקוף האסלה בתא) - הפלט לא ייתן בהכרח את
   ה-Origin של אף אחת מ-5 האסלות.
2. הוא **אינו מדפיס את ה-`LocationPoint`** של המופע לצד ה-Connectors,
   כך שאין בפלט השוואת `fixture.Location` ↔ `Connector Origin` באותו
   מקום.

### כדי להשלים - נדרשת אחת מהאפשרויות (ממתין להחלטת המשתמשת)

- **אפשרות A (בלי שינוי-קוד):** להריץ את `DiscoverFixtureSignatureCommand`
  הקיים, ולשלב את הפלט ידנית עם ערכי `fixture.Location` שכבר בדוח.
  נותן חתימת-Connector כללית + כיווני `BasisZ`, אך **לא** מספרי-Origin
  מדויקים ל-5 המקרים, ולא השוואה per-instance אמינה (בגלל סיבוב).
- **אפשרות B (שינוי-קוד זמני, ReadOnly, דורש אישור):** להרחיב את
  `DiscoverFixtureSignatureCommand` כך ש- (1) יעבור על **כל** המופעים
  ולא רק על נציג, ו-(2) ידפיס לכל מופע את `LocationPoint` + וקטור-
  ההפרש לכל Connector - **או** פקודת-אבחון ReadOnly זעירה חדשה שמקבלת
  את 5 ה-ElementId-ים ומדפיסה את ההשוואה המלאה.

**שום קוד מהאפשרויות האלה לא נכתב. נדרש אישור מפורש של המשתמשת לפני
כתיבתו.**

---

## 7. מסקנה

### לכל מקרה (A, B, D)

- **האם הגדם מתחיל מ-`fixture.Location`?** כן - ודאי, מהקוד (סעיף 4.1).
- **האם `fixture.Location` שונה משמעותית מ-Connector Origin?** **לא ידוע** -
  Connector Origin לא נאסף.
- **מה ההפרש ב-X/Y/Z?** לא ניתן לחשב עדיין.
- **האם הבדל ב-Z יכול להסביר את הגדם?** לא (סעיף 4.4) - בדיקת החסימה
  דו-ממדית; הפרש-Z לא משפיע על עצם הגדם, רק על השיפוע/גובה-החיבור.
- **האם כיוון הגדם שונה מכיוון ה-Connector?** ככל הנראה כן (כיוון הגדם
  הוא הכיוון האווירי לקולטן, לא `BasisZ` - סעיף 4.3), אך ההשוואה
  המספרית טרם בוצעה.
- **דפוס חוזר?** כן, אך לא לגבי ה-Connector: בכל 5 המקרים `fixture.Location.Z`
  זהה ל-Z של הקולטן, וב-4 מתוך 5 הקולטן יושב **בדיוק בפינת שני קירות**
  (מרחק 0.0000 מ').

### מסקנה כללית

**אפשרות 3 - אין מספיק מידע כדי לקבוע.**

- הראיה הקיימת (הדוח מ-2026-08-13) מייחסת את 4/5 המקרים ל**מיקום
  הקולטן בפינת-שני-קירות**, לא לנקודת-המוצא של האסלה. הכשל בכל 4
  המקרים האלה מתרחש **ליד הקולטן** (3-12 ס"מ ממנו), לא ליד האסלה.
- מנגד, נקודת-המוצא `fixture.Location` (במקום נקודת-יציאת-הביוב האמיתית)
  **לא נשללה** כתורם-שולי: מקרה D נכשל במרווח של 3.8 ס"מ בלבד, וסדר-
  גודל של היסט נקודת-מוצא (אם קיים כזה בין ה-`LocationPoint` ל-Connector)
  יכול להיות באותו טווח.
- כדי להכריע חסר **בדיוק דבר אחד**: גיאומטריית ה-Connector של 5 האסלות
  (`Origin` ו-`BasisZ`), שדורשת הרצת Revit - ראו סעיף 6, אפשרויות A/B.

---

## 8. מה לא שונה בעקבות הבדיקה

- **שום קובץ קוד לא שונה.** לא ב-Core, לא ב-Revit.
- **אלגוריתם הניתוב לא שונה** - `PipeRouteCalculator`, `DrawPipesCommand`,
  `RevitModelReader` זהים לחלוטין למצבם לפני הבדיקה.
- **לא נוצרה שום פקודת-אבחון חדשה**, ולא הורחבה קיימת.
- ההחלטות הפתוחות מ-[docs/pipe-mep-investigation.md](pipe-mep-investigation.md)
  (מעבר ל-Pipe אמיתי, לוגיקת-Fitting, נקודת-יציאה מ-Connector) נשארות
  פתוחות - המסמך הזה לא מכריע באף אחת מהן.

---

## 9. עדכון (RCA מלא של השרשרת) - ראו `docs/pipe-rca-chain.md`

בוצע RCA מסודר של כל שרשרת יצירת הצינור על 5 המקרים (חלקים א'+ב'+ג'
ב-[docs/pipe-rca-chain.md](pipe-rca-chain.md)). הממצא הרלוונטי למסמך
הזה:

- **נקודת ההתחלה** (`fixture.Location`, לא `Connector.Origin`) נשארה
  **UNKNOWN** - עדיין לא נאספו נתוני Connector חיים.
- אבל ה-RCA זיהה שורש אחר, **מוכח**, ל-4 מתוך 5 המקרים (B/C/D/E):
  `collector.Location` שווה בדיוק ל-`Endpoint` של קו-מיקום-הקיר החוסם,
  כלומר יעד-הניתוב יושב ~10 ס"מ **בתוך גוף-הקיר** (חצי מעובי 20 ס"מ,
  שנקרא מ-`Wall.Width`). מרחק-הפגיעה הקבוע של כל 28 החלופות (~10-12
  ס"מ מהקולטן, ללא תלות בפרמטרים) = חצי-עובי-הקיר. זה שלב 2 (מיקום
  הקולטן), לא שלב 4 (נקודת-התחלה).
- מקרה D נכשל ב-3.8 ס"מ - כלומר היסט-מוצא בסדר-גודל של ס"מ בודדים
  (אם קיים בין `LocationPoint` ל-Connector) עדיין **יכול** להיות
  רלוונטי כתורם-משני, אבל **אינו** ההסבר העיקרי.
- המסקנה הפתוחה: ה-engineering target הנכון של נקודת-החיבור לקולטן
  אינו מתועד (מסקנה "3 - Insufficient information" ב-`pipe-rca-chain.md`
  סעיף 30). השלב הבא הוא קבלת הכלל ההנדסי, לא שינוי קוד.

## 10. עדכון (2026-09-02) - הכלל ההנדסי אושר, ה-routing תוקן

המנהלת אישרה: **מותר לצינור הביוב להיכנס לתוך עובי הקיר כדי להגיע
לקולטן ולהתחבר אליו**. כלומר `collector.Location` על קו-אמצע-הקיר
בקצהו **תקין הנדסית** - הבעיה הייתה שה-routing חסם את הכניסה לקיר
הזה. `WallRayCasting.FindBlockingWallDetailed` + `DrawPipesCommand`
עודכנו כך שבדיקת-החסימה מתירה חדירה **לקיר שמכיל את הקולטן בלבד,
במקטע האחרון בלבד, עד לקולטן בלבד** - ראו `docs/pipe-rca-chain.md`
חלק ה' (סעיפים 40-46).

**לגבי נקודת-ההתחלה `fixture.Location` מול `Connector.Origin`** (הנושא
של המסמך הזה): נשאר **UNKNOWN** - לא נאספו נתוני Connector, ולא בוצע
שינוי בנקודת-ההתחלה. אם אחרי התיקון של חלק ה' יישאר מקרה גבולי
(למשל D, שנכשל קודם ב-3.8 ס"מ), החקירה של נקודת-ההתחלה עשויה לחזור
להיות רלוונטית - אבל רק אז.
