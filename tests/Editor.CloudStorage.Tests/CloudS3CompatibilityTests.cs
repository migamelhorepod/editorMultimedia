using System.Net;
using System.Net.Http.Headers;
using Editor.CloudStorageProbe;
using Xunit;

namespace Editor.CloudStorage.Tests;

public sealed class CloudS3CompatibilityTests
{
    private static readonly Uri FakeSignedUrl = new(
        "https://example.test/only-poc-synthetic.bin");

    [Fact]
    public void GivenOracleNamespaceAndRegion_WhenEndpointIsConfigured_ThenUsePathStyleSigV4Service()
    {
        var cfg = CloudProbeSettings.Oracle(
            "axexample", "sa-saopaulo-1", "editor-private",
            "public-identifier", "test-only-not-a-real-secret");
        Assert.Equal("oracle", cfg.Provider);
        Assert.Equal("sa-saopaulo-1", cfg.SigningRegion);
        Assert.Equal("https://axexample.compat.objectstorage.sa-saopaulo-1.oci.customer-oci.com/",
            cfg.Endpoint.ToString());
        Assert.DoesNotContain("test-only-not-a-real-secret", cfg.ToString());
    }

    [Fact]
    public void GivenCloudflareAccount_WhenEndpointIsConfigured_ThenRegionIsAuto()
    {
        var cfg = CloudProbeSettings.R2(
            new string('a', 32), "editor-private",
            "public-identifier", "test-only-not-a-real-secret");
        Assert.Equal("r2", cfg.Provider);
        Assert.Equal("auto", cfg.SigningRegion);
        Assert.Equal("https://" + new string('a', 32) +
            ".r2.cloudflarestorage.com/", cfg.Endpoint.ToString());
    }

    [Theory]
    [InlineData("../wrong")]
    [InlineData("bad_value")]
    [InlineData("http://internal")]
    [InlineData("r2.cloudflarestorage.com")]
    public void GivenMaliciousAccountIdentifier_WhenConfigured_ThenRejectEndpoint(string account)
    {
        Assert.Throws<ArgumentException>(() => CloudProbeSettings.R2(
            account, "editor-private", "key", "fake-secret"));
    }

    [Fact]
    public void GivenEmptyCredentials_WhenConfigured_ThenFailClosed()
    {
        Assert.Throws<ArgumentException>(() => CloudProbeSettings.Oracle(
            "oracle123", "sa-saopaulo-1", "private-bucket", "key", ""));
    }

    [Theory]
    [InlineData(-1, 1, 200L)]
    [InlineData(0, 0, 200L)]
    [InlineData(190, 20, 200L)]
    [InlineData(200, 1, 200L)]
    [InlineData(0, 1048577, 1048577L)]
    public void GivenInvalidByteRange_WhenRequested_ThenNeverDownloadWholeObject(
        long start, int length, long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CloudRangeRules.CheckedRange(start, length, size));
    }

    [Fact]
    public async Task GivenValidRange_WhenHTTP206HasExactBytes_ThenVerifyAgainstOriginal()
    {
        byte[] data = Enumerable.Range(0, 1024).Select(x => (byte)x).ToArray();
        using var handler = new FakeHandler((request, count) =>
        {
            Assert.Equal(1, count);
            Assert.Equal("bytes=400-543", request.Headers.Range?.ToString());
            return Partial(data, 400, 144);
        });
        using var http = new HttpClient(handler);
        byte[] fetched = await CloudRangeHttpContract.FetchVerifiedAsync(
            http, FakeSignedUrl, data, 400, 144);
        Assert.True(CloudRangeRules.MatchesFixture(fetched, data, 400, 144));
    }

    [Fact]
    public async Task GivenServerIgnoresRange_WhenHTTP200_ThenFailWithoutReadingBody()
    {
        byte[] data = new byte[1024];
        using var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl, data, 0, 10));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GivenMismatchedContentRange_WhenHTTP206_ThenReject()
    {
        byte[] data = new byte[512];
        using var handler = new FakeHandler((_, _) =>
        {
            var response = Partial(data, 0, 32);
            response.Content.Headers.ContentRange =
                new ContentRangeHeaderValue(1, 32, data.Length);
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl, data, 0, 32));
    }

    [Fact]
    public async Task GivenTruncatedRange_WhenResponseArrives_ThenRejectWithoutSuccess()
    {
        byte[] data = new byte[512];
        using var handler = new FakeHandler((_, _) =>
        {
            var response = Partial(data, 0, 32);
            response.Content = new ByteArrayContent(data[..16]);
            response.Content.Headers.ContentLength = 32;
            response.Content.Headers.ContentRange =
                new ContentRangeHeaderValue(0, 31, data.Length);
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl, data, 0, 32));
    }

    [Fact]
    public async Task GivenCorruptedBytes_WhenServerReturnsExactSize_ThenRejectIntegrityMismatch()
    {
        byte[] data = Enumerable.Range(0, 512).Select(x => (byte)x).ToArray();
        using var handler = new FakeHandler((_, _) =>
        {
            var response = Partial(data, 0, 32);
            ((ByteArrayContent)response.Content).Dispose();
            byte[] changed = data[..32].ToArray();
            changed[0] ^= 0xff;
            response.Content = new ByteArrayContent(changed);
            response.Content.Headers.ContentRange =
                new ContentRangeHeaderValue(0, 31, data.Length);
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl, data, 0, 32));
    }

    [Fact]
    public async Task GivenTemporary503_WhenRetrySucceeds_ThenReadExactlyOnce()
    {
        byte[] data = Enumerable.Range(0, 100).Select(x => (byte)x).ToArray();
        using var handler = new FakeHandler((_, count) =>
            count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Partial(data, 80, 20));
        using var http = new HttpClient(handler);
        byte[] fetched = await CloudRangeHttpContract.FetchVerifiedAsync(
            http, FakeSignedUrl, data, 80, 20);
        Assert.Equal(20, fetched.Length);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GivenRepeated503_WhenRetryBudgetEnds_ThenFailClosed()
    {
        byte[] data = new byte[100];
        using var handler = new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl, data, 10, 30));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task GivenPermissionDenied_When403_ThenNeverRetryOrDisclose()
    {
        byte[] data = new byte[100];
        using var handler = new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl, data, 10, 30));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GivenCancelledRequest_WhenReadIsRequested_ThenNoNetworkCall()
    {
        byte[] data = new byte[100];
        using var handler = new FakeHandler((_, _) => Partial(data, 0, 10));
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http, FakeSignedUrl,
                data, 0, 10, cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GivenPlainHttpUrl_WhenReadIsRequested_ThenFailBeforeNetwork()
    {
        byte[] data = new byte[100];
        using var handler = new FakeHandler((_, _) => Partial(data, 0, 10));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CloudRangeHttpContract.FetchVerifiedAsync(http,
                new Uri("http://example.test/object"), data, 0, 10));
        Assert.Equal(0, handler.Calls);
    }

    private static HttpResponseMessage Partial(byte[] source, int start, int length)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(source.AsSpan(start, length).ToArray())
        };
        response.Content.Headers.ContentRange =
            new ContentRangeHeaderValue(start, start + length - 1, source.Length);
        return response;
    }

    private sealed class FakeHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(send(request, Calls));
        }
    }
}
