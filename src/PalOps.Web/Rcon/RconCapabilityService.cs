using System.Net.Sockets;
using PalOps.Web.Management;
using PalOps.Web.Settings;

namespace PalOps.Web.Rcon;

public enum RconCapabilityStatus
{
    NotProbed,
    Supported,
    Unsupported,
    AuthenticationFailed,
    TransportFailed,
    InvalidResponse
}

public sealed record RconCapability(
    string CommandId,
    string Template,
    string Status,
    string Reason,
    DateTimeOffset? CheckedAt,
    string ResponseSummary);

public interface IRconCapabilityService
{
    IReadOnlyList<RconCapability> GetCapabilities();
    Task<IReadOnlyList<RconCapability>> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed class RconCapabilityService(
    IServerSettingsStore settingsStore,
    IRconClient rconClient,
    ILogger<RconCapabilityService> logger) : IRconCapabilityService
{
    private static readonly (string Id, string Command)[] ReadOnlyCandidates =
    [
        ("palworld.info", "Info"),
        ("palworld.players", "ShowPlayers"),
        ("paldefender.version", "version"),
        ("paldefender.commands", "getrconcmds"),
        ("paldefender.whitelist", "whitelist_get")
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<RconCapability> _capabilities = ReadOnlyCandidates
        .Select(item => new RconCapability(item.Id, item.Command, RconCapabilityStatus.NotProbed.ToString(), "notProbed", null, string.Empty))
        .ToArray();

    public IReadOnlyList<RconCapability> GetCapabilities() => _capabilities;

    public async Task<IReadOnlyList<RconCapability>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await settingsStore.GetAsync(cancellationToken);
            var results = new List<RconCapability>(ReadOnlyCandidates.Length);
            foreach (var candidate in ReadOnlyCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var checkedAt = DateTimeOffset.UtcNow;
                try
                {
                    var result = await rconClient.ExecuteAsync(settings.Rcon, candidate.Command, cancellationToken);
                    var interpretation = RconActionResponseInterpreter.Interpret(result.Response);
                    results.Add(interpretation.Code switch
                    {
                        "OK" => Build(candidate, RconCapabilityStatus.Supported, "accepted", checkedAt, result.Response),
                        "RCON_UNKNOWN_COMMAND" => Build(candidate, RconCapabilityStatus.Unsupported, "unknownCommand", checkedAt, result.Response),
                        "RCON_NO_RESPONSE" => Build(candidate, RconCapabilityStatus.InvalidResponse, "emptyResponse", checkedAt, result.Response),
                        _ => Build(candidate, RconCapabilityStatus.InvalidResponse, interpretation.Code, checkedAt, result.Response)
                    });
                }
                catch (RconException ex)
                {
                    var status = ex.Code.Contains("AUTH", StringComparison.OrdinalIgnoreCase)
                        ? RconCapabilityStatus.AuthenticationFailed
                        : RconCapabilityStatus.TransportFailed;
                    results.Add(Build(candidate, status, ex.Code, checkedAt, ex.Message));
                }
                catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
                {
                    logger.LogWarning(ex, "RCON capability probe failed for {Command}.", candidate.Command);
                    results.Add(Build(candidate, RconCapabilityStatus.TransportFailed, ex.GetType().Name, checkedAt, ex.Message));
                }
            }

            _capabilities = results;
            return _capabilities;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static RconCapability Build(
        (string Id, string Command) candidate,
        RconCapabilityStatus status,
        string reason,
        DateTimeOffset checkedAt,
        string response)
    {
        var summary = response.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (summary.Length > 240) summary = summary[..240];
        return new RconCapability(candidate.Id, candidate.Command, status.ToString(), reason, checkedAt, summary);
    }
}
