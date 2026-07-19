using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PalOps.Web.Configuration;
using PalOps.Web.Contracts;

namespace PalOps.Web.Grants;

public sealed class GrantValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IGrantValidator
{
    void ValidateShape(BulkGrantRequest request);
}

public sealed class GrantValidator(IOptions<AppRuntimeOptions> options) : IGrantValidator
{
    private static readonly Regex SafeResourceIdPattern = new(
        "^[A-Za-z0-9_.:-]{1,128}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedRelics = new(StringComparer.OrdinalIgnoreCase)
    {
        "CapturePower", "HungerReduction", "SwimSpeed", "FoodDecayReduction", "JumpPower",
        "GliderSpeed", "ClimbSpeed", "StatusAilmentResist", "StaminaReduction", "SphereHoming",
        "ExpBonus", "RainbowPassiveRate", "MoveSpeed"
    };

    private readonly AppRuntimeOptions _options = options.Value;

    public void ValidateShape(BulkGrantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PlayerIdentifiers is null || request.Items is null || request.Pals is null)
            throw new GrantValidationException("INVALID_REQUEST", "玩家、物品和帕鲁列表不能为空。");

        var playerCount = request.PlayerIdentifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (playerCount is < 1 || playerCount >  _options.MaxPlayersPerGrant)
            throw new GrantValidationException("INVALID_PLAYER_COUNT", $"每次必须选择 1 到 {_options.MaxPlayersPerGrant} 名玩家。");

        if (request.PlayerIdentifiers.Any(static id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            throw new GrantValidationException("INVALID_PLAYER_IDENTIFIER", "玩家标识无效。");

        var resourceCount = request.Items.Count + request.Pals.Count;
        if (resourceCount > _options.MaxResourcesPerGrant)
            throw new GrantValidationException("TOO_MANY_RESOURCES", $"每次最多选择 {_options.MaxResourcesPerGrant} 种物资。");

        if (request.Items.Count == 0 && request.Pals.Count == 0 && !HasProgression(request.Progression))
            throw new GrantValidationException("EMPTY_GRANT", "至少需要选择一种物品、帕鲁或成长奖励。");

        if (request.Items.Any(static item => !SafeResourceIdPattern.IsMatch(item.ItemId?.Trim() ?? string.Empty) || item.Count <= 0))
            throw new GrantValidationException("INVALID_ITEM", "物品 ID 只能包含字母、数字、下划线、连字符、冒号和点，且数量必须大于 0。");
        var itemUnits = request.Items.Sum(static item => (long)item.Count);
        if (itemUnits > _options.MaxItemUnitsPerGrant)
            throw new GrantValidationException("ITEM_LIMIT_EXCEEDED", $"单次物品总数不能超过 {_options.MaxItemUnitsPerGrant}。");

        if (request.Pals.Any(static pal => !SafeResourceIdPattern.IsMatch(pal.PalId?.Trim() ?? string.Empty) || pal.Level is < 1 or > 100 || pal.Count is < 1 or > 99))
            throw new GrantValidationException("INVALID_PAL", "帕鲁 ID 只能包含字母、数字、下划线、连字符、冒号和点，等级或数量无效。");
        var palCount = request.Pals.Sum(static pal => pal.Count);
        if (palCount > _options.MaxPalsPerGrant)
            throw new GrantValidationException("PAL_LIMIT_EXCEEDED", $"单次帕鲁总数不能超过 {_options.MaxPalsPerGrant}。");

        if (request.Progression is { } progression)
        {
            if (progression.Experience is <= 0 or > 100_000_000 || progression.Experience > _options.MaxExperiencePerGrant)
                throw new GrantValidationException("INVALID_EXPERIENCE", $"经验必须在 1 到 {_options.MaxExperiencePerGrant} 之间。");
            if (progression.TechnologyPoints is <= 0 or > 100_000)
                throw new GrantValidationException("INVALID_TECH_POINTS", "科技点必须在 1 到 100000 之间。");
            if (progression.AncientTechnologyPoints is <= 0 or > 100_000)
                throw new GrantValidationException("INVALID_ANCIENT_TECH_POINTS", "古代科技点必须在 1 到 100000 之间。");
            if (progression.Relics is not null && progression.Relics.Any(pair => !SupportedRelics.Contains(pair.Key) || pair.Value is <= 0 or > 100_000))
                throw new GrantValidationException("INVALID_RELICS", "遗物类型或数量无效。");
        }
    }

    private static bool HasProgression(ProgressionGrantRequest? progression)
        => progression is not null && (progression.Experience is > 0 || progression.TechnologyPoints is > 0 || progression.AncientTechnologyPoints is > 0 || progression.Relics?.Count > 0);
}
