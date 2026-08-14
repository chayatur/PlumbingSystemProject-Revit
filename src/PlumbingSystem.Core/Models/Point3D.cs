namespace PlumbingSystem.Core.Models;

/// <summary>
/// נקודה במרחב תלת-ממדי (יחידות מטרים, בהתאם למערכת הקואורדינטות של
/// מודל ה-Revit). זהו <c>struct</c> ולא <c>class</c> כי מדובר בערך
/// "טהור" בלי זהות משלו - שתי נקודות עם אותם X,Y,Z הן, לכל דבר, אותה
/// נקודה. זה גם חוסך הקצאות heap מיותרות, כי נקודות כאלה עוברות בין
/// אובייקטים (Fixture, Pipe וכו') המון פעמים בחישובי מיקום.
/// </summary>
public readonly struct Point3D : IEquatable<Point3D>
{
    /// <summary>קואורדינטת X.</summary>
    public double X { get; }

    /// <summary>קואורדינטת Y.</summary>
    public double Y { get; }

    /// <summary>קואורדינטת Z (גובה).</summary>
    public double Z { get; }

    /// <summary>יוצר נקודה מהקואורדינטות הנתונות.</summary>
    public Point3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <inheritdoc/>
    public bool Equals(Point3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Point3D other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>משווה שתי נקודות לפי ערך (X, Y, Z), לא לפי זהות.</summary>
    public static bool operator ==(Point3D left, Point3D right) => left.Equals(right);

    /// <summary>ההפך מ-<see cref="operator =="/>.</summary>
    public static bool operator !=(Point3D left, Point3D right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => $"({X}, {Y}, {Z})";
}
