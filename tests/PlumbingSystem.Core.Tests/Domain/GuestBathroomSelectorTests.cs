using PlumbingSystem.Core.Domain;
using Xunit;

namespace PlumbingSystem.Core.Tests.Domain;

/// <summary>בדיקות עבור <see cref="GuestBathroomSelector.Select"/>.</summary>
public class GuestBathroomSelectorTests
{
    /// <summary>דירה עם אסלה אחת - אוטומטית שירותים בודדים, בלי צורך במרחקים.</summary>
    [Fact]
    public void Select_SingleToilet_IsAutomaticallyGuestBathroom()
    {
        var toilets = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>
        {
            ("toilet-1", Array.Empty<double>()),
        };

        var result = GuestBathroomSelector.Select(toilets);

        Assert.Single(result);
        Assert.Equal("toilet-1", result[0].FixtureId);
        Assert.True(result[0].IsGuestBathroom);
        Assert.Null(result[0].MinDistanceToWetFixture);
    }

    /// <summary>מבין כמה אסלות, זו עם המרחק-המינימלי-הגדול-ביותר מנצחת.</summary>
    [Fact]
    public void Select_MultipleToilets_FarthestFromNearestWetFixtureWins()
    {
        var toilets = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>
        {
            ("near", new List<double> { 1 }),
            ("far", new List<double> { 9 }),
        };

        var result = GuestBathroomSelector.Select(toilets);

        Assert.True(result.Single(r => r.FixtureId == "far").IsGuestBathroom);
        Assert.False(result.Single(r => r.FixtureId == "near").IsGuestBathroom);
    }

    /// <summary>לכל אסלה נלקח המרחק המינימלי מתוך רשימת המרחקים שלה - לא המקסימלי.</summary>
    [Fact]
    public void Select_UsesMinimumDistancePerToilet_NotMaximum()
    {
        var toilets = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>
        {
            ("a", new List<double> { 1, 18 }),   // min = 1
            ("b", new List<double> { 19, 2 }),   // min = 2
        };

        var result = GuestBathroomSelector.Select(toilets);

        Assert.Equal(1, result.Single(r => r.FixtureId == "a").MinDistanceToWetFixture!.Value, precision: 6);
        Assert.Equal(2, result.Single(r => r.FixtureId == "b").MinDistanceToWetFixture!.Value, precision: 6);
        Assert.True(result.Single(r => r.FixtureId == "b").IsGuestBathroom);
    }

    /// <summary>
    /// רגרסיה לבאג בדירה '1133': קיר חסם את קו הראייה בין האסלה הנכונה
    /// לאלמנט הרטוב הקרוב אליה, מה שהפך את המרחק האווירי לקטן באופן
    /// מטעה. הקורא (RevitModelReader) מייצג זוג חסום כ-double.MaxValue
    /// במקום המרחק האווירי הגולמי - הבדיקה הזו מוודאת שכש-Core מקבל
    /// ערך כזה, הוא מתייחס אליו כ"רחוק ביותר" ולא כ"קרוב", כך שהאסלה
    /// הנכונה (עם הזוג החסום) עדיין מנצחת נכון.
    /// </summary>
    [Fact]
    public void Select_BlockedPairRepresentedAsMaxValue_DoesNotFalselyWinAsNearby()
    {
        var toilets = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>
        {
            // הזוג הקרוב ביותר (אווירית) חסום קיר -> Double.MaxValue; הזוג
            // האמיתי-הפנוי היחיד הוא 6.0, ולכן ה-min האמיתי הוא 6.0.
            ("correct-guest-bathroom", new List<double> { double.MaxValue, 6.0 }),
            ("full-bathroom", new List<double> { 3.0 }),
        };

        var result = GuestBathroomSelector.Select(toilets);

        Assert.True(result.Single(r => r.FixtureId == "correct-guest-bathroom").IsGuestBathroom);
        Assert.False(result.Single(r => r.FixtureId == "full-bathroom").IsGuestBathroom);
    }

    /// <summary>יותר מאסלה אחת, אבל 0 אלמנטים רטובים - מקרה שלא אמור לקרות, זורק שגיאה ברורה.</summary>
    [Fact]
    public void Select_MultipleToiletsNoWetFixtures_Throws()
    {
        var toilets = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>
        {
            ("a", Array.Empty<double>()),
            ("b", Array.Empty<double>()),
        };

        Assert.Throws<InvalidOperationException>(() => GuestBathroomSelector.Select(toilets));
    }

    /// <summary>שתי אסלות עם מרחק מקסימלי זהה בדיוק - תיקו, זורק שגיאה ברורה במקום לנחש.</summary>
    [Fact]
    public void Select_TiedMaximumDistance_Throws()
    {
        var toilets = new List<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>
        {
            ("a", new List<double> { 5 }),
            ("b", new List<double> { 5 }),
        };

        Assert.Throws<InvalidOperationException>(() => GuestBathroomSelector.Select(toilets));
    }

    /// <summary>רשימת אסלות ריקה - שגיאת קריאה (לא מצב הנדסי אפשרי).</summary>
    [Fact]
    public void Select_NoToilets_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => GuestBathroomSelector.Select(
                Array.Empty<(string Id, IReadOnlyList<double> DistancesToWetFixtures)>()));
    }
}
