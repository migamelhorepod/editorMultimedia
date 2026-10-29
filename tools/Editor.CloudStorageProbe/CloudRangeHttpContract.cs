using System.Net;
using System.Net.Http.Headers;

namespace Editor.CloudStorageProbe;

/// <summary>
/// HTTP-level S3 Range contract used by the offline conformance suite.
/// Never accepts HTTP 200 fallback: a missing Range could download a 10GiB
/// object and exhaust the user's HDD. This is a research helper, not yet
/// wired into production storage or AppContainer IPC.
/// </summary>
public static class CloudRangeHttpContract
{
    public static async Task<byte[]> FetchVerifiedAsync(
        HttpClient client, Uri url, byte[] original,
        long offset, int length, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(original);
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("HTTPS is mandatory for cloud ranges.");
        var (start, end) = CloudRangeRules.CheckedRange(
            offset, length, original.LongLength);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end);
            try
            {
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.StatusCode is HttpStatusCode.ServiceUnavailable or
                    HttpStatusCode.TooManyRequests)
                {
                    if (attempt == 2)
                        throw new HttpRequestException(
                            "Cloud range failed after bounded retries.");
                    await Task.Delay(20 * (attempt + 1), cancellationToken);
                    continue;
                }
                // A 200 response indicates the provider ignored Range.
                // Fail CLOSED instead of reading an unbounded object body.
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    throw new InvalidDataException(
                        "Cloud GET must return HTTP 206 for a bounded range.");
                ContentRangeHeaderValue? actualRange = response.Content.Headers.ContentRange;
                if (actualRange?.From != start || actualRange.To != end ||
                    actualRange.Length != original.LongLength ||
                    response.Content.Headers.ContentLength != length)
                    throw new InvalidDataException(
                        "Cloud Content-Range/Length does not match authorized bytes.");
                // Read no more than expected + 1. A lying server cannot
                // stream an entire multi-GB object into client memory.
                await using Stream body = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                var buffer = new byte[length + 1];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = await body.ReadAsync(
                        buffer.AsMemory(read), cancellationToken);
                    if (n == 0) break;
                    read += n;
                }
                if (read != length)
                    throw new InvalidDataException(
                        "Cloud range response was truncated or oversized.");
                byte[] exact = buffer.AsSpan(0, length).ToArray();
                if (!CloudRangeRules.MatchesFixture(exact, original, offset, length))
                    throw new InvalidDataException(
                        "Cloud range bytes failed integrity comparison.");
                return exact;
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(20 * (attempt + 1), cancellationToken);
            }
        }
        throw new HttpRequestException("Cloud range retries exhausted.");
    }
}
