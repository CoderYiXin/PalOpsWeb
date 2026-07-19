using System.Globalization;
using PalOps.Web.SaveGames.Binary;

namespace PalOps.Web.SaveGames.Projection;

internal sealed class GvasValueNavigator(int maximumNodes = 2_000_000)
{
    private readonly int _maximumNodes = maximumNodes;

    public IEnumerable<GvasLocatedValue> Walk(IReadOnlyDictionary<string, GvasProperty> properties)
    {
        var state = new TraversalState();
        foreach (var property in properties.Values)
        {
            foreach (var value in WalkValue(property.Value, property.Name, state))
                yield return value;
        }
    }

    public object? FindFirst(IReadOnlyDictionary<string, GvasProperty> properties, params string[] names)
    {
        var candidates = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var located in Walk(properties))
        {
            if (candidates.Contains(located.Name)) return located.Value;
        }
        return null;
    }

    public IReadOnlyList<GvasLocatedStruct> FindStructs(IReadOnlyDictionary<string, GvasProperty> properties)
    {
        var results = new List<GvasLocatedStruct>();
        var state = new TraversalState();
        foreach (var property in properties.Values)
            CollectStructs(property.Value, property.Name, results, state);
        return results;
    }

    private IEnumerable<GvasLocatedValue> WalkValue(object? value, string path, TraversalState state)
    {
        if (++state.Visited > _maximumNodes) throw new InvalidDataException("GVAS 节点数量超过投影限制。" );
        var name = LastSegment(path);
        yield return new GvasLocatedValue(path, name, value);

        switch (value)
        {
            case IReadOnlyDictionary<string, GvasProperty> properties:
                foreach (var property in properties.Values)
                {
                    foreach (var nested in WalkValue(property.Value, path + "." + property.Name, state))
                        yield return nested;
                }
                break;
            // IReadOnlyList<T> is covariant. A IReadOnlyList<GvasMapEntry> also matches
            // IReadOnlyList<object?>, so the more specific map-entry list must be handled first.
            case IReadOnlyList<GvasMapEntry> map:
                for (var index = 0; index < map.Count; index++)
                {
                    foreach (var nested in WalkValue(map[index].Key, $"{path}[{index}].Key", state))
                        yield return nested;
                    foreach (var nested in WalkValue(map[index].Value, $"{path}[{index}].Value", state))
                        yield return nested;
                }
                break;
            case IReadOnlyList<object?> list:
                for (var index = 0; index < list.Count; index++)
                {
                    foreach (var nested in WalkValue(list[index], $"{path}[{index}]", state))
                        yield return nested;
                }
                break;
            case GvasMapEntry entry:
                foreach (var nested in WalkValue(entry.Key, path + ".Key", state)) yield return nested;
                foreach (var nested in WalkValue(entry.Value, path + ".Value", state)) yield return nested;
                break;
        }
    }

    private void CollectStructs(
        object? value,
        string path,
        ICollection<GvasLocatedStruct> output,
        TraversalState state)
    {
        if (++state.Visited > _maximumNodes) throw new InvalidDataException("GVAS 节点数量超过投影限制。" );
        switch (value)
        {
            case IReadOnlyDictionary<string, GvasProperty> properties:
                output.Add(new GvasLocatedStruct(path, properties));
                foreach (var property in properties.Values)
                    CollectStructs(property.Value, path + "." + property.Name, output, state);
                break;
            // Keep the specific covariant list case before the general object list case.
            case IReadOnlyList<GvasMapEntry> map:
                for (var index = 0; index < map.Count; index++)
                {
                    CollectStructs(map[index].Key, $"{path}[{index}].Key", output, state);
                    CollectStructs(map[index].Value, $"{path}[{index}].Value", output, state);
                }
                break;
            case IReadOnlyList<object?> list:
                for (var index = 0; index < list.Count; index++)
                    CollectStructs(list[index], $"{path}[{index}]", output, state);
                break;
        }
    }

    public static string? ToText(object? value)
        => value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            Guid guid => guid.ToString("N").ToUpperInvariant(),
            byte[] bytes when bytes.Length == 16 => new Guid(bytes).ToString("N").ToUpperInvariant(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

    public static int ToInt32(object? value, int fallback = 0)
    {
        try
        {
            return value switch
            {
                null => fallback,
                int number => number,
                uint number when number <= int.MaxValue => (int)number,
                long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
                ulong number when number <= int.MaxValue => (int)number,
                short number => number,
                ushort number => number,
                byte number => number,
                sbyte number => number,
                float number => checked((int)number),
                double number => checked((int)number),
                string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => fallback
            };
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    public static long ToInt64(object? value, long fallback = 0)
    {
        try
        {
            return value switch
            {
                null => fallback,
                long number => number,
                ulong number when number <= long.MaxValue => (long)number,
                int number => number,
                uint number => number,
                short number => number,
                ushort number => number,
                byte number => number,
                sbyte number => number,
                float number => checked((long)number),
                double number => checked((long)number),
                string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => fallback
            };
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    public static double? ToDouble(object? value)
        => value switch
        {
            double number => number,
            float number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };

    public static bool ToBoolean(object? value)
        => value switch
        {
            bool boolean => boolean,
            byte number => number != 0,
            int number => number != 0,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };

    private static string LastSegment(string path)
    {
        var segment = path[(path.LastIndexOf('.') + 1)..];
        var bracket = segment.IndexOf('[');
        return bracket >= 0 ? segment[..bracket] : segment;
    }

    private sealed class TraversalState
    {
        public int Visited;
    }
}

internal sealed record GvasLocatedValue(string Path, string Name, object? Value);
internal sealed record GvasLocatedStruct(string Path, IReadOnlyDictionary<string, GvasProperty> Properties);
