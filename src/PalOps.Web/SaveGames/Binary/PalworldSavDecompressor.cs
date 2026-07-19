using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PalOps.Web.SaveGames.Binary;

public sealed record PalworldSavLimits(
    long MaximumInputBytes,
    long MaximumOutputBytes,
    int MaximumCompressionRatio)
{
    public static PalworldSavLimits Default { get; } = new(
        MaximumInputBytes: 8L * 1024 * 1024 * 1024,
        MaximumOutputBytes: 16L * 1024 * 1024 * 1024,
        MaximumCompressionRatio: 512);
}

public sealed record PalworldSavPayload(
    byte[] Data,
    byte SaveType,
    bool CnkWrapped,
    string Format);

public sealed record PalworldSavHeaderInfo(
    string Format,
    byte? SaveType,
    bool CnkWrapped,
    long InputLength,
    long? DeclaredCompressedLength,
    long? DeclaredUncompressedLength,
    string HeaderHex,
    string HeaderAscii,
    bool Supported,
    string Message);

public interface IPalworldSavDecompressor
{
    Task<PalworldSavPayload> DecompressAsync(Stream input, CancellationToken cancellationToken = default);
    Task<PalworldSavHeaderInfo> InspectAsync(Stream input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads Palworld save envelopes without launching an external parser process.
/// Raw GVAS and PlZ use managed code; Palworld 1.0 PlM1 payloads use the
/// bundled GPL <see cref="IPalworldOozDecoder"/> boundary.
/// </summary>
public sealed class PalworldSavDecompressor : IPalworldSavDecompressor
{
    private static ReadOnlySpan<byte> GvasMagic => "GVAS"u8;
    private static ReadOnlySpan<byte> PlzMagic => "PlZ"u8;
    private static ReadOnlySpan<byte> PlmMagic => "PlM"u8;
    private static ReadOnlySpan<byte> CnkMagic => "CNK"u8;

    private readonly IPalworldOozDecoder _oozDecoder;
    private readonly PalworldSavLimits _limits;

    public PalworldSavDecompressor(IPalworldOozDecoder oozDecoder)
    {
        _oozDecoder = oozDecoder ?? throw new ArgumentNullException(nameof(oozDecoder));
        _limits = PalworldSavLimits.Default;
    }

    internal PalworldSavDecompressor(
        IPalworldOozDecoder oozDecoder,
        PalworldSavLimits limits)
    {
        _oozDecoder = oozDecoder ?? throw new ArgumentNullException(nameof(oozDecoder));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public async Task<PalworldSavHeaderInfo> InspectAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("存档输入流不可读。", nameof(input));

        var originalPosition = input.CanSeek ? input.Position : 0;
        try
        {
            var length = input.CanSeek ? input.Length : -1;
            var buffer = new byte[64];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await input.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
                if (read == 0) break;
                total += read;
            }

            var header = buffer.AsSpan(0, total).ToArray();
            return InspectHeader(header, length >= 0 ? length : total);
        }
        finally
        {
            if (input.CanSeek) input.Position = originalPosition;
        }
    }

    public async Task<PalworldSavPayload> DecompressAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("存档输入流不可读。", nameof(input));

        var raw = await ReadAllBoundedAsync(input, _limits.MaximumInputBytes, cancellationToken);
        if (raw.Length < 4) throw new InvalidDataException("Palworld 存档头被截断。");

        if (HasMagic(raw, 0, GvasMagic))
        {
            if (raw.LongLength > _limits.MaximumOutputBytes)
                throw new InvalidDataException("未压缩 GVAS 存档超过大小限制。");
            return new PalworldSavPayload(raw, 0x30, false, "GVAS");
        }

        var header = InspectHeader(raw.AsSpan(0, Math.Min(raw.Length, 64)).ToArray(), raw.LongLength);
        if (!header.Supported) throw UnsupportedHeader(raw, header);

        var envelopeOffset = header.CnkWrapped ? 12 : 0;
        if (raw.Length < envelopeOffset + 12)
            throw new InvalidDataException(header.CnkWrapped ? "CNK 内层存档头被截断。" : "Palworld 存档头被截断。");

        if (HasMagic(raw, envelopeOffset + 8, PlmMagic))
            return await DecompressPlmAsync(raw, header, envelopeOffset, cancellationToken);

        if (HasMagic(raw, envelopeOffset + 8, PlzMagic))
            return await DecompressPlzAsync(raw, header, envelopeOffset, cancellationToken);

        throw UnsupportedHeader(raw, header);
    }

    private async Task<PalworldSavPayload> DecompressPlmAsync(
        byte[] raw,
        PalworldSavHeaderInfo header,
        int envelopeOffset,
        CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(raw, envelopeOffset);
        if (envelope.SaveType != 0x31)
            throw new NotSupportedException(
                $"不支持的 Palworld PlM 类型 0x{envelope.SaveType:X2}。{FormatHeader(raw)}");
        if (envelope.CompressedLength != envelope.StoredLength)
            throw new InvalidDataException(
                $"PlM1 压缩长度不匹配：头部 {envelope.CompressedLength:N0}，实际 {envelope.StoredLength:N0}。");

        EnsureCompressionRatio(envelope.UncompressedLength, envelope.StoredLength);
        var data = await _oozDecoder.DecompressAsync(
            raw.AsMemory(envelope.PayloadOffset, envelope.StoredLength),
            checked((int)envelope.UncompressedLength),
            cancellationToken);
        ValidateDecompressedGvas(data, envelope.UncompressedLength);

        return new PalworldSavPayload(
            data,
            envelope.SaveType,
            header.CnkWrapped,
            header.CnkWrapped ? "CNK+PlM1" : "PlM1");
    }

    private async Task<PalworldSavPayload> DecompressPlzAsync(
        byte[] raw,
        PalworldSavHeaderInfo header,
        int envelopeOffset,
        CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(raw, envelopeOffset);
        byte[] data;
        switch (envelope.SaveType)
        {
            case 0x30:
                if (envelope.UncompressedLength != envelope.StoredLength
                    || envelope.CompressedLength != envelope.StoredLength)
                {
                    throw new InvalidDataException(
                        $"PlZ 未压缩长度不匹配：声明 {envelope.UncompressedLength:N0}/{envelope.CompressedLength:N0}，" +
                        $"实际 {envelope.StoredLength:N0}。");
                }

                data = raw.AsSpan(envelope.PayloadOffset, envelope.StoredLength).ToArray();
                break;

            case 0x31:
                if (envelope.CompressedLength != envelope.StoredLength)
                    throw new InvalidDataException(
                        $"PlZ 压缩长度不匹配：头部 {envelope.CompressedLength:N0}，实际 {envelope.StoredLength:N0}。");
                EnsureCompressionRatio(envelope.UncompressedLength, envelope.StoredLength);
                data = await InflateAsync(
                    raw.AsMemory(envelope.PayloadOffset, envelope.StoredLength),
                    envelope.UncompressedLength,
                    _limits.MaximumOutputBytes,
                    cancellationToken);
                break;

            case 0x32:
                // For double-zlib saves the header compressed length is the length of
                // the inner compressed stream, not the outer stored byte count.
                EnsureCompressionRatio(
                    Math.Max(envelope.UncompressedLength, envelope.CompressedLength),
                    envelope.StoredLength);
                var innerCompressed = await InflateAsync(
                    raw.AsMemory(envelope.PayloadOffset, envelope.StoredLength),
                    envelope.CompressedLength,
                    _limits.MaximumOutputBytes,
                    cancellationToken);
                EnsureCompressionRatio(envelope.UncompressedLength, innerCompressed.Length);
                data = await InflateAsync(
                    innerCompressed,
                    envelope.UncompressedLength,
                    _limits.MaximumOutputBytes,
                    cancellationToken);
                break;

            default:
                throw new NotSupportedException(
                    $"不支持的 Palworld PlZ 类型 0x{envelope.SaveType:X2}。{FormatHeader(raw)}");
        }

        ValidateDecompressedGvas(data, envelope.UncompressedLength);
        return new PalworldSavPayload(
            data,
            envelope.SaveType,
            header.CnkWrapped,
            header.CnkWrapped ? "CNK+PlZ" : "PlZ");
    }

    private EnvelopeInfo ReadEnvelope(byte[] raw, int envelopeOffset)
    {
        var uncompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(envelopeOffset, 4));
        var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(envelopeOffset + 4, 4));
        var saveType = raw[envelopeOffset + 11];
        var payloadOffset = envelopeOffset + 12;
        var storedLength = raw.Length - payloadOffset;

        if (uncompressedLength == 0)
            throw new InvalidDataException("存档声明的解压长度为 0。");
        if (uncompressedLength > _limits.MaximumOutputBytes || uncompressedLength > int.MaxValue)
            throw new InvalidDataException(
                $"存档解压后大小 {uncompressedLength:N0} 超过当前解析器限制。");
        if (storedLength <= 0)
            throw new InvalidDataException("Palworld 存档没有压缩数据。");

        return new EnvelopeInfo(
            uncompressedLength,
            compressedLength,
            saveType,
            payloadOffset,
            storedLength);
    }

    private static PalworldSavHeaderInfo InspectHeader(byte[] header, long inputLength)
    {
        var hex = Convert.ToHexString(header.AsSpan(0, Math.Min(header.Length, 32))).ToLowerInvariant();
        var ascii = ToPrintableAscii(header.AsSpan(0, Math.Min(header.Length, 32)));
        if (HasMagic(header, 0, GvasMagic))
        {
            return new PalworldSavHeaderInfo(
                "GVAS", 0x30, false, inputLength, inputLength, inputLength,
                hex, ascii, true, "未压缩 GVAS 存档。");
        }

        if (header.Length >= 12 && HasMagic(header, 8, PlzMagic))
            return BuildPlzInfo(header, inputLength, 0, false, hex, ascii);

        if (header.Length >= 12 && HasMagic(header, 8, PlmMagic))
            return BuildPlmInfo(header, inputLength, 0, false, hex, ascii);

        if (header.Length >= 24 && HasMagic(header, 8, CnkMagic))
        {
            if (HasMagic(header, 20, PlzMagic))
                return BuildPlzInfo(header, inputLength, 12, true, hex, ascii);
            if (HasMagic(header, 20, PlmMagic))
                return BuildPlmInfo(header, inputLength, 12, true, hex, ascii);
        }

        var detected = header.Length >= 11
            ? Encoding.ASCII.GetString(header, 8, Math.Min(3, header.Length - 8))
            : string.Empty;
        return new PalworldSavHeaderInfo(
            string.IsNullOrWhiteSpace(detected) ? "unknown" : detected,
            null,
            HasMagic(header, 8, CnkMagic),
            inputLength,
            null,
            null,
            hex,
            ascii,
            false,
            "未识别为 raw GVAS、PlZ、PlM1 或对应的 CNK 包装。请确认选择的是世界目录中的 Level.sav，" +
            "而不是备份、临时文件或其他 SAV。");
    }

    private static PalworldSavHeaderInfo BuildPlzInfo(
        byte[] header,
        long inputLength,
        int offset,
        bool cnk,
        string hex,
        string ascii)
    {
        var uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset, 4));
        var compressed = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset + 4, 4));
        var type = header[offset + 11];
        var supported = type is 0x30 or 0x31 or 0x32;
        return new PalworldSavHeaderInfo(
            cnk ? "CNK+PlZ" : "PlZ",
            type,
            cnk,
            inputLength,
            compressed,
            uncompressed,
            hex,
            ascii,
            supported,
            supported
                ? $"识别到 {(cnk ? "CNK+PlZ" : "PlZ")}，类型 0x{type:X2}。"
                : $"识别到 PlZ，但类型 0x{type:X2} 尚不支持。");
    }

    private static PalworldSavHeaderInfo BuildPlmInfo(
        byte[] header,
        long inputLength,
        int offset,
        bool cnk,
        string hex,
        string ascii)
    {
        var uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset, 4));
        var compressed = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset + 4, 4));
        var type = header[offset + 11];
        var supported = type == 0x31;
        var format = cnk ? "CNK+PlM1" : "PlM1";
        return new PalworldSavHeaderInfo(
            format,
            type,
            cnk,
            inputLength,
            compressed,
            uncompressed,
            hex,
            ascii,
            supported,
            supported
                ? $"识别到 {format}（Oodle/ooz），类型 0x{type:X2}。解析时将使用程序内置的 GPL ooz 解码器，无需外部 DLL。"
                : $"识别到 PlM，但版本/类型 0x{type:X2} 尚不支持。");
    }

    private static InvalidDataException UnsupportedHeader(byte[] raw, PalworldSavHeaderInfo header)
        => new(
            $"不是受支持的 Palworld 存档包。识别结果：{header.Format}；{header.Message} " +
            $"文件长度：{raw.LongLength:N0}；前 32 字节 HEX：{header.HeaderHex}；ASCII：{header.HeaderAscii}");

    private static void ValidateDecompressedGvas(byte[] data, long expectedLength)
    {
        if (data.LongLength != expectedLength)
            throw new InvalidDataException(
                $"存档解压长度不匹配：期望 {expectedLength:N0}，实际 {data.LongLength:N0}。");
        if (!HasMagic(data, 0, GvasMagic))
            throw new InvalidDataException(
                $"存档包已解压，但内部不是 GVAS 文档。内部头：{FormatHeader(data)}");
    }

    private void EnsureCompressionRatio(long outputLength, long inputLength)
    {
        if (inputLength <= 0) throw new InvalidDataException("压缩数据长度无效。");
        if (outputLength > checked(inputLength * (long)_limits.MaximumCompressionRatio))
            throw new InvalidDataException("存档压缩比超过安全限制。");
    }

    private static bool HasMagic(byte[] source, int offset, ReadOnlySpan<byte> magic)
        => offset >= 0
           && offset + magic.Length <= source.Length
           && source.AsSpan(offset, magic.Length).SequenceEqual(magic);

    private static string FormatHeader(byte[] source)
    {
        var length = Math.Min(source.Length, 32);
        return $"HEX={Convert.ToHexString(source.AsSpan(0, length)).ToLowerInvariant()} " +
               $"ASCII={ToPrintableAscii(source.AsSpan(0, length))}";
    }

    private static string ToPrintableAscii(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var index = 0; index < bytes.Length; index++)
            chars[index] = bytes[index] is >= 32 and <= 126 ? (char)bytes[index] : '.';
        return new string(chars);
    }

    private static async Task<byte[]> ReadAllBoundedAsync(
        Stream input,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (input.CanSeek && (input.Length > maximumBytes || input.Length > int.MaxValue))
            throw new InvalidDataException(
                $"存档大小 {input.Length:N0} 超过当前解析器限制。");

        using var output = new MemoryStream(input.CanSeek && input.Length <= int.MaxValue ? (int)input.Length : 0);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("存档输入超过大小限制。");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static async Task<byte[]> InflateAsync(
        ReadOnlyMemory<byte> compressed,
        long expectedLength,
        long maximumOutputBytes,
        CancellationToken cancellationToken)
    {
        if (expectedLength < 0 || expectedLength > maximumOutputBytes)
            throw new InvalidDataException("解压目标大小无效。");

        await using var source = new MemoryStream(compressed.ToArray(), writable: false);
        await using var zlib = new ZLibStream(source, CompressionMode.Decompress, leaveOpen: false);
        using var output = new MemoryStream(expectedLength <= int.MaxValue ? (int)expectedLength : 0);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        try
        {
            while (true)
            {
                var read = await zlib.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maximumOutputBytes || total > expectedLength)
                    throw new InvalidDataException("存档解压输出超过声明长度或安全限制。");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new InvalidDataException("Palworld 存档 Zlib 数据损坏。", ex);
        }

        return output.ToArray();
    }

    private sealed record EnvelopeInfo(
        uint UncompressedLength,
        uint CompressedLength,
        byte SaveType,
        int PayloadOffset,
        int StoredLength);
}
