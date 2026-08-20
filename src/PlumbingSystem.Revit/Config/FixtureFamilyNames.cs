namespace PlumbingSystem.Revit.Config;

/// <summary>
/// נקודת ההתאמה היחידה הנדרשת כדי להריץ את הזיהוי (RevitModelReader)
/// על בניין/פרויקט אחר: שמות המשפחות והמילות-מפתח שמזהות אסלות
/// ואלמנטים "רטובים" (אמבטיה/מקלחון) בתוך מודל Revit ספציפי. התאמה
/// לפרויקט חדש = עריכת שתי הרשימות כאן בלבד - בלי לגעת בלוגיקה עצמה
/// ב-<see cref="RevitModelReader"/>.
/// </summary>
/// <remarks>
/// זו נשארת בכוונה בשלב "קבועים בקוד" ולא "קונפיגורציה חיצונית"
/// (JSON/XML נטען בזמן ריצה) - זה overkill לצורך הנוכחי. המטרה כרגע
/// היא עריכה במקום אחד וברור, לא קונפיגורציה דינמית; אם וכשיהיה צורך
/// אמיתי בהחלפת קבצי קונפיגורציה בלי rebuild, זה שיפור עתידי נפרד.
///
/// הקובץ הזה יושב ב-PlumbingSystem.Revit (לא ב-Core) למרות שהוא לא
/// תלוי ב-RevitAPI בעצמו: שמות משפחות Revit הם בעצם ידע על *איך מזהים
/// אלמנטים במודל Revit ספציפי* - זו בדיוק האחריות של שכבת ה-Revit
/// (המתרגם בין Revit לבין מודל הדומיין), לא ידע עסקי כללי שרלוונטי
/// גם בלי Revit בכלל, כמו שאר מה שיושב ב-Core.
/// </remarks>
public static class FixtureFamilyNames
{
    /// <summary>
    /// שמות משפחות Revit שמזוהות כ"אסלה" (השוואה מדויקת ל-Family.Name,
    /// לא Contains). רשימה ולא מחרוזת בודדת, כי בפרויקטים עתידיים
    /// יכולות להיות כמה משפחות אסלה שונות (יצרנים/דגמים שונים) שכולן
    /// צריכות להיכנס לאותה לוגיקת זיהוי.
    ///
    /// זו כבר לא נקודת-הזיהוי הראשית - <see cref="RevitModelReader.IsToiletFixture"/>
    /// בודק קודם Type Parameter (<c>Is_Toilet</c>, Yes/No) על ה-FamilySymbol.
    /// הרשימה הזו נשארת כ-Fallback עבור Types שעדיין לא קיבלו את הפרמטר
    /// (כולל הפרויקט הנוכחי, נכון לעכשיו) - ראו האזהרות ב-Warnings בזמן ריצה.
    /// </summary>
    public static readonly IReadOnlyList<string> ToiletFamilyNames = new List<string>
    {
        "If_toilet_wall_hung_6505",
    };

    /// <summary>
    /// מילות מפתח לזיהוי אלמנטים "רטובים" (אמבטיה/מקלחון) - נבדקות
    /// בהכלה (Contains) בתוך שם המשפחה, לא בשוויון מדויק, כי כמה משפחות
    /// שונות מכילות את אותה מילת מפתח (למשל "P_מקלחון").
    /// </summary>
    public static readonly IReadOnlyList<string> WetFixtureKeywords = new List<string>
    {
        "אמבטיה",
        "מקלחון",
    };
}
