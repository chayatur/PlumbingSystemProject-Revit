# שלב 1 - שלד הסולושן

## מה נבנה בשלב הזה

שלד סולושן ריק פונקציונלית אך מלא מבנית, בשם `PlumbingSystem`, עם שלושה
פרויקטים:

```
PlumbingSystem.sln
├── src/
│   ├── PlumbingSystem.Core/       (Class Library, net10.0)
│   └── PlumbingSystem.Revit/      (Class Library, net10.0-windows)
│       ├── App.cs                 - IExternalApplication
│       ├── Commands/
│       │   └── ReadElementsCommand.cs  - IExternalCommand
│       └── PlumbingSystem.addin   - manifest לדוגמה
└── tests/
    └── PlumbingSystem.Core.Tests/ (xUnit, net10.0)
```

בשלב הזה אין עדיין לוגיקה עסקית. המטרה היחידה היא לוודא שהתוסף **נטען
ב-Revit ומגיב** - כפתור "בדיקת חיבור" שמציג TaskDialog עם "עובד". זו נקודת
מוצא בטוחה להמשך הפיתוח: כל תכונה עתידית תיבנה על שלד שכבר הוכח כתקין.

## למה יש הפרדה בין Core ל-Revit

**PlumbingSystem.Core** אחראי (בעתיד) על כל הלוגיקה שלא תלויה ב-Revit
עצמו: מודלים, חישובים, כללים עסקיים, ולידציות וכו'. הוא מטורגט ל-`net10.0`
"רגיל" (בלי `-windows`) ובלי שום Reference ל-`RevitAPI`/`RevitAPIUI`
בכוונה תחילה. הסיבה:

1. **בדיקות מהירות ואמינות** - `PlumbingSystem.Core.Tests` יכול לרוץ עם
   `dotnet test` על כל מכונה (כולל שרת CI), בלי שיהיה מותקן שם Revit
   בכלל. אילו Core היה תלוי ב-RevitAPI, כל בדיקה הייתה דורשת Revit מותקן
   ורישיון תקף - זה יקר, איטי, ולא ניתן להרצה אוטומטית בקלות.
2. **הפרדת אחריות** - לוגיקה עסקית (חישובי אינסטלציה, כללים) לא אמורה
   לדעת בכלל ש-Revit קיים. זה מאפשר לבדוק אותה בבידוד, ואם בעתיד יהיה
   צורך לתמוך בפלטפורמה נוספת (למשל exporter עצמאי, כלי CLI), Core כבר
   מוכן לשימוש חוזר.
3. **מניעת דליפת תלות** - ברגע ש-Core "יתפתה" להשתמש ב-`Autodesk.Revit.DB`
   פעם אחת, קשה מאוד לחזור אחורה. השלד אוכף את ההפרדה מהיום הראשון.

**PlumbingSystem.Revit** הוא שכבת האינטגרציה: הוא זה שבאמת נטען לתוך
תהליך Revit (`IExternalApplication`, `IExternalCommand`), מציג UI
(ריבון, TaskDialog), וקורא/כותב למודל דרך RevitAPI. הוא מטורגט ל-
`net10.0-windows` כי הוא זקוק ל-Windows APIs (ולדרישת Revit 2027 ל-.NET
10). הוא מחזיק `ProjectReference` ל-Core, כלומר: **Revit תלוי ב-Core, לא
להפך**. כיוון התלות הזה הוא-הוא מה שמאפשר את כל היתרונות שתוארו למעלה.

**PlumbingSystem.Core.Tests** בודק רק את Core (Reference אחד בלבד), מאותה
סיבה - אין לו שום דרך לגעת ב-RevitAPI, וזה נכון ומכוון.

## איך לבדוק שהשלד עובד

### בדיקת Core + Tests (לא דורש Revit מותקן)

דורש **.NET 10 SDK** מותקן (Revit 2027 API בנוי עליו, אז ממילא יידרש
במחשב הפיתוח). הפרויקט משתמש ב-`xunit.v3` (ולא ב-xUnit 2.x) כי חבילות
xUnit הישנות (מבוססות `netstandard1.1`) לא נפתרות נכון מול `net10.0` -
`xunit.v3` היא הגרסה הנתמכת רשמית ל-.NET 8 ומעלה:

```powershell
dotnet build PlumbingSystem.sln
dotnet test tests/PlumbingSystem.Core.Tests/PlumbingSystem.Core.Tests.csproj
```

הבדיקה `Sanity_OnePlusOneEqualsTwo` אמורה לעבור בירוק. זה לא בודק שום
דבר "אמיתי" עדיין - רק מוכיח שה-runner, ה-references וה-target framework
תקינים.

### בדיקת ה-Add-in בתוך Revit (דורש Revit 2027 מותקן)

1. לוודא ש-`RevitAPI.dll` ו-`RevitAPIUI.dll` אכן נמצאים בנתיב
   `C:\Program Files\Autodesk\Revit 2027\` (אם ההתקנה בנתיב אחר, לעדכן
   את ה-`HintPath` בקובץ `src/PlumbingSystem.Revit/PlumbingSystem.Revit.csproj`).
2. `dotnet build src/PlumbingSystem.Revit/PlumbingSystem.Revit.csproj`.
3. ליצור GUID חדש (למשל `New-Guid` ב-PowerShell) ולהחליף את
   `{{ADDIN-GUID}}` בקובץ `PlumbingSystem.addin`.
4. להחליף את `{{PATH-TO-DLL}}` בנתיב המלא לתיקיית ה-build (למשל
   `...\src\PlumbingSystem.Revit\bin\Debug\net10.0-windows`).
5. להעתיק את `PlumbingSystem.addin` לתיקיית
   `%ProgramData%\Autodesk\Revit\Addins\2027\`.
6. לפתוח Revit 2027 → אמורה להופיע לשונית **Startarc** עם פאנל
   **אינסטלציה** וכפתור **בדיקת חיבור**. לחיצה עליו מציגה TaskDialog עם
   הטקסט "עובד".

אם הכפתור מופיע ומציג את הדיאלוג - השלד תקין וניתן להתחיל לבנות עליו
פונקציונליות אמיתית.
