using System.Text.Json;
using PalOps.Web.Contracts;
using PalOps.Web.Infrastructure;

namespace PalOps.Web.Map;

public interface ICustomMapMarkerRepository
{
    Task<IReadOnlyList<CustomMapMarkerV1>> ListAsync(CancellationToken cancellationToken = default);
    Task<CustomMapMarkerV1> CreateAsync(CustomMapMarkerWriteRequest request, CancellationToken cancellationToken = default);
    Task<CustomMapMarkerV1> UpdateAsync(string id, CustomMapMarkerWriteRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class JsonCustomMapMarkerRepository : ICustomMapMarkerRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonCustomMapMarkerRepository(IRuntimePathResolver paths)
    {
        var directory = paths.ResolveDataPath("map");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "custom-markers.json");
    }

    public async Task<IReadOnlyList<CustomMapMarkerV1>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadAndMigrateWithoutLockAsync(cancellationToken);
            return items.Select(ToContract).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CustomMapMarkerV1> CreateAsync(
        CustomMapMarkerWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalized = Normalize(
            Guid.NewGuid().ToString("N"), request, now, now, current: null);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadAndMigrateWithoutLockAsync(cancellationToken)).ToList();
            items.Add(normalized);
            await WriteWithoutLockAsync(items, cancellationToken);
            return ToContract(normalized);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CustomMapMarkerV1> UpdateAsync(
        string id,
        CustomMapMarkerWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadAndMigrateWithoutLockAsync(cancellationToken)).ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new KeyNotFoundException("自定义地图标记不存在。");
            var current = items[index];
            var updated = Normalize(
                current.Id, request, current.CreatedAt, DateTimeOffset.UtcNow, current);
            items[index] = updated;
            await WriteWithoutLockAsync(items, cancellationToken);
            return ToContract(updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadAndMigrateWithoutLockAsync(cancellationToken)).ToList();
            if (items.RemoveAll(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0)
                throw new KeyNotFoundException("自定义地图标记不存在。");
            await WriteWithoutLockAsync(items, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<StoredCustomMapMarker>> ReadAndMigrateWithoutLockAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return [];

        List<StoredCustomMapMarker> items;
        await using (var stream = new FileStream(
                         _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                         64 * 1024, useAsync: true))
        {
            items = await JsonSerializer.DeserializeAsync<List<StoredCustomMapMarker>>(
                        stream, JsonOptions, cancellationToken)
                    ?? [];
        }

        var migrated = false;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var mapLayer = NormalizeStoredLayer(item.MapLayer);
            var coordinateSpace = NormalizeStoredCoordinateSpace(item.CoordinateSpace);
            if (mapLayer == item.MapLayer && coordinateSpace == item.CoordinateSpace)
                continue;
            items[index] = item with
            {
                MapLayer = mapLayer,
                CoordinateSpace = coordinateSpace
            };
            migrated = true;
        }

        if (migrated)
            await WriteWithoutLockAsync(items, cancellationToken);
        return items;
    }

    private async Task WriteWithoutLockAsync(
        IReadOnlyList<StoredCustomMapMarker> items,
        CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    items.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static StoredCustomMapMarker Normalize(
        string id,
        CustomMapMarkerWriteRequest request,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        StoredCustomMapMarker? current)
    {
        var label = (request.Label ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (label.Length is < 1 or > 100)
            throw new ArgumentException("标记名称长度必须在 1 到 100 个字符之间。");
        var description = (request.Description ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (description.Length > 500)
            description = description[..500];
        var category = (request.Category ?? "custom").Trim();
        if (category.Length is < 1 or > 50)
            category = "custom";
        foreach (var coordinate in new[] { request.X, request.Y, request.Z })
        {
            if (!double.IsFinite(coordinate))
                throw new ArgumentException("地图坐标无效。");
        }

        var mapLayer = string.IsNullOrWhiteSpace(request.MapLayer)
            ? current?.MapLayer ?? "palpagos"
            : NormalizeRequestedLayer(request.MapLayer);
        var coordinateSpace = string.IsNullOrWhiteSpace(request.CoordinateSpace)
            ? current?.CoordinateSpace ?? "game-map"
            : NormalizeRequestedCoordinateSpace(request.CoordinateSpace);
        return new StoredCustomMapMarker(
            id,
            label,
            description,
            request.X,
            request.Y,
            request.Z,
            category,
            createdAt,
            updatedAt,
            mapLayer,
            coordinateSpace);
    }

    private static CustomMapMarkerV1 ToContract(StoredCustomMapMarker item)
    {
        var warning = item.CoordinateSpace == "legacy-inferred"
            ? $"该旧标记缺少坐标空间，当前推断为 {InferCoordinateSpace(item.X, item.Y)}；请编辑并确认。"
            : null;
        return new CustomMapMarkerV1(
            item.Id,
            item.Label,
            item.Description,
            item.X,
            item.Y,
            item.Z,
            item.Category,
            item.CreatedAt,
            item.UpdatedAt,
            item.MapLayer ?? "palpagos",
            item.CoordinateSpace ?? "legacy-inferred",
            warning);
    }

    private static string NormalizeStoredLayer(string? value) =>
        value?.Trim().ToLowerInvariant() is "palpagos" or "world-tree"
            ? value.Trim().ToLowerInvariant()
            : "palpagos";

    private static string NormalizeStoredCoordinateSpace(string? value) =>
        value?.Trim().ToLowerInvariant() is "world" or "game-map" or "legacy-inferred"
            ? value.Trim().ToLowerInvariant()
            : "legacy-inferred";

    private static string NormalizeRequestedLayer(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "palpagos" or "world-tree"
            ? normalized
            : throw new ArgumentException("地图图层必须是 palpagos 或 world-tree。");
    }

    private static string NormalizeRequestedCoordinateSpace(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "world" or "game-map"
            ? normalized
            : throw new ArgumentException("坐标空间必须是 world 或 game-map。");
    }

    private static string InferCoordinateSpace(double x, double y) =>
        Math.Abs(x) > 10_000 || Math.Abs(y) > 10_000 ? "world" : "game-map";

    private sealed record StoredCustomMapMarker(
        string Id,
        string Label,
        string Description,
        double X,
        double Y,
        double Z,
        string Category,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? MapLayer,
        string? CoordinateSpace);
}
