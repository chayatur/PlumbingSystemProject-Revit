using Xunit;

namespace PlumbingSystem.Core.Tests;

/// <summary>
/// בדיקה בודדת שמטרתה היחידה כרגע היא להוכיח שה-test runner (xUnit דרך
/// <c>dotnet test</c>) עובד על שלד הפרויקט, עוד לפני שיש קוד אמיתי ב-Core
/// לבדוק. כשיתווספו מחלקות אמיתיות ל-PlumbingSystem.Core, בדיקה זו תישאר
/// כ-sanity check ותצטרף אליה בדיקות שמכסות את הלוגיקה בפועל.
/// </summary>
public class DummyTests
{
    /// <summary>
    /// בדיקת שפיות פשוטה שמוודאת שהחשבון הבסיסי ביותר עובד - אם היא נכשלת,
    /// הבעיה היא בסביבת ההרצה עצמה ולא בקוד של הפרויקט.
    /// </summary>
    [Fact]
    public void Sanity_OnePlusOneEqualsTwo()
    {
        Assert.Equal(2, 1 + 1);
    }
}
