namespace PlumbingSystem.Core.Domain;

/// <summary>
/// מזהה איזו אסלה בדירה היא "שירותים בודדים", לפי הכלל שאושר: האסלה
/// הכי רחוקה (מרחק-מינימלי-מקסימלי) מהאלמנט הרטוב (אמבטיה/מקלחון)
/// הקרוב אליה. זו בחירת **מקסימום יחסי** בתוך הדירה (לא סף מרחק קבוע),
/// כי "רחוק" הוא יחסי לגודל/פריסת הדירה הספציפית.
/// </summary>
/// <remarks>
/// המחלקה הזו מקבלת **מרחקים מוכנים מראש** לכל זוג (אסלה, אלמנט רטוב) -
/// לא נקודות גיאומטריות - ולא מחשבת שום דבר גיאומטרי בעצמה. זו החלטה
/// מכוונת: מרחק אווירי (קו ישר) לא מתחשב בקירות, וגילינו בפועל (דירה
/// '1133') שקיר בין אסלה לאלמנט רטוב יכול לגרום למרחק האווירי להיראות
/// קטן משמעותית מהמרחק האמיתי, ולהוביל לבחירה שגויה. בדיקת חסימת קיר
/// (ReferenceIntersector) חייבת RevitAPI, ולכן היא מתבצעת ב-
/// PlumbingSystem.Revit (RevitModelReader) *לפני* שהמרחקים מגיעים
/// לכאן - זוג חסום מועבר עם מרחק double.MaxValue במקום המרחק האווירי
/// הגולמי, כך שהוא לעולם לא "ינצח" כמרחק-קרוב-ביותר-שגוי. הקוד כאן
/// נשאר לוגיקה טהורה (מוצא מינימום פר-אסלה, מקסימום מביניהם, מזהה
/// תיקו/העדר-מועמדים) בלי שום תלות ב-Revit, וניתן לבדיקה מלאה.
/// </remarks>
public static class GuestBathroomSelector
{
    /// <summary>
    /// סבילות (tolerance) להשוואת מרחקים כשוללים ודאי-שווים, כדי שלא
    /// שגיאות עיגול floating-point ייצרו "כמעט-תיקו" מזוייף. הערך קטן
    /// מספיק שלא ישפיע על החלטה אמיתית, אבל מונע רגישות-יתר לעיגול.
    /// </summary>
    private const double DistanceTieTolerance = 1e-6;

    /// <summary>
    /// תוצאת הבחירה עבור אסלה בודדת: המרחק המינימלי שלה לאלמנט רטוב
    /// (null אם היה רק אסלה אחת בדירה ולא היה צורך למדוד), והאם היא
    /// זו שנבחרה כשירותים בודדים.
    /// </summary>
    public readonly record struct Result(string FixtureId, double? MinDistanceToWetFixture, bool IsGuestBathroom);

    /// <summary>
    /// בוחרת את השירותים-הבודדים מתוך כל האסלות בדירה אחת.
    /// </summary>
    /// <param name="toiletsWithDistances">
    /// לכל אסלה בדירה: מזהה, ורשימת המרחקים שלה לכל אלמנט רטוב בדירה
    /// (כבר מחושבים סופית על ידי הקורא - כולל כל התאמה פיזית כמו חסימת
    /// קיר; ראו התיעוד על המחלקה). חייב להכיל לפחות אסלה אחת.
    /// </param>
    /// <returns>תוצאה אחת לכל אסלה שסופקה, באותו סדר קלט.</returns>
    /// <exception cref="ArgumentException">
    /// אם <paramref name="toiletsWithDistances"/> ריק - אין דירה בלי אף
    /// אסלה, לפי מה שאושר; קלט כזה הוא שגיאת קריאה, לא מצב הנדסי.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// אם יש יותר מאסלה אחת בדירה אבל לפחות אחת מהן קיבלה רשימת מרחקים
    /// ריקה (כלומר: אין בדירה אלמנטים רטובים בכלל), או אם שתי אסלות
    /// (או יותר) נקשרות בדיוק על אותו מרחק מקסימלי - שני המקרים האלה
    /// לא אמורים לקרות לפי מה שאושר על המודל, ועדיף לזרוק שגיאה ברורה
    /// מאשר לנחש בשקט איזו מהן "נכונה".
    /// </exception>
    public static IReadOnlyList<Result> Select(
        IReadOnlyList<(string Id, IReadOnlyList<double> DistancesToWetFixtures)> toiletsWithDistances)
    {
        if (toiletsWithDistances.Count == 0)
        {
            throw new ArgumentException(
                "רשימת האסלות בדירה ריקה - אין דירה בלי אף אסלה.",
                nameof(toiletsWithDistances));
        }

        if (toiletsWithDistances.Count == 1)
        {
            return new[] { new Result(toiletsWithDistances[0].Id, null, IsGuestBathroom: true) };
        }

        for (int i = 0; i < toiletsWithDistances.Count; i++)
        {
            if (toiletsWithDistances[i].DistancesToWetFixtures.Count == 0)
            {
                throw new InvalidOperationException(
                    $"בדירה יש {toiletsWithDistances.Count} אסלות, אבל האסלה " +
                    $"'{toiletsWithDistances[i].Id}' לא קיבלה אף מרחק לאלמנט רטוב - " +
                    "כנראה שאין אלמנטים רטובים (אמבטיה/מקלחון) בדירה בכלל. לא ניתן " +
                    "לזהות שירותים בודדים.");
            }
        }

        var withDistance = toiletsWithDistances
            .Select(toilet => new Result(
                toilet.Id,
                toilet.DistancesToWetFixtures.Min(),
                IsGuestBathroom: false))
            .ToList();

        double maxOfMinDistances = withDistance.Max(r => r.MinDistanceToWetFixture!.Value);

        List<Result> winners = withDistance
            .Where(r => Math.Abs(r.MinDistanceToWetFixture!.Value - maxOfMinDistances) <= DistanceTieTolerance)
            .ToList();

        if (winners.Count > 1)
        {
            throw new InvalidOperationException(
                $"{winners.Count} אסלות קשורות (tied) בדיוק על אותו מרחק מקסימלי " +
                $"({maxOfMinDistances:F4}) מהאלמנט הרטוב הקרוב אליהן - לא ניתן לקבוע " +
                "שירותים בודדים בין שוות.");
        }

        string winnerId = winners[0].FixtureId;
        return withDistance
            .Select(r => r with { IsGuestBathroom = r.FixtureId == winnerId })
            .ToList();
    }
}
