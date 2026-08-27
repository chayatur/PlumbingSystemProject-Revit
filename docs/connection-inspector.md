# Connection Inspector - פאנל-מעוגן לבדיקת קשרי אסלה↔קולטן↔צינור

תיעוד נפרד מ-`step7.md` (לוגיקת-ניתוב), `client-report.md` (דוח-לקוח
HTML) ו-`progress-infrastructure.md` (חלון-התקדמות-חי) - זה עוסק
**רק** בפאנל-הבדיקה האינטראקטיבי. `src/PlumbingSystem.Revit/Inspector/`
(5 קבצים) + חיווט ב-`App.cs`.

## 1. מה זה, ולמה בלי חישוב-מחדש

המנהלת ביקשה כלי אינטראקטיבי: קליק על אסלה מציג לאיזה קולטן היא
מחוברת; קליק על קולטן מציג אילו אסלות מחוברות אליו; עם אפשרות
Highlight. **הממצא המרכזי שהוביל לארכיטקטורה**: הקשר הזה **כבר
persisted במודל עצמו**, לא רק בזיכרון-חולף של הרצת-פקודה - `DrawPipesCommand`
ו-`CollectorPlacementService` כבר כותבים אותו, מאז ומתמיד, דרך
`Mark`/`Comments`/`Name` על כל אלמנט שהם יוצרים:

| אלמנט | `Mark`/`Comments`/`Name` | מקור |
|---|---|---|
| Collector (DirectShape) | `"COL-{fixtureId}"` | `CollectorPlacementService.cs:606-609` |
| Pipe (DirectShape, גם תקין וגם דורש-ידני) | `"PIPE-{fixtureId}-COL-{fixtureId}"` | `DrawPipesCommand.cs:918-921` |

המשמעות: `Connection Inspector` **לא קורא בשום שלב** ל-`PipeRouteCalculator`,
`CollectorLocator`, `WallEdgeSnapper` או `RevitModelReader` - הוא רק
סורק `FilteredElementCollector` ומפענח מחרוזות שכבר כתובות. אין
"חישוב מחדש" בכלל, לא בגלל משמעת-קוד אלא כי **הנתון כבר שם**.

## 2. הקבצים

| קובץ | תפקיד |
|---|---|
| `ElementRelationshipLookup.cs` | **הלוגיקה היחידה** - קוראת-בלבד. `TryDescribe(doc, elementId)` מחזירה קשר-שכבר-קיים או `null`. |
| `ConnectionInspectorViewModel.cs` | `INotifyPropertyChanged` - מתרגם `RelationshipInfo` (שכבר פוענח) למשפטים ולרשימת-Highlight. |
| `ConnectionInspectorView.xaml` / `.xaml.cs` | `UserControl` (לא `Window` - `DockablePane` דורש `FrameworkElement` רגיל) שמוצג בפאנל. |
| `ConnectionInspectorPaneProvider.cs` | `IDockablePaneProvider` - נרשמת ב-`App.OnStartup`, לפני שנפתח מסמך. |

**שינוי יחיד בקובץ קיים**: `App.cs` - רישום הפאנל + מנוי ל-`SelectionChanged`
(ראו סעיף 4). **אפס שינוי** ב-`PipeRouteCalculator.cs`, `WallEdgeSnapper.cs`,
`CollectorLocator.cs`, `RevitModelReader.cs`, `DrawPipesCommand.cs`,
`CollectorPlacementService.cs`, או כל קובץ ב-`PlumbingSystem.Core`.

## 3. איך הפענוח עובד (בלי לזהות "זו אסלה" בכלל)

**החלטה מכוונת**: כדי לא לשכפל/לחשוף את `RevitModelReader.IsToiletFixture`
(פרטית, תלוית-state), הזיהוי "זה רלוונטי" נעשה **בעקיפין**: לא נבדקת
קטגוריה/שם-משפחה של האלמנט הנבחר בכלל. במקום זה:

- **קולטן נבחר** (מזוהה לפי `Name` שמתחיל ב-`"PlumbingSystem Collector "`) →
  קוראים את ה-`Mark` שלו (`"COL-{x}"`) → סורקים כל הצינורות שה-`Mark`
  שלהם **מסתיים** ב-`"-COL-{x}"` → זה נותן את **כל** האסלות המחוברות.
- **אסלה נבחרה** → יש כבר `ElementId` שלה (מהבחירה עצמה) → סורקים צינורות
  שה-`Mark` שלהם **מתחיל** ב-`"PIPE-{id}-COL-"` → נותן את הקולטן+הצינור.
- **אף אחד מהשניים** → `null` → הפאנל מציג "לא-רלוונטי", לא משנה שום דבר.

**מגבלה ידועה, לא תקלה**: אסלה שעדיין לא עובדה (לא רץ "צייר צינורות"
על הקומה שלה) תוצג כ"לא-רלוונטי" - כי אין עדיין שום צינור שמצביע
עליה. זה עקבי עם "אין חישוב-מחדש" - הכלי הזה **רק** קורא תוצאות
שכבר קיימות, לא מריץ ניתוב-חדש.

**מצב הצינור** (תקין/דורש-ידני) נגזר מ-**איזה Material מוחל בפועל**
על האלמנט (`GetMaterialIds`, השוואה ל-`"PlumbingSystem Manual Engineering Orange"`) -
אותה טכניקה שכבר קיימת ב-`DrawPipesCommand.DescribeActualMaterials`,
לא לוגיקה חדשה.

**כפילות-קבועים מכוונת ומתועדת**: `CollectorNamePrefix`, `PipeNamePrefix`
ו-`ManualEngineeringMaterialName` ב-`ElementRelationshipLookup.cs` הם
עותקים מדויקים של קבועים `private` מקבילים ב-`CollectorPlacementService.cs`/
`DrawPipesCommand.cs`. **הוחלט במפורש לא לחשוף אותם** (`private`→`internal`)
כדי לא לגעת בקבצי-ליבה קיימים ללא צורך - סיכון-תחזוקה קטן ומודע
(אם הקבועים המקוריים ישתנו, הקובץ הזה צריך עדכון תואם), לא פספוס.

## 4. `SelectionChanged` + `IDockablePane` - הרכיבים החדשים היחידים

- `UIControlledApplication.SelectionChanged` (namespace `Autodesk.Revit.UI.Events`) -
  event שנרשם **פעם אחת** ב-`OnStartup`, יורה אוטומטית בכל שינוי-בחירה
  בכל מסמך פתוח. `sender` הוא ה-`UIApplication` (לא `UIControlledApplication`) -
  כך מקבלים `ActiveUIDocument` בזמן-האירוע.
- `IDockablePaneProvider`/`RegisterDockablePane` - Revit מוסיף אוטומטית
  את הפאנל לרשימה תחת **View → User Interface** - אין צורך בכפתור-ריבון.
  `DockablePaneId` הוא GUID **קבוע-לתמיד** (`ConnectionInspectorPaneProvider.PaneId`) -
  Revit עשוי לשמור state (מיקום/גודל/נראות) לפיו בין הרצות.

**אימות-API בפועל, לא הנחה**: נכתב, נבנה, ותוקנו שגיאות-קומפילציה
אמיתיות מול ה-DLL האמיתי (לא ניחוש מהזיכרון) - `IDockablePaneProvider`,
`DockablePaneProviderData`, `DockablePaneState`, `DockPosition`,
`RegisterDockablePane`, `SelectionChangedEventArgs.GetSelectedElements()`
כולם קומפלו נכון בפעם הראשונה; התאמה יחידה שנדרשה:
`SelectionChangedEventArgs` **אין לה** `.Document` - נפתר בעזרת
`((UIApplication)sender).ActiveUIDocument.Document` במקום. זו לא
"בעיית lifecycle משמעותית" (המשתמשת ביקשה לעצור על כזו) - התאמת-API
טריוויאלית, לא סיכון ארכיטקטוני.

## 5. Highlight

`UIDocument.Selection.SetElementIds(ICollection<ElementId>)` - פעולת-UI
טהורה (בלי Transaction, בלי שינוי-מודל) שמחליפה את הבחירה הנוכחית
לכל האלמנטים הקשורים. כפתור "Highlight Related" בפאנל, מנוטרל
(`IsEnabled`) כשאין קשר להציג.

**עדכון (2026-08-27) - נוסחה אחידה לשלושת סוגי-הבחירה**: אחרי בדיקה
אמיתית ב-Revit אושר ש-Toilet כבר כלל 3 אלמנטים נכון (ראו סעיף 6),
אבל Collector כלל רק את עצמו+הצינורות - **לא** את האסלות המחוברות
(הפער: `ConnectedPipe.FixtureId` היה מחרוזת בלבד, אף פעם לא הומר
ל-`ElementId`). תוקן על ידי הרחבת `ConnectedPipe` בשני שדות חדשים -
`FixtureElementId` (הומר מהמחרוזת, עם בדיקת-קיום) ו-`ApartmentLabel` -
ובניית-הרשימה השתנתה מ-`List` ל-`HashSet<ElementId>` (מבטל כפילויות,
כמו קולטן-שנבחר שהיה נכנס פעמיים):
```
relatedIds = { הנבחר } ∪ { הקולטן, אם נמצא }
           ∪ { לכל צינור-בתוצאה: הצינור עצמו, האסלה שלו אם נמצאה }
```
נוסחה **אחת**, זהה לשלושת ה-Kind - Fixture נותנת {אסלה,קולטן,צינור},
Collector נותנת {קולטן, כל-הצינורות, כל-האסלות}, Pipe נותנת
{אסלה,צינור,קולטן} - בלי branching נפרד בקוד ל-Highlight עצמו.

**באג שהתגלה ותוקן (2026-08-27) - Highlight מאפס את הפאנל**: נבדק
בפועל ב-Revit - לחיצה על "Highlight Related" הדגישה נכון, אבל
הפאנל **התאפס מיד** ל-"בחר/י אלמנט". **השורש**: `SetElementIds`
משנה את הבחירה **בפועל**-ב-Revit, מה שמעורר `SelectionChanged`
**נוסף** - בדיוק אותו אירוע ש-`App.OnSelectionChanged` כבר מאזין
לו. מכיוון שההדגשה כוללת כמה אלמנטים (למשל 3), ה-handler ראה
`selected.Count != 1` וקרא ל-`ShowNoSelection()` - לולאת-משוב
עצמית: הפעולה שלנו-עצמנו איפסה את מה שהיא-עצמה הייתה אמורה להציג.

**התיקון**: דגל-דיכוי (`_suppressNextSelectionChanged` ב-
`ConnectionInspectorViewModel`) שמסומן **ממש-לפני** ש-`Highlight()`
קוראת ל-`SetElementIds`, ונצרך (consumed) בתחילת `App.OnSelectionChanged` -
אם דלוק, האירוע הזה מתעלם-לגמרי (גם לא מעדכן `CurrentUiDocument`),
בלי לגעת בשום מצב בפאנל. נצרך פעם אחת בלבד - כל `SelectionChanged`
עתידי (בחירה אמיתית של המשתמש/ת) מטופל כרגיל.

## 6. תמיכה מלאה בשלושת הסוגים (עדכון 2026-08-27)

**Toilet** - זיהוי ללא שינוי. תצוגה הורחבה: קולטן **וגם ElementId שלו**
(לא רק "COL-מספר"), **Route ID מלא**, **מספר-דירה** (חדש - ראו סעיף 3
המעודכן), וסטטוס.

**Collector** - זיהוי ללא שינוי. תצוגה שונתה מספירה-כללית לרשימה
אמיתית (`ObservableCollection<string>`, `ItemsControl` חדש ב-XAML) -
שורה לכל אסלה מחוברת: `ElementId`, דירה, וסטטוס **ספציפי-לאותה-אסלה**
(לא סטטיסטיקה מצרפית).

**Pipe (חדש לגמרי)** - ענף-זיהוי שלישי ב-`TryDescribe`: אם ה-`Name`
מתחיל ב-"PlumbingSystem Pipe " - פענוח ה-`Mark` של **האלמנט-הנבחר-
עצמו** (לא סריקת-אחרים - הוא כבר בידינו), אותה שרשרת-בירור בדיוק כמו
Fixture. מציג: אסלה-מקור (+דירה), קולטן-יעד (+ElementId), Route ID,
וסטטוס - כל הפרטים שהתבקשו.

**מקור מספר-הדירה** (חדש בכל שלושת המקרים): `Document.GetRoomAtPoint`
על מיקום-האסלה-המקורית - קריאת-Room-קיים, לא שיוך-קולטן-מחדש. אותה
טכניקה בדיוק כמו `RevitModelReader.CollectFixturesWithRoom`/
`CollectorPlacementService.GetRoomContext`. מחושב **רק** על תוצאות-
סופיות-מסוננות (לא על כל צינור במסמך) - שיקול-ביצועים, ב-`Enrich`
(שלב נפרד מ-`ParsePipe` הזול).

## 7. אימות טכני

`dotnet build` (מלא, `--no-incremental`) - 0 שגיאות, 2 אזהרות (אותו
בסיס-קיים-מראש, ללא שינוי) - אומת שוב אחרי תיקון באג-הדיכוי (סעיף 5).
`dotnet test` - 62/62 עובר (Core לא נגע כלל בשום שלב). DLL נפרש
בפועל, timestamp זהה-בדיוק ל-build (2026-08-27 22:16:16).

**אומת ידנית ב-Revit, חלקית**: Toilet ו-Collector נבדקו ואושרו
(זיהוי נכון בשני המקרים; Highlight הדגיש נכון בפועל בשני המקרים,
אחרי תיקון-הדיכוי). **טרם אומת ידנית**: Pipe (חדש), הרשימה-המפורטת-
לקולטן, מספרי-הדירה, ו-**שהפאנל אכן נשאר-עם-הנתונים** (לא מתאפס)
אחרי Highlight - זה בדיוק מה שתוקן הפעם, דורש בדיקה-חוזרת. נקודות
ספציפיות לבדוק:
1. קליק על אסלה - הפאנל מציג דירה + Route ID מלא + ElementId-של-הקולטן (לא רק תווית קצרה).
2. קליק על קולטן - רשימה אמיתית (לא רק ספירה) - שורה לכל אסלה, עם דירה וסטטוס נפרד.
3. **קליק על צינור עצמו** (לא על האסלה/הקולטן) - האם הפאנל מזהה אותו בכלל ומציג אסלה+קולטן+Route ID+סטטוס.
4. **החדש**: אחרי לחיצה על "Highlight Related" - כל האלמנטים מודגשים חזותית **וגם** הפאנל נשאר עם אותם נתונים (לא מתאפס ל-"בחר/י אלמנט"), על שלושת הסוגים.
5. אסלה עם דירה-לא-ניתנת-לזיהוי (Room=null) - צריכה להציג "(לא ידוע)", לא שגיאה.
