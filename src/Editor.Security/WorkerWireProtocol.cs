using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Editor.Security;

/// <summary>
/// Strict length-prefixed JSON frames: no stream-wide ReadToEnd,
/// no remote paths and no unbounded allocation.
///
/// A frame carries authorization metadata only. No media bytes are processed.
/// </summary>
public static class WorkerWireProtocol
{
    public const int MaxFrameBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static async Task WriteAsync<T>(
        Stream stream, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(value);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (json.Length == 0 || json.Length > MaxFrameBytes)
            throw new InvalidDataException("IPC frame length exceeds the permitted size.");
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, json.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (size <= 0 || size > MaxFrameBytes)
            throw new InvalidDataException("IPC frame length is invalid.");
        byte[] json = new byte[size];
        await stream.ReadExactlyAsync(json, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException("Empty IPC message.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid or unknown IPC fields.", ex);
        }
    }
}
