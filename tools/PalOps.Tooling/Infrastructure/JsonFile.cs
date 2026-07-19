using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PalOps.Tooling.Infrastructure;

public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw ToolExitException.Verification($"缺少 JSON 文件：{path}");

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
                   ?? throw ToolExitException.Verification($"JSON 文件为空：{path}");
        }
        catch (JsonException exception)
        {
            throw ToolExitException.Verification($"JSON 格式无效：{path}：{exception.Message}");
        }
    }

    public static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException($"无法确定目录：{path}");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
