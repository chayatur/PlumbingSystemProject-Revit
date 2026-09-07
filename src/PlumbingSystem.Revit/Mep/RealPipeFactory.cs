using Autodesk.Revit.DB;
using PlumbingSystem.Core.Models;

// alias בלבד - import מלא של Autodesk.Revit.DB.Plumbing היה יוצר
// התנגשות בין ה-PipeSegment של Revit ל-PlumbingSystem.Core.Models.PipeSegment.
using RevitPipe = Autodesk.Revit.DB.Plumbing.Pipe;

namespace PlumbingSystem.Revit.Mep;

/// <summary>
/// יצירת <c>Autodesk.Revit.DB.Plumbing.Pipe</c> אמיתי של Revit MEP
/// ממקטע-מסלול בודד (<see cref="PipeSegment"/>) שכבר חושב על ידי
/// <c>PipeRouteCalculator</c> (Core). **PIPE Step 5** - משמש רק את הנתיב
/// של **מסלול ישר תקין** ב-<c>DrawPipesCommand</c>. את דפוס הקוד הזה
/// הוכיח <c>CreateTestPipeCommand</c> (PIPE Step 4).
/// </summary>
/// <remarks>
/// **לא יוצר <see cref="DirectShape"/>** ולא נוגע בגיאומטריית-המסלול:
/// נקודות ההתחלה/סיום (כולל ה-<c>Z</c> שכבר מגלם את השיפוע) מגיעות
/// כמות-שהן מ-<c>segment</c>. <c>levelId</c> נדרש ל-<c>Pipe.Create</c>
/// כ-Reference Level בלבד - **אינו** משנה את גובה הצינור (הגיאומטריה
/// נקבעת מהנקודות).
///
/// **זהות**: <c>Comments</c> **וגם** <c>Mark</c> מקבלים את <c>routeId</c>
/// (קידומת <c>"PIPE-"</c>) - לצינור אמיתי אי-אפשר לקבוע <c>Element.Name</c>
/// (הוא שם ה-<c>PipeType</c>), לכן הזיהוי (מחיקה / Connection Inspector)
/// מסתמך על <c>Mark</c>/<c>Comments</c>.
///
/// **כשל**: אם <c>Pipe.Create</c> או הגדרת-פרמטר נכשלים - נזרקת חריגה,
/// וה-<c>Transaction</c> של הקורא מגלגל אחורה את **כל** ההרצה. אין
/// fallback ל-<c>DirectShape</c> עבור מסלול שאמור להיות Pipe.
/// </remarks>
internal static class RealPipeFactory
{
    /// <summary>
    /// יוצר Pipe יחיד למקטע <paramref name="segment"/>, מגדיר קוטר
    /// (<paramref name="diameterMm"/>) וסימון (<paramref name="routeId"/>),
    /// ומחזיר אותו. חייב לרוץ בתוך <c>Transaction</c> פתוח.
    /// </summary>
    public static RevitPipe Create(
        Document doc,
        ElementId systemTypeId,
        ElementId pipeTypeId,
        ElementId levelId,
        PipeSegment segment,
        double diameterMm,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeId);

        XYZ start = RevitUnitConversion.ToRevitPoint(segment.StartPoint);
        XYZ end = RevitUnitConversion.ToRevitPoint(segment.EndPoint);

        RevitPipe pipe = RevitPipe.Create(doc, systemTypeId, pipeTypeId, levelId, start, end)
            ?? throw new InvalidOperationException(
                $"Pipe.Create החזיר null עבור מסלול '{routeId}'.");

        double diameterFeet = UnitUtils.ConvertToInternalUnits(diameterMm, UnitTypeId.Millimeters);
        pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(diameterFeet);

        // שני המזהים - Comments הוא הבטוח (בלי אילוץ ייחודיות), Mark
        // לעקביות עם ה-DirectShape הקיים ועם CreateTestPipeCommand.
        pipe.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(routeId);
        pipe.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(routeId);

        return pipe;
    }
}
