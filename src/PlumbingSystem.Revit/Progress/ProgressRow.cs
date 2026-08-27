namespace PlumbingSystem.Revit.Progress;

/// <summary>
/// שורה בודדת ברשימה-הגוללת שבחלון-ההתקדמות - "מה קרה לפריט הזה".
/// נוצרת פעם אחת לכל <see cref="ProgressReport"/> שמכיל
/// <see cref="ProgressReport.StatusMessage"/> - ראו <see cref="ProgressViewModel.Apply"/>.
/// </summary>
public sealed record ProgressRow(string Apartment, string Item, string StatusMessage);
