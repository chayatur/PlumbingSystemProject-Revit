# שלב 2 - טעינת ה-Add-in בפועל בתוך Revit

## מה זה קובץ .addin, ולמה Revit סורק את התיקייה הזו

Revit לא סורק אוטומטית תיקיות `bin` של פרויקטים - הוא לא יודע בכלל
ש-`PlumbingSystem` קיים. הדרך היחידה שהוא "מגלה" Add-in היא דרך קובץ
**manifest** בפורמט XML עם סיומת `.addin`, שמונח באחת מתיקיות סטנדרטיות
שהוא סורק בעליה:

- **פר-משתמש**: `%AppData%\Autodesk\Revit\Addins\2027\` (זו התיקייה שבה
  אנחנו משתמשים בפרויקט הזה)
- **כלל-המחשב**: במוסכמה הרגילה `%ProgramData%\Autodesk\Revit\Addins\2027\`
  - אבל ב-build הספציפי של Revit 2027 שמותקן כאן, הוא שונה (ראו "תקלה
    שנתקלנו בה" למטה).

כל קובץ `.addin` שנמצא שם מתואר בו `<Assembly>` (הנתיב המלא ל-DLL של
ה-Add-in) ו-`<FullClassName>` (המחלקה שמיישמת `IExternalApplication` או
`IExternalCommand`). ב-Startup, Revit קורא את כל קבצי ה-`.addin` בתיקייה,
טוען את ה-DLL-ים שהם מצביעים אליהם באמצעות Reflection, ומפעיל את המחלקה
המצוינת. ה-`AddInId` (GUID) הוא המזהה הקבוע של ה-Add-in לאורך זמן - הוא
לא אמור להשתנות בין גרסאות/build-ים, כדי ש-Revit (ותוספים אחרים שתלויים
בו) יזהו אותו בעקביות.

בפרויקט שלנו, `PlumbingSystem.addin` מצביע כרגע ל:

```
C:\Users\user1\Desktop\STARTARC_PROJECT\src\PlumbingSystem.Revit\bin\Debug\net10.0-windows\PlumbingSystem.Revit.dll
```

עם `AddInId` = `9968f457-9f00-41a5-abd2-c0b1e672b45c` ו-`FullClassName` =
`PlumbingSystem.Revit.App`.

## למה יש Post-Build Event

בלי אוטומציה, כל build היה דורש להעתיק ידנית שני קבצים (ה-DLL המעודכן
וה-`.addin`) לתיקיית ה-Addins של Revit - שלב שקל לשכוח, ואז מתדפקים על
"למה השינויים שלי לא מופיעים ב-Revit" בזמן שבפועל ה-DLL הישן עדיין טעון.

לכן ב-`PlumbingSystem.Revit.csproj` הוגדר Target בשם `CopyAddinToRevit`
עם `AfterTargets="Build"`, שמעתיק אוטומטית בכל build:

1. `PlumbingSystem.Revit.dll`
2. `PlumbingSystem.Revit.pdb` (אם קיים - מאפשר Debug עם breakpoints בתוך
   Revit)
3. `PlumbingSystem.addin`

לתיקיית `%AppData%\Autodesk\Revit\Addins\2027\` (הפר-משתמשית - ראו
"תקלה שנתקלנו בה" למטה לגבי למה לא `%ProgramData%`).

השתמשנו ב-Target עם `<Copy>` Task של MSBuild (ולא ב-`PostBuildEvent`
עם `xcopy`/`copy` גולמי), כי `PostBuildEvent` הוא מנגנון legacy
מ-`.csproj` הישן (non-SDK-style) שפחות אמין ב-SDK-style csproj מודרני
(תלוי ב-cmd.exe, לא חוצה פלטפורמות, ולא משתלב טוב עם ה-MSBuild
dependency graph). `<Target AfterTargets="Build">` הוא הדרך הנתמכת
והמומלצת - הוא MSBuild-native, מדלג אוטומטית אם שום דבר לא השתנה (עם
`SkipUnchangedFiles`), ומופיע כשלב רגיל בלוג ה-build.

חשוב: התיקייה יכולה לא להתקיים עדיין (למשל בהתקנת Revit נקייה בלי
Add-ins אחרים), לכן ה-Target כולל גם `<MakeDir>` שיוצר אותה אם צריך.

## תקלה שנתקלנו בה: %ProgramData% נדחה בשקט

בסבב הפיתוח הראשון הנחנו (בטעות) שהתיקייה הכלל-מערכתית הסטנדרטית
`%ProgramData%\Autodesk\Revit\Addins\2027\` תעבוד, כמו בגרסאות Revit
ותיקות יותר. Revit עלה נקי, בלי שום דיאלוג שגיאה, אבל גם בלי הטאב
Startarc - כשל **שקט** לחלוטין.

הדרך שגילינו את הסיבה האמיתית: קובצי ה-**journal** של Revit (ב-
`%LocalAppData%\Autodesk\Revit\Autodesk Revit 2027\Journals\journal.NNNN.txt`)
מתעדים כל ניסיון טעינת Add-in, גם ניסיונות שלא מניבים דיאלוג למשתמש.
חיפוש בהם אחרי `PlumbingSystem` העלה את השורה:

```
Add-in manifest file from: C:\ProgramData\Autodesk\Revit\Addins\2027\PlumbingSystem.addin,
won't be loaded. All-users Add-in manifest files must be installed to:
C:\Program Files\Autodesk\Revit\Addins\2027
```

כלומר: ב-build הזה של Revit 2027, תיקיית ה-**all-users** זזה מ-
`%ProgramData%` ל-`C:\Program Files\Autodesk\Revit\Addins\2027`. בדיקה
הראתה שהתיקייה הזו דורשת הרשאות **Administrator** לכתיבה (Access
Denied למשתמש רגיל) - כלומר Post-Build Event רגיל לא יכול לכתוב
לשם בלי הרצת Visual Studio/הבנייה כ-Administrator.

הפתרון שבו אנחנו משתמשים: תיקיית ה-Add-ins **הפר-משתמשית**
(`%AppData%\Autodesk\Revit\Addins\2027\`, תחת Roaming AppData), שכבר
נוצרת ע"י Revit עצמו בהתקנה, וניתנת לכתיבה בלי הרשאות מיוחדות. לסביבת
פיתוח יחיד-משתמש זו האופציה הנוחה יותר ממילא; אם בעתיד יידרש להפיץ
את התוסף לכלל המשתמשים במחשב, יהיה צריך להתקין ל-`Program Files` דרך
installer שרץ מוגבה (MSI/EXE עם UAC elevation), לא דרך Post-Build
Event בזמן פיתוח.

**המסקנה החשובה**: "אין הודעת שגיאה" ב-Revit **לא** אומרת שהטעינה
הצליחה או אפילו שהיא נוסתה - חלק מהמקרים (כמו manifest בתיקייה
שגויה) נכשלים לגמרי בשקט, וה-journal הוא מקור האמת האמיתי היחיד
לבדיקה במקרה כזה.

## איך לוודא שהתוסף נטען בהצלחה ב-Revit

1. לוודא שהקבצים אכן נמצאים בתיקיית ה-Addins (אחרי `dotnet build`):
   ```powershell
   dir "$env:AppData\Autodesk\Revit\Addins\2027\"
   ```
   אמורים להופיע: `PlumbingSystem.Revit.dll`, `PlumbingSystem.Revit.pdb`,
   `PlumbingSystem.addin`.
2. לפתוח Revit 2027 (אם הוא כבר פתוח - **לסגור לגמרי ולפתוח מחדש**;
   Revit סורק את תיקיית ה-Addins רק בעליה, לא תוך כדי ריצה).
3. **אם הטעינה הצליחה**: תופיע לשונית ריבון חדשה בשם **Startarc**, ובתוכה
   פאנל **אינסטלציה** עם כפתור **בדיקת חיבור**. לחיצה עליו מציגה
   TaskDialog עם הטקסט "עובד".
4. **אם הטעינה נכשלה עם דיאלוג**: Revit מציג בעליה (לפני שהוא מסיים לטעון
   את הממשק) דיאלוג שגיאה בשם "Load Error" / "שגיאת טעינה", שמפרט את שם
   קובץ ה-`.addin` הבעייתי ואת סיבת הכשל (למשל: DLL לא נמצא בנתיב
   שצוין, שגיאת טעינת Assembly, אי-התאמת גרסת .NET, או שהמחלקה שצוינה
   ב-`FullClassName` לא נמצאה/לא ממשה `IExternalApplication`). זו נקודת
   הבדיקה הראשונה כשמשהו לא עובד - הדיאלוג הזה בדרך כלל מצביע במדויק על
   הבעיה.
5. **אם אין דיאלוג שגיאה וגם אין לשונית Startarc** (כשל שקט) - הדיאלוג
   לא תמיד מופיע (ראו הסעיף הקודם). הבדיקה האמינה היחידה במקרה כזה היא
   ה-**journal** של Revit:
   ```powershell
   Get-ChildItem "$env:LocalAppData\Autodesk\Revit\Autodesk Revit 2027\Journals" -Filter journal.*.txt |
     Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
     Get-Content | Select-String "addin","PlumbingSystem" -CaseSensitive:$false
   ```
   חפשו שורות עם `PlumbingSystem` - אם Revit ניסה וסירב לטעון, השורה
   תפרט למה (למשל תיקייה שגויה, כמו שקרה לנו). אם אין אף אזכור של
   `PlumbingSystem` בכלל - Revit אפילו לא ראה את הקובץ, כנראה כי הוא
   בתיקייה שגויה או שלא בוצע restart אחרי ה-build.
