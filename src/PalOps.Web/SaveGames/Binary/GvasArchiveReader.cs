using System.Buffers.Binary;
using System.Text;

namespace PalOps.Web.SaveGames.Binary;

/// <summary>Bounded little-endian reader used by the GVAS parser.</summary>
public sealed class GvasArchiveReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _maximumStringBytes;

    public GvasArchiveReader(ReadOnlyMemory<byte> data, int maximumStringBytes = 16 * 1024 * 1024)
    {
        _data = data;
        _maximumStringBytes = maximumStringBytes;
    }

    public int Position { get; private set; }
    public int Length => _data.Length;
    public int Remaining => Length - Position;
    public bool End => Position >= Length;

    public byte ReadByte() => ReadSpan(1)[0];
    public bool ReadBooleanByte() => ReadByte() != 0;
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(2));
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(ReadSpan(2));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(4));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(4));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadSpan(8));
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(8));
    public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
    public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

    public Guid ReadGuid()
    {
        var bytes = ReadBytes(16);
        return new Guid(bytes);
    }

    public byte[] ReadBytes(int length) => ReadSpan(length).ToArray();

    public ReadOnlyMemory<byte> ReadMemory(int length)
    {
        EnsureAvailable(length);
        var result = _data.Slice(Position, length);
        Position += length;
        return result;
    }

    public GvasArchiveReader ReadSubReader(int length)
        => new(ReadMemory(length), _maximumStringBytes);

    public void Skip(int length)
    {
        EnsureAvailable(length);
        Position += length;
    }

    public string ReadFString()
    {
        var count = ReadInt32();
        if (count == 0) return string.Empty;
        if (count == int.MinValue) throw new InvalidDataException("FString 长度无效。" );

        if (count < 0)
        {
            // Convert through Int64 before negation/multiplication. Malformed data can
            // otherwise escape as OverflowException before the configured FString limit
            // has a chance to reject it.
            var characterCount = -(long)count;
            var byteCount = characterCount * 2L;
            EnsureStringLength(byteCount);
            var bytes = ReadSpan((int)byteCount);
            if (bytes.Length < 2 || bytes[^1] != 0 || bytes[^2] != 0)
                throw new InvalidDataException("UTF-16 FString 缺少结尾空字符。" );
            return Encoding.Unicode.GetString(bytes[..^2]);
        }

        EnsureStringLength(count);
        var utf8 = ReadSpan(count);
        if (utf8.Length < 1 || utf8[^1] != 0)
            throw new InvalidDataException("FString 缺少结尾空字符。" );
        return Encoding.UTF8.GetString(utf8[..^1]);
    }

    public bool TryReadOptionalGuid(out Guid guid)
    {
        var hasGuid = ReadByte();
        if (hasGuid is not 0 and not 1)
            throw new InvalidDataException($"属性 GUID 标志无效：{hasGuid}。" );
        guid = hasGuid == 1 ? ReadGuid() : Guid.Empty;
        return hasGuid == 1;
    }

    private ReadOnlySpan<byte> ReadSpan(int length)
    {
        EnsureAvailable(length);
        var span = _data.Span.Slice(Position, length);
        Position += length;
        return span;
    }

    private void EnsureAvailable(int length)
    {
        if (length < 0 || length > Remaining)
            throw new EndOfStreamException($"GVAS 数据被截断：需要 {length} 字节，剩余 {Remaining} 字节。" );
    }

    private void EnsureStringLength(long bytes)
    {
        if (bytes < 0 || bytes > _maximumStringBytes || bytes > int.MaxValue)
            throw new InvalidDataException($"FString 长度 {bytes} 超过限制。" );
    }
}
