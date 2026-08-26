using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlumbingSystem.Revit;

/// <summary>
/// דיאלוג-בחירת-היקף משותף ("קומה פעילה בלבד" מול "כל הבניין") -
/// חולץ מ-<see cref="Commands.DrawPipesCommand"/> (שם נבנה במקור, ראו
/// docs/step7.md - "סינון-לפי-קומה") לכאן, כדי ש-
/// <see cref="Commands.PlaceCollectorsCommand"/> ו-<see cref="Commands.BuildCollectorsCommand"/>
/// יוכלו להציע את אותה בחירה בדיוק בלי לשכפל את קוד-הדיאלוג. הלוגיקה
/// עצמה (מה קורה עם <c>onlyLevelId</c> בתוך <see cref="RevitModelReader.ReadApartments"/>,
/// כולל החרגת-קומה-0) לא השתנתה בהעברה הזו - רק מיקום הקוד.
/// </summary>
public static class ScopeSelector
{
    /// <summary>
    /// מציגה דיאלוג-בחירת-היקף: "קומה פעילה בלבד" (ברירת המחדל, לפי
    /// <paramref name="activeLevel"/> - בד"כ <c>activeView.GenLevel</c>
    /// של הקורא) מול "כל הבניין". אם <paramref name="activeLevel"/> הוא
    /// null (תצוגה בלי Level משויך - למשל 3D) אין טעם להציע "קומה פעילה
    /// בלבד" בכלל, אז מוצגת רק אפשרות "כל הבניין" (+ Cancel).
    /// </summary>
    /// <param name="activeLevel">ה-Level של התצוגה הפעילה, אם יש (ראו למעלה).</param>
    /// <param name="activeFloorNumber">מספר-הקומה המפוענח של <paramref name="activeLevel"/> (<see cref="RevitModelReader.TryGetFloorNumber(Autodesk.Revit.DB.Level)"/>) - רק לצורך תצוגה בדיאלוג/בתיאור-ההיקף.</param>
    /// <param name="onlyLevelId">
    /// פלט: אם "קומה פעילה בלבד" נבחרה - <c>activeLevel.Id</c>. אם "כל
    /// הבניין" נבחרה - <c>null</c>. להעברה ישירה ל-
    /// <see cref="RevitModelReader.ReadApartments"/>.
    /// </param>
    /// <param name="scopeDescription">פלט: תיאור-קריא של ההיקף שנבחר, להדפסה בכותרת הדוח.</param>
    /// <returns>false אם המשתמש/ת ביטל/ה (Cancel) - הקורא אמור להחזיר Result.Cancelled.</returns>
    public static bool TryChooseScope(
        Level? activeLevel,
        int? activeFloorNumber,
        out ElementId? onlyLevelId,
        out string scopeDescription)
    {
        string floorText = activeFloorNumber is int fn
            ? $"Floor {fn}"
            : "קומה שמספרה לא זוהה משם ה-Level";

        var td = new TaskDialog("PlumbingSystem - היקף הרצה")
        {
            MainInstruction = "לעבד רק את הקומה הפעילה, או את כל הבניין?",
            MainContent = activeLevel is null
                ? "לתצוגה הפעילה אין Level מזוהה (למשל תצוגת-3D) - אפשר להריץ רק על כל הבניין. " +
                  "כדי לעבד קומה בודדת, פתחי תוכנית-קומה (Floor Plan) של הקומה הרצויה ונסי שוב."
                : $"התצוגה הפעילה שייכת ל-Level '{activeLevel.Name}' ({floorText}).",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            DefaultButton = TaskDialogResult.Cancel,
        };

        if (activeLevel is not null)
        {
            td.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                $"קומה פעילה בלבד ({activeLevel.Name})",
                "מעבד רק אסלות/דירות שנמצאות ב-Level הזה בדיוק. ברירת המחדל.");
        }

        td.AddCommandLink(
            activeLevel is null ? TaskDialogCommandLinkId.CommandLink1 : TaskDialogCommandLinkId.CommandLink2,
            "כל הבניין",
            "מעבד את כל הקומות במסמך.");

        TaskDialogResult result = td.Show();

        if (activeLevel is not null && result == TaskDialogResult.CommandLink1)
        {
            onlyLevelId = activeLevel.Id;
            scopeDescription = activeFloorNumber is int floorNumber
                ? $"Floor {floorNumber} only (Level '{activeLevel.Name}')"
                : $"Level '{activeLevel.Name}' only (floor number could not be parsed from its name)";
            return true;
        }

        bool wholeBuildingChosen =
            (activeLevel is not null && result == TaskDialogResult.CommandLink2) ||
            (activeLevel is null && result == TaskDialogResult.CommandLink1);

        if (wholeBuildingChosen)
        {
            onlyLevelId = null;
            scopeDescription = "entire building (all levels)";
            return true;
        }

        onlyLevelId = null;
        scopeDescription = string.Empty;
        return false;
    }
}
