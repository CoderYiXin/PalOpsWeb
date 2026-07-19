namespace PalOps.Web.Versioning;

/// <summary>
/// Stable numeric version core. Examples: v1.2.3 == 1.2.3, 1.2.4 > 1.2.3, 1.2 == 1.2.0.
/// </summary>
public readonly record struct SemanticVersionValue(
    int Major,
    int Minor,
    int Patch,
    int Revision) : IComparable<SemanticVersionValue>
{
    public static bool TryParse(string? value, out SemanticVersionValue version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0) normalized = normalized[..suffixIndex];
        var parts = normalized.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4 || parts.Any(string.IsNullOrWhiteSpace)) return false;
        Span<int> numbers = stackalloc int[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out numbers[index]) || numbers[index] < 0) return false;
        }
        version = new(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public int CompareTo(SemanticVersionValue other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0) return comparison;
        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0) return comparison;
        comparison = Patch.CompareTo(other.Patch);
        return comparison != 0 ? comparison : Revision.CompareTo(other.Revision);
    }

    public override string ToString() => Revision == 0
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}.{Revision}";
}
