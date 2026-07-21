using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PalOps.Web.SaveGames.Diff;

public sealed record SaveDiffExportDocument(byte[] Content, string ContentType, string Extension);

public interface ISaveDiffReportWriter
{
    Task<SaveDiffExportDocument> WriteJsonAsync(SaveDiffReport report, CancellationToken cancellationToken = default);
    Task<SaveDiffExportDocument> WriteCsvAsync(SaveDiffReport report, CancellationToken cancellationToken = default);
    Task<SaveDiffExportDocument> WriteMarkdownAsync(SaveDiffReport report, CancellationToken cancellationToken = default);
}

public sealed class SaveDiffReportWriter : ISaveDiffReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Task<SaveDiffExportDocument> WriteJsonAsync(SaveDiffReport report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SaveDiffExportDocument(
            JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions),
            "application/json",
            "json"));
    }

    public Task<SaveDiffExportDocument> WriteCsvAsync(SaveDiffReport report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new StringBuilder();
        CsvRow(builder, "Category", "ChangeType", "EntityId", "Name", "Field", "Before", "After", "Delta", "Important", "Severity", "Description");

        foreach (var change in report.PlayerChanges.Items)
            CsvRow(builder, "Player", change.ChangeType, change.PlayerUid, FirstNonEmpty(change.AfterName, change.BeforeName), string.Join("|", change.ChangedFields),
                change.BeforeLevel, change.AfterLevel, change.LevelDelta, false, string.Empty, string.Empty);
        foreach (var change in report.GuildChanges.Items)
            CsvRow(builder, "Guild", change.ChangeType, change.GuildId, FirstNonEmpty(change.AfterName, change.BeforeName), string.Join("|", change.ChangedFields),
                change.BeforeLeaderPlayerUid, change.AfterLeaderPlayerUid, change.AfterMemberCount - change.BeforeMemberCount, false, string.Empty,
                $"members +{change.AddedMemberPlayerUids.Count}/-{change.RemovedMemberPlayerUids.Count}");
        foreach (var change in report.BaseChanges.Items)
            CsvRow(builder, "Base", change.ChangeType, change.BaseId, change.BaseId, string.Join("|", change.ChangedFields),
                Coordinates(change.BeforeX, change.BeforeY, change.BeforeZ), Coordinates(change.AfterX, change.AfterY, change.AfterZ), change.DistanceMoved, false, string.Empty, string.Empty);
        foreach (var change in report.ItemChanges.Items)
            CsvRow(builder, "Item", change.ChangeType, $"{change.PlayerUid}:{change.ItemId}", change.ItemName, change.ContainerType,
                change.BeforeQuantity, change.AfterQuantity, change.QuantityDelta, change.Important, string.Empty, change.PlayerName);
        foreach (var change in report.PalChanges.Items)
            CsvRow(builder, "Pal", change.ChangeType, $"{change.PlayerUid}:{change.PalId}", change.PalName, "count",
                change.BeforeCount, change.AfterCount, change.CountDelta, false, string.Empty, change.PlayerName);
        foreach (var anomaly in report.Anomalies)
            CsvRow(builder, "Anomaly", string.Empty, anomaly.EntityId, anomaly.Title, anomaly.Rule,
                anomaly.BeforeValue, anomaly.AfterValue, anomaly.ChangePercent, false, anomaly.Severity, anomaly.Description);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return Task.FromResult(new SaveDiffExportDocument(utf8.GetBytes(builder.ToString()), "text/csv", "csv"));
    }

    public Task<SaveDiffExportDocument> WriteMarkdownAsync(SaveDiffReport report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = new StringBuilder();
        builder.AppendLine("# PalOps 存档差异报告");
        builder.AppendLine();
        builder.AppendLine($"- 世界：`{Markdown(report.To.WorldId)}`");
        builder.AppendLine($"- 源快照：`{Markdown(report.From.SnapshotId)}`（{report.From.ParsedAt:O}）");
        builder.AppendLine($"- 目标快照：`{Markdown(report.To.SnapshotId)}`（{report.To.ParsedAt:O}）");
        builder.AppendLine($"- 生成时间：{report.GeneratedAt:O}");
        builder.AppendLine("- 模式：只读差异分析，不包含恢复或写回操作。");
        builder.AppendLine();
        builder.AppendLine("## 摘要");
        builder.AppendLine();
        builder.AppendLine($"玩家 +{report.Summary.AddedPlayers}/-{report.Summary.RemovedPlayers}/变更 {report.Summary.ChangedPlayers}；公会 {report.Summary.ChangedGuilds}；据点 {report.Summary.ChangedBases}；物品 {report.Summary.ChangedItems}；帕鲁 {report.Summary.ChangedPals}；异常 {report.Summary.AnomalyCount}。");

        AppendAnomalies(builder, report.Anomalies);
        AppendPlayers(builder, report.PlayerChanges.Items);
        AppendGuilds(builder, report.GuildChanges.Items);
        AppendBases(builder, report.BaseChanges.Items);
        AppendItems(builder, report.ItemChanges.Items);
        AppendPals(builder, report.PalChanges.Items);

        return Task.FromResult(new SaveDiffExportDocument(Encoding.UTF8.GetBytes(builder.ToString()), "text/markdown", "md"));
    }

    private static void AppendAnomalies(StringBuilder builder, IReadOnlyList<SaveDiffAnomaly> anomalies)
    {
        builder.AppendLine();
        builder.AppendLine("## 异常提示");
        builder.AppendLine();
        if (anomalies.Count == 0) { builder.AppendLine("未命中异常大幅变化规则。"); return; }
        builder.AppendLine("| 严重级别 | 规则 | 实体 | 说明 |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var anomaly in anomalies)
            builder.AppendLine($"| {anomaly.Severity} | {Markdown(anomaly.Rule)} | {Markdown(anomaly.EntityId)} | {Markdown(anomaly.Description)} |");
    }

    private static void AppendPlayers(StringBuilder builder, IReadOnlyList<SaveDiffPlayerChange> changes)
    {
        builder.AppendLine(); builder.AppendLine("## 玩家变化"); builder.AppendLine();
        builder.AppendLine("| 类型 | 玩家 | 等级 | 公会 | 字段 |"); builder.AppendLine("|---|---|---|---|---|");
        foreach (var change in changes)
            builder.AppendLine($"| {change.ChangeType} | {Markdown(FirstNonEmpty(change.AfterName, change.BeforeName, change.PlayerUid))} | {change.BeforeLevel} → {change.AfterLevel} | {Markdown(change.BeforeGuildId)} → {Markdown(change.AfterGuildId)} | {Markdown(string.Join(", ", change.ChangedFields))} |");
    }

    private static void AppendGuilds(StringBuilder builder, IReadOnlyList<SaveDiffGuildChange> changes)
    {
        builder.AppendLine(); builder.AppendLine("## 公会变化"); builder.AppendLine();
        builder.AppendLine("| 类型 | 公会 | 会长 | 成员变化 | 字段 |"); builder.AppendLine("|---|---|---|---|---|");
        foreach (var change in changes)
            builder.AppendLine($"| {change.ChangeType} | {Markdown(FirstNonEmpty(change.AfterName, change.BeforeName, change.GuildId))} | {Markdown(change.BeforeLeaderPlayerUid)} → {Markdown(change.AfterLeaderPlayerUid)} | +{change.AddedMemberPlayerUids.Count}/-{change.RemovedMemberPlayerUids.Count} | {Markdown(string.Join(", ", change.ChangedFields))} |");
    }

    private static void AppendBases(StringBuilder builder, IReadOnlyList<SaveDiffBaseChange> changes)
    {
        builder.AppendLine(); builder.AppendLine("## 据点变化"); builder.AppendLine();
        builder.AppendLine("| 类型 | 据点 | 公会 | 移动距离 | 字段 |"); builder.AppendLine("|---|---|---|---:|---|");
        foreach (var change in changes)
            builder.AppendLine($"| {change.ChangeType} | {Markdown(change.BaseId)} | {Markdown(change.BeforeGuildId)} → {Markdown(change.AfterGuildId)} | {Format(change.DistanceMoved)} | {Markdown(string.Join(", ", change.ChangedFields))} |");
    }

    private static void AppendItems(StringBuilder builder, IReadOnlyList<SaveDiffItemChange> changes)
    {
        builder.AppendLine(); builder.AppendLine("## 物品变化"); builder.AppendLine();
        builder.AppendLine("| 类型 | 玩家 | 物品 | 容器 | 数量 | 重要 |"); builder.AppendLine("|---|---|---|---|---:|---|");
        foreach (var change in changes)
            builder.AppendLine($"| {change.ChangeType} | {Markdown(change.PlayerName)} | {Markdown(change.ItemName)} | {Markdown(change.ContainerType)} | {change.BeforeQuantity} → {change.AfterQuantity} ({change.QuantityDelta:+#;-#;0}) | {(change.Important ? "是" : "否")} |");
    }

    private static void AppendPals(StringBuilder builder, IReadOnlyList<SaveDiffPalChange> changes)
    {
        builder.AppendLine(); builder.AppendLine("## 帕鲁变化"); builder.AppendLine();
        builder.AppendLine("| 类型 | 玩家 | 帕鲁 | 数量 | 平均等级 |"); builder.AppendLine("|---|---|---|---:|---:|");
        foreach (var change in changes)
            builder.AppendLine($"| {change.ChangeType} | {Markdown(change.PlayerName)} | {Markdown(change.PalName)} | {change.BeforeCount} → {change.AfterCount} ({change.CountDelta:+#;-#;0}) | {change.BeforeAverageLevel:F1} → {change.AfterAverageLevel:F1} |");
    }

    private static void CsvRow(StringBuilder builder, params object?[] values)
        => builder.AppendLine(string.Join(",", values.Select(value => EscapeCsv(NeutralizeFormula(ToInvariant(value))))));

    public static string EscapeCsv(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    public static string NeutralizeFormula(string value)
        => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static string ToInvariant(object? value)
        => value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };

    private static string Coordinates(double? x, double? y, double? z)
        => x.HasValue && y.HasValue ? $"{x:F1},{y:F1},{(z ?? 0):F1}" : string.Empty;

    private static string Format(double? value) => value?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Markdown(string? value)
        => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
