namespace PalOps.Web.SaveGames.Binary;

public sealed record GvasProperty(
    string Name,
    string TypeName,
    object? Value,
    IReadOnlyDictionary<string, object?> Metadata,
    ReadOnlyMemory<byte> RawPayload)
{
    public int GetInt32() => Value switch
    {
        int value => value,
        uint value when value <= int.MaxValue => (int)value,
        _ => throw new InvalidCastException($"属性 {Name} 不是 Int32。" )
    };

    public long GetInt64() => Value switch
    {
        long value => value,
        int value => value,
        uint value => value,
        ulong value when value <= long.MaxValue => (long)value,
        _ => throw new InvalidCastException($"属性 {Name} 不是 Int64。" )
    };

    public string GetString() => Value as string
        ?? throw new InvalidCastException($"属性 {Name} 不是字符串。" );

    public bool GetBoolean() => Value is bool value
        ? value
        : throw new InvalidCastException($"属性 {Name} 不是布尔值。" );

    public IReadOnlyDictionary<string, GvasProperty> GetStruct()
        => Value as IReadOnlyDictionary<string, GvasProperty>
           ?? throw new InvalidCastException($"属性 {Name} 不是结构体。" );

    public IReadOnlyList<object?> GetArray()
        => Value as IReadOnlyList<object?>
           ?? throw new InvalidCastException($"属性 {Name} 不是数组。" );
}
