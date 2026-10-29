using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Editor.CloudStorageProbe;

/// <summary>
/// Explicit opt-in live benchmark of pre-existing private buckets.
/// It creates only random synthetic objects under a unique POC prefix and
/// deletes only those objects. No original media, accounts or buckets changed.
/// </summary>
internal static class Program
{
    private const int MiB = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--live")
        {
            Console.Error.WriteLine(
                "Run offline xUnit tests with tests\\testar-nuvem.cmd. " +
                "Live OCI/R2 comparison requires --live and restricted secrets.");
            return 2;
        }

        const string relativeReport = "TestResults/cloud-poc/live-comparison.json";
        Directory.CreateDirectory(Path.GetDirectoryName(relativeReport)!);
        var report = new CloudComparisonReport
        {
            EvaluatedUtc = DateTimeOffset.UtcNow,
            Scenario = "64KiB PUT/full GET; 13MiB multipart (12+1); 3x bounded ranged GET",
            RealProvidersTested = false,
            ProductionApproved = false,
            PersonalMediaAccessed = false
        };
        try
        {
            // Fail BEFORE any cloud write unless BOTH providers are configured.
            // This ensures benchmark conditions are comparable and avoids a
            // surprise partial paid test in a single provider.
            var oracle = CloudProbeSettings.FromEnvironment("oracle");
            var r2 = CloudProbeSettings.FromEnvironment("r2");
            report.RealProvidersTested = true;

            byte[] small = new byte[64 * 1024];
            byte[] large = new byte[13 * MiB];
            RandomNumberGenerator.Fill(small);
            RandomNumberGenerator.Fill(large);
            // Same test data for both providers; never use user media.
            foreach (var cfg in new[] { oracle, r2 })
            {
                report.Results.Add(await ProbeAsync(cfg, small, large));
            }
        }
        catch (Exception ex)
        {
            report.SetupError = SafeError(ex);
        }
        finally
        {
            await File.WriteAllTextAsync(relativeReport,
                JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine("Evidence written to " + relativeReport);
        }

        bool pass = report.RealProvidersTested && report.SetupError is null &&
            report.Results.Count == 2 &&
            report.Results.All(p => p.Passed && p.CleanupSucceeded);
        Console.WriteLine(pass
            ? "Both cloud buckets passed synthetic S3 conformance. Production remains blocked."
            : "Cloud live comparison INCOMPLETE or FAILED; inspect sanitized JSON.");
        return pass ? 0 : 1;
    }

    private static async Task<ProviderResult> ProbeAsync(
        CloudProbeSettings cfg, byte[] small, byte[] large)
    {
        var result = new ProviderResult { Provider = cfg.Provider };
        string prefix = "editor-cloud-poc/" + Guid.NewGuid().ToString("N") + "/";
        string smallKey = prefix + "small-test.bin";
        string largeKey = prefix + "multipart-test.bin";
        bool multipartCreated = false;
        string? uploadId = null;
        try
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL = cfg.Endpoint.ToString().TrimEnd('/'),
                AuthenticationRegion = cfg.SigningRegion,
                ForcePathStyle = true,
                Timeout = TimeSpan.FromSeconds(90),
                MaxErrorRetry = 2
            };
            using var client = new AmazonS3Client(
                new BasicAWSCredentials(cfg.AccessKeyId, cfg.SecretAccessKey),
                s3Config);
            var watch = Stopwatch.StartNew();
            using (var input = new MemoryStream(small, writable: false))
            {
                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = cfg.Bucket, Key = smallKey,
                    InputStream = input, ContentType = "application/octet-stream"
                });
            }
            result.SmallPutMs = watch.ElapsedMilliseconds;

            watch.Restart();
            using (var response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = cfg.Bucket, Key = smallKey }))
            using (var destination = new MemoryStream())
            {
                await response.ResponseStream.CopyToAsync(destination);
                if (!CryptographicOperations.FixedTimeEquals(
                        small, destination.ToArray()))
                    throw new InvalidDataException("Small-object SHA256 bytes mismatch.");
            }
            result.SmallGetMs = watch.ElapsedMilliseconds;
            result.SmallObjectSha256 = Convert.ToHexString(SHA256.HashData(small));

            watch.Restart();
            var started = await client.InitiateMultipartUploadAsync(
                new InitiateMultipartUploadRequest
                {
                    BucketName = cfg.Bucket, Key = largeKey
                });
            multipartCreated = true;
            uploadId = started.UploadId;
            var parts = new List<PartETag>();
            foreach (var (number, offset, count) in new[]
            {
                (1, 0, 12 * MiB),
                (2, 12 * MiB, 1 * MiB)
            })
            {
                using var piece = new MemoryStream(large, offset, count,
                    writable: false, publiclyVisible: false);
                var response = await client.UploadPartAsync(new UploadPartRequest
                {
                    BucketName = cfg.Bucket,
                    Key = largeKey,
                    UploadId = uploadId,
                    PartNumber = number,
                    PartSize = count,
                    InputStream = piece
                });
                parts.Add(new PartETag { PartNumber = number, ETag = response.ETag });
            }
            await client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = cfg.Bucket,
                    Key = largeKey,
                    UploadId = uploadId,
                    PartETags = parts
                });
            multipartCreated = false;
            result.MultipartMs = watch.ElapsedMilliseconds;

            var metadata = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = cfg.Bucket, Key = largeKey
                });
            if (metadata.ContentLength != large.LongLength)
                throw new InvalidDataException("Multipart object size mismatch.");
            result.MultipartBytes = large.Length;
            // Tests a boundary crossing a 12MiB part, a middle slice and tail.
            foreach (var (offset, length) in new (long, int)[]
            {
                (0, 4096), (12L * MiB - 2048, 4096), (large.LongLength - 4096, 4096)
            })
            {
                var range = CloudRangeRules.CheckedRange(offset, length, large.Length);
                watch.Restart();
                using var response = await client.GetObjectAsync(
                    new GetObjectRequest
                    {
                        BucketName = cfg.Bucket,
                        Key = largeKey,
                        ByteRange = new ByteRange(range.Start, range.End)
                    });
                using var output = new MemoryStream();
                await response.ResponseStream.CopyToAsync(output);
                if (response.HttpStatusCode != HttpStatusCode.PartialContent ||
                    !CloudRangeRules.MatchesFixture(output.ToArray(),
                        large, offset, length))
                    throw new InvalidDataException(
                        "Range response was not HTTP 206 with exact expected data.");
                result.RangeMs.Add(watch.ElapsedMilliseconds);
                result.RangeBytes += output.Length;
            }

            result.Passed = true;
        }
        catch (Exception ex)
        {
            result.Error = SafeError(ex);
        }
        finally
        {
            // Strict cleanup under uniquely generated keys only. Never list or
            // delete users' existing buckets, prefixes or personal objects.
            try
            {
                var config = new AmazonS3Config
                {
                    ServiceURL = cfg.Endpoint.ToString().TrimEnd('/'),
                    AuthenticationRegion = cfg.SigningRegion,
                    ForcePathStyle = true,
                    Timeout = TimeSpan.FromSeconds(45)
                };
                using var client = new AmazonS3Client(
                    new BasicAWSCredentials(cfg.AccessKeyId, cfg.SecretAccessKey),
                    config);
                if (multipartCreated && !string.IsNullOrEmpty(uploadId))
                    await client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                    {
                        BucketName = cfg.Bucket, Key = largeKey, UploadId = uploadId
                    });
                // Delete both unique synthetic keys even if an earlier PUT
                // timed out after being committed remotely. S3 deletion of a
                // non-existent object is idempotent and never targets user media.
                await client.DeleteObjectAsync(
                    new DeleteObjectRequest { BucketName = cfg.Bucket, Key = smallKey });
                await client.DeleteObjectAsync(
                    new DeleteObjectRequest { BucketName = cfg.Bucket, Key = largeKey });
                result.CleanupSucceeded = true;
            }
            catch (Exception cleanupError)
            {
                result.CleanupSucceeded = false;
                result.CleanupError = SafeError(cleanupError);
                result.Passed = false;
            }
        }
        return result;
    }

    private static string SafeError(Exception ex) =>
        ex is AmazonS3Exception s3
            ? $"AmazonS3Exception status={(int)s3.StatusCode}; code={s3.ErrorCode ?? "unspecified"}"
            : ex.GetType().Name; // never print exception messages or secrets
}

public sealed class CloudComparisonReport
{
    public DateTimeOffset EvaluatedUtc { get; set; }
    public string Scenario { get; set; } = "";
    public bool RealProvidersTested { get; set; }
    public bool ProductionApproved { get; set; }
    public bool PersonalMediaAccessed { get; set; }
    public string? SetupError { get; set; }
    public List<ProviderResult> Results { get; } = new();
}

public sealed class ProviderResult
{
    public string Provider { get; set; } = "";
    public bool Passed { get; set; }
    public bool CleanupSucceeded { get; set; }
    public string? Error { get; set; }
    public string? CleanupError { get; set; }
    public string? SmallObjectSha256 { get; set; }
    public long SmallPutMs { get; set; }
    public long SmallGetMs { get; set; }
    public long MultipartMs { get; set; }
    public long MultipartBytes { get; set; }
    public long RangeBytes { get; set; }
    public List<long> RangeMs { get; } = new();
}
