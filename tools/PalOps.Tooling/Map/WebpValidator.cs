using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PalOps.Tooling.Map;

public static class WebpValidator
{
    public static async Task<WebpInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return WebpInspection.Invalid("文件不存在。");

        var info = new FileInfo(path);
        if (info.Length < 12)
            return WebpInspection.Invalid("文件短于 12 字节。", info.Length);

        var header = new byte[12];
        await using (var stream = new FileStream(
                         path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
            if (read != header.Length)
                return WebpInspection.Invalid("无法读取完整 WebP 文件头。", info.Length);
        }

        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8))
            return WebpInspection.Invalid("缺少 RIFF 签名。", info.Length);
        if (!header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return WebpInspection.Invalid("缺少 WEBP 签名。", info.Length);
        var declaredLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)) + 8L;
        if (declaredLength != info.Length)
            return WebpInspection.Invalid(
                $"RIFF 声明长度 {declaredLength} 与文件长度 {info.Length} 不一致。", info.Length);

        await using var hashStream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(hashStream, cancellationToken);
        return WebpInspection.Valid(info.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }
}

public sealed record WebpInspection(bool IsValid, long Length, string Sha256, string Reason)
{
    public static WebpInspection Invalid(string reason, long length = 0) =>
        new(false, length, string.Empty, reason);

    public static WebpInspection Valid(long length, string sha256) =>
        new(true, length, sha256, string.Empty);
}
