using System.Collections.ObjectModel;

namespace PalOps.Web.SaveGames.Binary;

public sealed record GvasParserOptions(
    long MaximumDocumentBytes,
    int MaximumDepth,
    int MaximumCollectionElements,
    int MaximumStringBytes = 16 * 1024 * 1024,
    int MaximumRawPropertyBytes = 64 * 1024 * 1024)
{
    public static GvasParserOptions Default { get; } = new(
        MaximumDocumentBytes: 8L * 1024 * 1024 * 1024,
        MaximumDepth: 128,
        MaximumCollectionElements: 10_000_000);
}

public interface IGvasParser
{
    GvasDocument Parse(ReadOnlyMemory<byte> data);
    GvasPropertyBagResult ParsePropertyBag(ReadOnlyMemory<byte> data);
}

/// <summary>
/// Generic Unreal GVAS reader with Palworld path-based struct hints. Unknown
/// property payloads are retained as bounded raw bytes so newer fields do not
/// invalidate the entire document.
/// </summary>
public sealed class GvasParser : IGvasParser
{
    private const uint GvasMagic = 0x53415647;
    private readonly GvasParserOptions _options;
    private readonly IReadOnlyDictionary<string, string> _typeHints;

    public GvasParser(GvasParserOptions? options = null)
    {
        _options = options ?? GvasParserOptions.Default;
        _typeHints = PalworldGvasTypeHints.Values;
    }

    public GvasDocument Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length > _options.MaximumDocumentBytes)
            throw new InvalidDataException("GVAS 文档超过大小限制。" );

        var reader = new GvasArchiveReader(data, _options.MaximumStringBytes);
        if (reader.ReadUInt32() != GvasMagic)
            throw new InvalidDataException("GVAS 魔数无效。" );

        var saveGameVersion = reader.ReadInt32();
        var packageFileVersionUe4 = reader.ReadInt32();
        var packageFileVersionUe5 = reader.ReadInt32();
        var engine = new GvasEngineVersion(
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadUInt32(),
            reader.ReadFString());

        var customVersionFormat = reader.ReadInt32();
        var customCount = ReadCollectionCount(reader, "自定义版本");
        var customVersions = new List<GvasCustomVersion>(Math.Min(customCount, 4096));
        for (var index = 0; index < customCount; index++)
            customVersions.Add(new GvasCustomVersion(reader.ReadGuid(), reader.ReadInt32()));

        var className = reader.ReadFString();
        var properties = ReadPropertyBag(reader, 0, string.Empty);
        var trailer = reader.ReadMemory(reader.Remaining);

        return new GvasDocument(
            saveGameVersion,
            packageFileVersionUe4,
            packageFileVersionUe5,
            engine,
            customVersionFormat,
            customVersions,
            className,
            new ReadOnlyDictionary<string, GvasProperty>(properties),
            trailer);
    }

    public GvasPropertyBagResult ParsePropertyBag(ReadOnlyMemory<byte> data)
    {
        if (data.Length > _options.MaximumRawPropertyBytes)
            throw new InvalidDataException("嵌套属性数据超过大小限制。");

        var reader = new GvasArchiveReader(data, _options.MaximumStringBytes);
        var properties = ReadPropertyBag(reader, 0, string.Empty);
        return new GvasPropertyBagResult(
            new ReadOnlyDictionary<string, GvasProperty>(properties),
            reader.Position,
            data.Slice(reader.Position));
    }

    private Dictionary<string, GvasProperty> ReadPropertyBag(
        GvasArchiveReader reader,
        int depth,
        string path)
    {
        EnsureDepth(depth);
        var properties = new Dictionary<string, GvasProperty>(StringComparer.Ordinal);
        while (!reader.End)
        {
            var name = reader.ReadFString();
            if (name.Equals("None", StringComparison.Ordinal)) break;

            var propertyPath = AppendPath(path, name);
            try
            {
                var property = ReadProperty(reader, name, depth + 1, propertyPath);
                properties[name] = property;
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException)
            {
                throw new InvalidDataException(
                    $"解析 GVAS 属性 {propertyPath} 失败：{ex.Message}",
                    ex);
            }
        }
        return properties;
    }

    private GvasProperty ReadProperty(
        GvasArchiveReader reader,
        string name,
        int depth,
        string path)
    {
        EnsureDepth(depth);
        var type = reader.ReadFString();
        var declaredSize = reader.ReadInt64();
        if (declaredSize < 0 || declaredSize > int.MaxValue)
            throw new InvalidDataException($"属性 {name} 的长度无效：{declaredSize}。" );
        if (declaredSize > _options.MaximumRawPropertyBytes)
            throw new InvalidDataException($"属性 {name} 超过单属性大小限制。" );

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        object? value;
        ReadOnlyMemory<byte> raw;

        switch (type)
        {
            case "BoolProperty":
                value = reader.ReadBooleanByte();
                reader.TryReadOptionalGuid(out var boolGuid);
                metadata["propertyGuid"] = boolGuid == Guid.Empty ? null : boolGuid;
                raw = ReadOnlyMemory<byte>.Empty;
                break;

            case "ByteProperty":
                metadata["enumType"] = reader.ReadFString();
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(reader, (int)declaredSize, payload =>
                    declaredSize == 1 ? payload.ReadByte() : payload.ReadFString());
                break;

            case "EnumProperty":
                metadata["enumType"] = reader.ReadFString();
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(reader, (int)declaredSize, payload => payload.ReadFString());
                break;

            case "StructProperty":
                var structType = reader.ReadFString();
                metadata["structType"] = structType;
                metadata["structGuid"] = reader.ReadGuid();
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(
                    reader,
                    (int)declaredSize,
                    payload => ReadStruct(payload, structType, depth, path));
                break;

            case "ArrayProperty":
                var arrayType = reader.ReadFString();
                metadata["arrayType"] = arrayType;
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(
                    reader,
                    (int)declaredSize,
                    payload => ReadArray(payload, arrayType, depth, path));
                break;

            case "MapProperty":
                var keyType = reader.ReadFString();
                var valueType = reader.ReadFString();
                var keyPath = AppendPath(path, "Key");
                var valuePath = AppendPath(path, "Value");
                var keyStructType = ResolveCollectionStructType(keyType, keyPath, "Guid");
                var valueStructType = ResolveCollectionStructType(valueType, valuePath, "StructProperty");
                metadata["keyType"] = keyType;
                metadata["valueType"] = valueType;
                metadata["keyStructType"] = keyStructType;
                metadata["valueStructType"] = valueStructType;
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(
                    reader,
                    (int)declaredSize,
                    payload => ReadMap(
                        payload,
                        keyType,
                        valueType,
                        keyStructType,
                        valueStructType,
                        depth,
                        keyPath,
                        valuePath));
                break;

            case "SetProperty":
                var setType = reader.ReadFString();
                var setPath = AppendPath(path, "StructProperty");
                var setStructType = ResolveCollectionStructType(setType, setPath, "StructProperty");
                metadata["setType"] = setType;
                metadata["structType"] = setStructType;
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(
                    reader,
                    (int)declaredSize,
                    payload => ReadSet(payload, setType, setStructType, depth, setPath));
                break;

            default:
                ReadOptionalGuid(reader, metadata);
                (value, raw) = ReadBoundedPayload(
                    reader,
                    (int)declaredSize,
                    payload => ReadScalar(payload, type));
                break;
        }

        return new GvasProperty(name, type, value, metadata, raw);
    }

    private object? ReadScalar(GvasArchiveReader reader, string type)
        => type switch
        {
            "Int8Property" => unchecked((sbyte)reader.ReadByte()),
            "Int16Property" => reader.ReadInt16(),
            "IntProperty" or "Int32Property" => reader.ReadInt32(),
            "UInt16Property" => reader.ReadUInt16(),
            "UInt32Property" => reader.ReadUInt32(),
            "Int64Property" or "FixedPoint64Property" => reader.ReadInt64(),
            "UInt64Property" => reader.ReadUInt64(),
            "FloatProperty" => reader.ReadSingle(),
            "DoubleProperty" => reader.ReadDouble(),
            "StrProperty" or "NameProperty" or "ObjectProperty" or "SoftObjectProperty" or "TextProperty" => reader.ReadFString(),
            _ => reader.ReadMemory(reader.Remaining).ToArray()
        };

    private object? ReadStruct(
        GvasArchiveReader reader,
        string structType,
        int depth,
        string path)
    {
        EnsureDepth(depth);
        return structType switch
        {
            "Vector" => new GvasVector(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()),
            "Vector2D" => new GvasVector2(reader.ReadDouble(), reader.ReadDouble()),
            "Quat" => new GvasQuaternion(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()),
            "LinearColor" => new GvasLinearColor(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            "Color" => new GvasColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()),
            "Guid" => reader.ReadGuid(),
            "DateTime" => reader.ReadUInt64(),
            _ => new ReadOnlyDictionary<string, GvasProperty>(ReadPropertyBag(reader, depth + 1, path))
        };
    }

    private object ReadArray(
        GvasArchiveReader reader,
        string elementType,
        int depth,
        string path)
    {
        EnsureDepth(depth);
        var count = ReadCollectionCount(reader, "数组");

        // RawData and several large Palworld blobs are ByteProperty arrays. Keeping
        // each byte as a boxed object multiplies memory use and causes the projection
        // walker to visit millions of meaningless scalar nodes. Preserve them as a
        // contiguous memory; PalworldRawDataDecoder already consumes ReadOnlyMemory<byte>.
        if (elementType.Equals("ByteProperty", StringComparison.Ordinal))
            return reader.ReadMemory(count);

        var values = new List<object?>(Math.Min(count, 4096));
        if (elementType.Equals("StructProperty", StringComparison.Ordinal))
        {
            if (count == 0) return values;
            var propertyName = reader.ReadFString();
            var propertyType = reader.ReadFString();
            _ = reader.ReadInt64();
            var structType = reader.ReadFString();
            var structGuid = reader.ReadGuid();
            if (reader.Remaining > 0 && reader.ReadByte() != 0)
                throw new InvalidDataException("结构体数组 GUID 标志无效。" );

            var elementPath = AppendPath(path, propertyName);
            for (var index = 0; index < count; index++)
                values.Add(ReadStruct(reader, structType, depth, elementPath));

            _ = propertyType;
            _ = structGuid;
            return values;
        }

        for (var index = 0; index < count; index++)
            values.Add(ReadCollectionValue(reader, elementType, null, depth, path));
        return values;
    }

    private IReadOnlyList<GvasMapEntry> ReadMap(
        GvasArchiveReader reader,
        string keyType,
        string valueType,
        string? keyStructType,
        string? valueStructType,
        int depth,
        string keyPath,
        string valuePath)
    {
        EnsureDepth(depth);
        _ = reader.ReadUInt32();
        var count = ReadCollectionCount(reader, "映射");
        var values = new List<GvasMapEntry>(Math.Min(count, 4096));
        for (var index = 0; index < count; index++)
        {
            var key = ReadCollectionValue(reader, keyType, keyStructType, depth, keyPath);
            var value = ReadCollectionValue(reader, valueType, valueStructType, depth, valuePath);
            values.Add(new GvasMapEntry(key, value));
        }
        return values;
    }

    private IReadOnlyList<object?> ReadSet(
        GvasArchiveReader reader,
        string elementType,
        string? structType,
        int depth,
        string path)
    {
        EnsureDepth(depth);
        _ = reader.ReadUInt32();
        var count = ReadCollectionCount(reader, "集合");
        var values = new List<object?>(Math.Min(count, 4096));
        for (var index = 0; index < count; index++)
            values.Add(ReadCollectionValue(reader, elementType, structType, depth, path));
        return values;
    }

    private object? ReadCollectionValue(
        GvasArchiveReader reader,
        string type,
        string? structType,
        int depth,
        string path)
    {
        EnsureDepth(depth);
        return type switch
        {
            "Int8Property" => unchecked((sbyte)reader.ReadByte()),
            "Int16Property" => reader.ReadInt16(),
            "IntProperty" or "Int32Property" => reader.ReadInt32(),
            "UInt16Property" => reader.ReadUInt16(),
            "UInt32Property" => reader.ReadUInt32(),
            "Int64Property" => reader.ReadInt64(),
            "UInt64Property" => reader.ReadUInt64(),
            "FloatProperty" => reader.ReadSingle(),
            "DoubleProperty" => reader.ReadDouble(),
            "BoolProperty" => reader.ReadBooleanByte(),
            "NameProperty" or "StrProperty" or "EnumProperty" or "ObjectProperty" or "SoftObjectProperty" => reader.ReadFString(),
            "Guid" or "GuidProperty" => reader.ReadGuid(),
            "ByteProperty" => reader.ReadByte(),
            "StructProperty" => ReadStruct(reader, structType ?? "StructProperty", depth, path),
            _ => throw new NotSupportedException($"暂不支持集合元素类型 {type}。" )
        };
    }

    private string? ResolveCollectionStructType(
        string propertyType,
        string path,
        string fallback)
    {
        if (!propertyType.Equals("StructProperty", StringComparison.Ordinal)) return null;
        return _typeHints.TryGetValue(path, out var structType) ? structType : fallback;
    }

    private (object? Value, ReadOnlyMemory<byte> Raw) ReadBoundedPayload(
        GvasArchiveReader parent,
        int length,
        Func<GvasArchiveReader, object?> parser)
    {
        var memory = parent.ReadMemory(length);
        var payload = new GvasArchiveReader(memory, _options.MaximumStringBytes);
        try
        {
            var value = parser(payload);
            return (value, memory);
        }
        catch (NotSupportedException)
        {
            return (memory.ToArray(), memory);
        }
    }

    private static void ReadOptionalGuid(
        GvasArchiveReader reader,
        IDictionary<string, object?> metadata)
    {
        reader.TryReadOptionalGuid(out var guid);
        metadata["propertyGuid"] = guid == Guid.Empty ? null : guid;
    }

    private int ReadCollectionCount(GvasArchiveReader reader, string label)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > _options.MaximumCollectionElements)
            throw new InvalidDataException($"{label}元素数量 {count} 超过限制。" );
        return count;
    }

    private void EnsureDepth(int depth)
    {
        if (depth > _options.MaximumDepth)
            throw new InvalidDataException("GVAS 属性嵌套超过限制。" );
    }

    private static string AppendPath(string path, string segment)
        => string.IsNullOrEmpty(path) ? "." + segment : path + "." + segment;
}

public sealed record GvasVector(double X, double Y, double Z);
public sealed record GvasVector2(double X, double Y);
public sealed record GvasQuaternion(double X, double Y, double Z, double W);
public sealed record GvasLinearColor(float R, float G, float B, float A);
public sealed record GvasColor(byte B, byte G, byte R, byte A);
public sealed record GvasMapEntry(object? Key, object? Value);
