using System.Text.Json.Serialization;

namespace PalOps.Web.External;

public sealed record PalworldPlayer(
    string Name,
    string AccountName,
    string PlayerId,
    string UserId,
    string Ip,
    double Ping,
    double LocationX,
    double LocationY,
    int Level,
    int BuildingCount);

public sealed record PalworldServerMetrics(
    double? ServerFps,
    int? CurrentPlayers,
    int? MaximumPlayers,
    double? ServerFrameTimeMilliseconds,
    long? UptimeSeconds);

internal sealed class PalworldServerMetricsPayload
{
    [JsonPropertyName("serverfps")]
    public double? ServerFps { get; set; }

    [JsonPropertyName("currentplayernum")]
    public int? CurrentPlayers { get; set; }

    [JsonPropertyName("maxplayernum")]
    public int? MaximumPlayers { get; set; }

    [JsonPropertyName("serverframetime")]
    public double? ServerFrameTimeMilliseconds { get; set; }

    [JsonPropertyName("uptime")]
    public long? UptimeSeconds { get; set; }
}

internal sealed class PalworldPlayersEnvelope
{
    [JsonPropertyName("players")]
    public List<PalworldPlayerPayload> Players { get; set; } = [];
}

internal sealed class PalworldPlayerPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("accountName")]
    public string AccountName { get; set; } = string.Empty;
    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = string.Empty;
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;
    [JsonPropertyName("ping")]
    public double Ping { get; set; }
    [JsonPropertyName("location_x")]
    public double LocationX { get; set; }
    [JsonPropertyName("location_y")]
    public double LocationY { get; set; }
    [JsonPropertyName("level")]
    public int Level { get; set; }
    [JsonPropertyName("building_count")]
    public int BuildingCount { get; set; }
}

public sealed record PalDefenderPlayer(
    string Name,
    string Ip,
    string PlayerUid,
    string UserId,
    string GuildName,
    string Status,
    double? WorldX,
    double? WorldY,
    double? WorldZ);

internal sealed class PalDefenderPlayersEnvelope
{
    [JsonPropertyName("Players")]
    public List<PalDefenderPlayerPayload> Players { get; set; } = [];
}

internal sealed class PalDefenderPlayerPayload
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("IP")]
    public string Ip { get; set; } = string.Empty;
    [JsonPropertyName("PlayerUID")]
    public string PlayerUid { get; set; } = string.Empty;
    [JsonPropertyName("UserId")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("GuildName")]
    public string GuildName { get; set; } = string.Empty;
    [JsonPropertyName("Status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("WorldLocation")]
    public CoordinatePayload? WorldLocation { get; set; }
}

internal sealed class CoordinatePayload
{
    [JsonPropertyName("x")]
    public double X { get; set; }
    [JsonPropertyName("y")]
    public double Y { get; set; }
    [JsonPropertyName("z")]
    public double Z { get; set; }
}

public sealed record ExternalItemGrant(string ItemId, int Count);
public sealed record ExternalPalGrant(string PalId, int Level);
public sealed record ExternalProgressionGrant(long? Experience, int? TechnologyPoints, int? AncientTechnologyPoints, IReadOnlyDictionary<string, int>? Relics);

internal sealed class ItemsGrantPayload
{
    [JsonPropertyName("Items")]
    public required IReadOnlyList<ItemGrantPayload> Items { get; init; }
}

internal sealed class ItemGrantPayload
{
    [JsonPropertyName("ItemID")]
    public required string ItemId { get; init; }
    [JsonPropertyName("Count")]
    public required int Count { get; init; }
}

internal sealed class PalsGrantPayload
{
    [JsonPropertyName("Pals")]
    public required IReadOnlyList<PalGrantPayload> Pals { get; init; }
}

internal sealed class PalGrantPayload
{
    [JsonPropertyName("PalID")]
    public required string PalId { get; init; }
    [JsonPropertyName("Level")]
    public required int Level { get; init; }
}

internal sealed class ProgressionGrantPayload
{
    [JsonPropertyName("EXP")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Experience { get; init; }

    [JsonPropertyName("TechnologyPoints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TechnologyPoints { get; init; }

    [JsonPropertyName("AncientTechnologyPoints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AncientTechnologyPoints { get; init; }

    [JsonPropertyName("Relics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, int>? Relics { get; init; }
}

internal sealed class LearnTechnologyPayload
{
    [JsonPropertyName("Technology")]
    public required string Technology { get; init; }
}

internal sealed class MessagePayload
{
    [JsonPropertyName("Message")]
    public required string Message { get; init; }
}

internal sealed class SinglePlayerMessagePayload
{
    [JsonPropertyName("SendType")]
    public required string SendType { get; init; }

    [JsonPropertyName("UserID")]
    public required string UserId { get; init; }

    [JsonPropertyName("Message")]
    public required string Message { get; init; }
}

internal sealed class MultiplePlayerMessagePayload
{
    [JsonPropertyName("SendType")]
    public required string SendType { get; init; }

    [JsonPropertyName("UserIDs")]
    public required IReadOnlyList<string> UserIds { get; init; }

    [JsonPropertyName("Message")]
    public required string Message { get; init; }
}

internal sealed class KickPayload
{
    [JsonPropertyName("Reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

internal sealed class PalDefenderErrorEnvelope
{
    [JsonPropertyName("Error")]
    public PalDefenderError? Error { get; set; }
}

internal sealed class PalDefenderError
{
    [JsonPropertyName("Code")]
    public string Code { get; set; } = "EXTERNAL_API_ERROR";
    [JsonPropertyName("Message")]
    public string Message { get; set; } = "PalDefender API 请求失败。";
    [JsonPropertyName("Details")]
    public object? Details { get; set; }
}
