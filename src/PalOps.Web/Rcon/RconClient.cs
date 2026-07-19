using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using PalOps.Web.Settings;

namespace PalOps.Web.Rcon;

public sealed class RconException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record RconExecutionResult(string Response, long ElapsedMilliseconds);

public interface IRconClient
{
    Task<RconExecutionResult> ExecuteAsync(RconConnection connection, string command, CancellationToken cancellationToken = default);
}

public sealed class RconClient : IRconClient
{
    private const int AuthPacketType = 3;
    private const int AuthResponseType = 2;
    private const int ExecutePacketType = 2;

    // 部分 Palworld RCON 实现只对请求 ID 0 稳定响应，因此认证与命令均使用兼容 ID。
    private const int AuthenticationPacketId = 0;
    private const int CommandPacketId = 0;

    public async Task<RconExecutionResult> ExecuteAsync(RconConnection connection, string command, CancellationToken cancellationToken = default)
    {
        var normalizedCommand = RconCommandNormalizer.Normalize(command);
        RconRiskClassifier.Validate(normalizedCommand);
        ValidateConnection(connection);
        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(connection.TimeoutSeconds));

        var stage = "连接";
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(connection.Host, connection.Port, timeoutSource.Token);
            await using var stream = client.GetStream();

            stage = "认证";
            await WritePacketAsync(
                stream,
                new RconPacket(AuthenticationPacketId, AuthPacketType, connection.Password),
                timeoutSource.Token);
            await AuthenticateAsync(stream, AuthenticationPacketId, timeoutSource.Token);

            stage = "执行";
            var transmitted = connection.Base64 ? RconBase64Codec.EncodeCommand(normalizedCommand) : normalizedCommand;
            await WritePacketAsync(
                stream,
                new RconPacket(CommandPacketId, ExecutePacketType, transmitted),
                timeoutSource.Token);

            var rawResponse = await ReadCommandResponseAsync(stream, CommandPacketId, timeoutSource.Token);
            var decoded = connection.Base64 ? DecodeBase64Response(rawResponse) : rawResponse;
            stopwatch.Stop();
            return new RconExecutionResult(decoded, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RconException("RCON_TIMEOUT", $"RCON {stage}超时（{connection.TimeoutSeconds} 秒）。");
        }
        catch (SocketException ex)
        {
            throw new RconException("RCON_UNREACHABLE", "无法连接 RCON 服务。", ex);
        }
        catch (IOException ex)
        {
            throw new RconException("RCON_PROTOCOL_ERROR", "RCON 数据传输失败。", ex);
        }
    }

    private static async Task AuthenticateAsync(Stream stream, int authId, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 3; i++)
        {
            var packet = await RconPacketCodec.ReadAsync(stream, cancellationToken);
            if (packet.Type != AuthResponseType) continue;
            if (packet.Id == -1)
            {
                throw new RconException("RCON_AUTH_FAILED", "RCON 密码错误或服务端拒绝认证。");
            }
            if (packet.Id == authId) return;
        }

        throw new RconException("RCON_AUTH_PROTOCOL", "未收到有效的 RCON 认证响应。");
    }

    private static async Task<string> ReadCommandResponseAsync(Stream stream, int executeId, CancellationToken overallToken)
    {
        var builder = new StringBuilder();
        var received = false;
        int? acceptedResponseId = null;

        while (true)
        {
            using var idleSource = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
            if (received) idleSource.CancelAfter(TimeSpan.FromMilliseconds(750));

            try
            {
                var packet = await RconPacketCodec.ReadAsync(stream, idleSource.Token);
                if (packet.Id == -1)
                {
                    throw new RconException("RCON_EXECUTION_REJECTED", "RCON 服务端拒绝指令。");
                }

                var matchesResponse = packet.Id == executeId
                    || (acceptedResponseId.HasValue && packet.Id == acceptedResponseId.Value)
                    || (!received && packet.Id == 0 && packet.Type == 0);
                if (!matchesResponse) continue;

                received = true;
                acceptedResponseId ??= packet.Id;
                builder.Append(packet.Body);
                if (packet.Body.Length == 0) break;
            }
            catch (EndOfStreamException) when (received)
            {
                break;
            }
            catch (OperationCanceledException) when (received && !overallToken.IsCancellationRequested)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string DecodeBase64Response(string rawResponse)
    {
        try
        {
            return RconBase64Codec.DecodeResponseOrPlainText(rawResponse);
        }
        catch (FormatException ex)
        {
            throw new RconException("RCON_BASE64_DECODE_FAILED", "RCON 返回值既不是有效的 Base64，也不是可显示的普通文本。请检查 PalDefender RCON 输出和网络传输。", ex);
        }
    }

    private static async Task WritePacketAsync(Stream stream, RconPacket packet, CancellationToken cancellationToken)
    {
        var bytes = RconPacketCodec.Encode(packet);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static void ValidateConnection(RconConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Host)) throw new RconException("RCON_NOT_CONFIGURED", "RCON 主机未配置。");
        if (connection.Port is < 1 or > 65535) throw new RconException("RCON_INVALID_PORT", "RCON 端口无效。");
        if (string.IsNullOrWhiteSpace(connection.Password)) throw new RconException("RCON_NOT_CONFIGURED", "RCON 密码未配置。");
        if (connection.TimeoutSeconds is < 3 or > 120) throw new RconException("RCON_INVALID_TIMEOUT", "RCON 超时必须在 3 到 120 秒之间。");
    }
}
