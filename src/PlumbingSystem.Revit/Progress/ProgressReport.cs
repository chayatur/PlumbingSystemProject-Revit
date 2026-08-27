namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// תמונת-מצב אחת לעדכון-התקדמות-חי. כל שדה <c>nullable</c> - קורא
/// ממלא רק מה שהשתנה; שדה <c>null</c> אומר "השאר כמו שהיה" בתצוגה
/// (לא "אפס"/ריק). זה מה שמאפשר לפקודות שונות (Draw Pipes, ובעתיד
/// Place Collectors/Electrical) לשלוח רק את מה שרלוונטי-להן, בלי
/// לדעת אחת על השנייה. ראו docs/progress-infrastructure.md.
/// </summary>
/// <remarks>
/// **בכוונה אין כאן שום משמעות הנדסית**: <see cref="SuccessCount"/> ו-
/// <see cref="ManualReviewCount"/> הם שני מספרים-לתצוגה בלבד - התשתית
/// לא "יודעת" מה זה אומר "הצלחה" בכלל. הקורא (למשל DrawPipesCommand)
/// אחראי לחשב ולהעביר את הספירה-הרצה שלו-עצמו. זה מה שמאפשר לפקודה
/// עתידית עם מושג-הצלחה שונה לגמרי (חשמל, למשל) להשתמש באותה תשתית.
/// </remarks>
public sealed record ProgressReport(
    string? OperationName = null,
    string? Floor = null,
    string? Apartment = null,
    string? CurrentItem = null,
    int? ProgressCurrent = null,
    int? ProgressTotal = null,
    int? SuccessCount = null,
    int? ManualReviewCount = null,
    string? StatusMessage = null);
