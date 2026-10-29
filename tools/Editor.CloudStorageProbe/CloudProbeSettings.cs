using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Editor.CloudStorageProbe;

/// <summary>
/// A storage-only POC: no FFmpeg, D: repository, cloud compute, or production
/// media-processing admission is modified by this experiment.
/// </summary>
public sealed record CloudProbeSettings(
    string Provider, Uri Endpoint, string SigningRegion, string Bucket,
    string AccessKeyId, string SecretAccessKey)
{
    // Do not accept arbitrary user-supplied endpoints: a wrong endpoint can
    // leak SigV4 authorization material and signed object operations.
    private static readonly Regex Label = new(
        "^[a-z0-9][a-z0-9-]{0,62}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CloudflareAccount = new(
        "^[a-f0-9]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BucketLabel = new(
        "^[a-z0-9][a-z0-9._-]{1,62}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CloudProbeSettings Oracle(
        string storageNamespace, string region, string bucket,
        string accessKeyId, string secretAccessKey)
    {
        if (!Label.IsMatch(storageNamespace) || !Label.IsMatch(region))
            throw new ArgumentException("Invalid Oracle namespace or region label.");
        ValidateBucketAndKey(bucket, accessKeyId, secretAccessKey);
        return new CloudProbeSettings("oracle",
            new Uri($"https://{storageNamespace}.compat.objectstorage.{region}.oci.customer-oci.com"),
            region, bucket, accessKeyId, secretAccessKey);
    }

    public static CloudProbeSettings R2(
        string accountId, string bucket, string accessKeyId, string secretAccessKey)
    {
        if (!CloudflareAccount.IsMatch(accountId))
            throw new ArgumentException("Invalid Cloudflare account identifier.");
        ValidateBucketAndKey(bucket, accessKeyId, secretAccessKey);
        return new CloudProbeSettings("r2",
            new Uri($"https://{accountId}.r2.cloudflarestorage.com"),
            "auto", bucket, accessKeyId, secretAccessKey);
    }

    private static void ValidateBucketAndKey(
        string bucket, string accessKeyId, string secretAccessKey)
    {
        if (string.IsNullOrEmpty(bucket) || !BucketLabel.IsMatch(bucket) ||
            bucket.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid bucket name.");
        if (string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(secretAccessKey))
            throw new ArgumentException("Missing restricted bucket credentials.");
    }

    public static CloudProbeSettings FromEnvironment(string provider)
    {
        static string Need(string key) => Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException(
                $"Required secret/configuration missing: {key} (never include its value in logs).");

        return provider switch
        {
            "oracle" => Oracle(Need("OCI_NAMESPACE"), Need("OCI_REGION"),
                Need("OCI_BUCKET"), Need("OCI_ACCESS_KEY_ID"), Need("OCI_SECRET_ACCESS_KEY")),
            "r2" => R2(Need("R2_ACCOUNT_ID"), Need("R2_BUCKET"),
                Need("R2_ACCESS_KEY_ID"), Need("R2_SECRET_ACCESS_KEY")),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    public override string ToString() => $"{Provider} (bucket redacted; credentials redacted)";
}

public static class CloudRangeRules
{
    // A media editor must not download an entire 10 GiB original to seek.
    public const int MaxProbeRangeBytes = 1 * 1024 * 1024;

    public static (long Start, long End) CheckedRange(
        long start, int length, long objectSize)
    {
        if (start < 0 || length < 1 || length > MaxProbeRangeBytes ||
            objectSize < 1 || start >= objectSize || length > objectSize - start)
            throw new ArgumentOutOfRangeException(nameof(start),
                "Range must be bounded, contained in the object, and <=1MiB.");
        return (start, checked(start + length - 1L));
    }

    public static bool MatchesFixture(
        byte[] actualRange, byte[] original, long start, int length)
    {
        ArgumentNullException.ThrowIfNull(actualRange);
        ArgumentNullException.ThrowIfNull(original);
        var bounds = CheckedRange(start, length, original.LongLength);
        if (bounds.End >= original.LongLength || actualRange.Length != length)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            actualRange, original.AsSpan(checked((int)start), length));
    }
}
