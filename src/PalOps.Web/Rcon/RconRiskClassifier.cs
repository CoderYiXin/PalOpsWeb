namespace PalOps.Web.Rcon;

public enum RconRisk
{
    Safe,
    Elevated,
    High
}

public static class RconRiskClassifier
{
    private static readonly HashSet<string> SafeCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "showplayers", "save", "broadcast", "getrconcmds", "version", "getpos",
        "pgbroadcast", "alert", "send", "settime", "reloadcfg", "whitelist_get",
        "getip", "gettechids", "getskinids", "getnearestbase", "tp", "give_exp",
        "givestats", "givetechpoints", "givebosstechpoints", "learntech", "exportguilds"
    };

    private static readonly HashSet<string> HighRiskCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "kick", "ban", "ipban", "banip", "unban", "unbanip", "killnearestbase", "deletebase",
        "delitem", "delitems", "clearinv", "deletepals", "unlearntech", "shutdown",
        "doexit", "stop", "force_stop"
    };

    public static void Validate(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("RCON 指令不能为空。", nameof(command));
        }

        if (command.Length > 4096)
        {
            throw new ArgumentException("RCON 指令不能超过 4096 个字符。", nameof(command));
        }

        if (command.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("RCON 指令不能包含换行符或空字符。", nameof(command));
        }
    }

    public static RconRisk Classify(string command)
    {
        Validate(command);
        var trimmed = RconCommandNormalizer.Normalize(command);
        var separator = trimmed.IndexOfAny([' ', '\t']);
        var name = separator < 0 ? trimmed : trimmed[..separator];

        if (HighRiskCommands.Contains(name)) return RconRisk.High;
        if (SafeCommands.Contains(name)) return RconRisk.Safe;
        return RconRisk.Elevated;
    }
}
