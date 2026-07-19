using System.Buffers.Binary;
using System.Text;

namespace PalOps.Web.Rcon;

public sealed record RconPacket(int Id, int Type, string Body);

public static class RconPacketCodec
{
    public const int MaximumPacketSize = 1_048_576;

    public static byte[] Encode(RconPacket packet)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(packet.Body);
        var payloadSize = 4 + 4 + bodyBytes.Length + 2;
        if (payloadSize > MaximumPacketSize)
        {
            throw new ArgumentOutOfRangeException(nameof(packet), "RCON 数据包过大。");
        }

        var bytes = new byte[payloadSize + 4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), payloadSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), packet.Id);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), packet.Type);
        bodyBytes.CopyTo(bytes.AsSpan(12));
        bytes[^2] = 0;
        bytes[^1] = 0;
        return bytes;
    }

    public static RconPacket DecodePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 10 || payload.Length > MaximumPacketSize)
        {
            throw new InvalidDataException("RCON 数据包长度无效。");
        }

        if (payload[^2] != 0 || payload[^1] != 0)
        {
            throw new InvalidDataException("RCON 数据包缺少结束符。");
        }

        var id = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
        var type = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
        var body = Encoding.UTF8.GetString(payload.Slice(8, payload.Length - 10));
        return new RconPacket(id, type, body);
    }

    public static async Task<RconPacket> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var sizeBuffer = new byte[4];
        await ReadExactlyAsync(stream, sizeBuffer, cancellationToken);
        var size = BinaryPrimitives.ReadInt32LittleEndian(sizeBuffer);
        if (size < 10 || size > MaximumPacketSize)
        {
            throw new InvalidDataException($"RCON 数据包长度 {size} 无效。");
        }

        var payload = new byte[size];
        await ReadExactlyAsync(stream, payload, cancellationToken);
        return DecodePayload(payload);
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("RCON 连接在数据包读取完成前关闭。");
            }
            offset += read;
        }
    }
}
