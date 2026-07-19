namespace PalOps.Tooling.Map;

public readonly record struct MapPoint(double X, double Y)
{
    public double DistanceTo(MapPoint other)
    {
        var deltaX = X - other.X;
        var deltaY = Y - other.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}

public readonly record struct AffineTransform(double A, double B, double C, double D, double E, double F)
{
    public double Determinant => A * E - B * D;

    public bool IsFinite =>
        double.IsFinite(A) &&
        double.IsFinite(B) &&
        double.IsFinite(C) &&
        double.IsFinite(D) &&
        double.IsFinite(E) &&
        double.IsFinite(F);

    public MapPoint Apply(MapPoint source) =>
        new(
            A * source.X + B * source.Y + C,
            D * source.X + E * source.Y + F);

    public AffineTransform Inverse()
    {
        var determinant = Determinant;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
            throw new InvalidOperationException("仿射矩阵不可逆。");

        var inverseA = E / determinant;
        var inverseB = -B / determinant;
        var inverseD = -D / determinant;
        var inverseE = A / determinant;
        return new AffineTransform(
            inverseA,
            inverseB,
            -(inverseA * C + inverseB * F),
            inverseD,
            inverseE,
            -(inverseD * C + inverseE * F));
    }
}
