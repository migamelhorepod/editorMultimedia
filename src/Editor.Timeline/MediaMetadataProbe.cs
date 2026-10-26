using System.Buffers.Binary;

namespace Editor.Timeline;

/// <summary>Metadata read from bounded container/header structures, without decoding media.</summary>
public sealed record MediaMetadata(long? DurationTicks = null,
    int? PixelWidth = null, int? PixelHeight = null);

public static class MediaMetadataProbe
{
    // Do not traverse arbitrarily long malformed header chains.
    private const int MaxWaveChunks = 128;
    private const long MaxWaveHeaderScan = 4L * 1024 * 1024;

    public static MediaMetadata Read(string path, MediaKind kind)
    {
        // A damaged or unsupported file is still allowed into the library,
        // but no real metadata is claimed for it.
        try
        {
            if (kind is not (MediaKind.Audio or MediaKind.Image or MediaKind.Video))
                return new MediaMetadata();
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            var extension = Path.GetExtension(path);
            var native = kind switch
            {
                MediaKind.Audio when extension.Equals(".wav",
                    StringComparison.OrdinalIgnoreCase) => ReadWave(file),
                MediaKind.Video when extension.Equals(".mp4",
                    StringComparison.OrdinalIgnoreCase) => ReadIsoMovie(file, requireFtyp: true),
                MediaKind.Video when extension.Equals(".mov",
                    StringComparison.OrdinalIgnoreCase) => ReadIsoMovie(file, requireFtyp: false),
                MediaKind.Image when extension.Equals(".png",
                    StringComparison.OrdinalIgnoreCase) => ReadPng(file),
                _ => new MediaMetadata()
            };
            if (kind == MediaKind.Image || native.DurationTicks is not null)
                return native;
            // Compressed/other temporal formats: measure, do not guess.
            return native with { DurationTicks = FfprobeDuration.Read(path) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or EndOfStreamException or
            System.Security.SecurityException)
        {
            return new MediaMetadata();
        }
    }

    private static MediaMetadata ReadPng(Stream file)
    {
        // PNG signature (8) + IHDR length/type (8) + width/height (8).
        Span<byte> header = stackalloc byte[24];
        if (!ReadExactly(file, header)) return new MediaMetadata();
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13 ||
            !header[12..16].SequenceEqual("IHDR"u8))
            return new MediaMetadata();
        var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width is < 1 or > 100000 || height is < 1 or > 100000)
            return new MediaMetadata();
        return new MediaMetadata(PixelWidth: (int)width, PixelHeight: (int)height);
    }

    private static MediaMetadata ReadWave(Stream file)
    {
        // RIFF/WAVE, little endian; this parser supports PCM/IEEE float only.
        Span<byte> riff = stackalloc byte[12];
        if (!ReadExactly(file, riff) || !riff[..4].SequenceEqual("RIFF"u8) ||
            !riff[8..12].SequenceEqual("WAVE"u8))
            return new MediaMetadata();

        var riffEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(riff[4..8]);
        if (riffEnd > file.Length || riffEnd < 12)
            return new MediaMetadata();

        uint bytesPerSecond = 0;
        ushort blockAlign = 0;
        uint dataBytes = 0;
        var foundFmt = false;
        var foundData = false;

        Span<byte> chunk = stackalloc byte[8];
        Span<byte> fmt = stackalloc byte[16];
        for (var i = 0; i < MaxWaveChunks && file.Position + 8 <= riffEnd &&
            file.Position < MaxWaveHeaderScan; i++)
        {
            if (!ReadExactly(file, chunk)) return new MediaMetadata();
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8]);
            var end = file.Position + size;
            if (end > riffEnd) return new MediaMetadata();

            if (chunk[..4].SequenceEqual("fmt "u8) && !foundFmt)
            {
                if (size < 16) return new MediaMetadata();
                if (!ReadExactly(file, fmt)) return new MediaMetadata();
                var format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[..2]);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..4]);
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..8]);
                var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..12]);
                var align = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..14]);
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..16]);
                if (format is not (1 or 3) || channels is < 1 or > 32 ||
                    rate is < 1 or > 768000 || align == 0 ||
                    bits is not (8 or 16 or 24 or 32 or 64) ||
                    (uint)channels * (uint)(bits / 8) != align ||
                    (ulong)rate * align != byteRate)
                    return new MediaMetadata();
                foundFmt = true;
                bytesPerSecond = byteRate;
                blockAlign = align;
            }
            else if (chunk[..4].SequenceEqual("data"u8) && !foundData)
            {
                dataBytes = size;
                foundData = true;
            }

            // Skip media payload; never decode, allocate or copy the data chunk.
            file.Position = end + (size & 1u);
            if (file.Position > riffEnd) return new MediaMetadata();
            if (foundFmt && foundData) break;
        }

        if (!foundFmt || !foundData || dataBytes == 0 ||
            dataBytes % blockAlign != 0) return new MediaMetadata();
        var ticks = (long)decimal.Truncate(
            (decimal)dataBytes * TimeSpan.TicksPerSecond / bytesPerSecond);
        if (ticks is <= 0 || ticks > TimeSpan.FromDays(7).Ticks)
            return new MediaMetadata();
        return new MediaMetadata(DurationTicks: ticks);
    }

    // ISO BMFF / QuickTime: parse top-level boxes and the moov/mvhd movie duration.
    // The potentially very large mdat box is skipped with Seek, never read or copied.
    // Some fragmented/malformed/unsupported files have no valid movie duration:
    // for them we report unknown instead of silently pretending the video lasts 5s.
    private const int MaxIsoTopLevelBoxes = 1024;
    private const int MaxIsoMovieBoxes = 2048;
    private const long MaxMoovHeaderScan = 16L * 1024 * 1024;
    private static readonly long MaxMeasuredDurationTicks = TimeSpan.FromDays(7).Ticks;

    private static MediaMetadata ReadIsoMovie(Stream file, bool requireFtyp)
    {
        var fileEnd = file.Length;
        var foundFtyp = false;
        for (var index = 0; index < MaxIsoTopLevelBoxes && file.Position < fileEnd; index++)
        {
            if (!TryReadIsoBox(file, fileEnd, out var boxType, out var boxEnd,
                out var payloadLength)) return new MediaMetadata();
            if (boxType == IsoType("ftyp"u8) && payloadLength >= 8)
                foundFtyp = true;
            else if (boxType == IsoType("moov"u8))
            {
                if (requireFtyp && !foundFtyp) return new MediaMetadata();
                return ReadMovieHeader(file, boxEnd);
            }

            // Skip the entire data payload, including gigabyte-sized mdat.
            file.Position = boxEnd;
        }
        return new MediaMetadata();
    }

    private static MediaMetadata ReadMovieHeader(Stream file, long moovEnd)
    {
        var start = file.Position;
        for (var i = 0; i < MaxIsoMovieBoxes &&
            file.Position < moovEnd && file.Position - start <= MaxMoovHeaderScan; i++)
        {
            if (!TryReadIsoBox(file, moovEnd, out var kind, out var end,
                out var payloadLength)) return new MediaMetadata();

            if (kind == IsoType("mvhd"u8))
                return ReadMovieDuration(file, payloadLength);

            // Track metadata may be large; skip it rather than inspecting codec data.
            file.Position = end;
        }
        return new MediaMetadata();
    }

    private static MediaMetadata ReadMovieDuration(Stream file, long payloadLength)
    {
        // mvhd v0: flags(4), created(4), modified(4), timescale(4), duration(4).
        // mvhd v1: flags(4), created(8), modified(8), timescale(4), duration(8).
        Span<byte> head = stackalloc byte[32];
        if (payloadLength < 4 || !ReadExactly(file, head[..4]))
            return new MediaMetadata();
        var version = head[0];
        var expectedLength = version switch { 0 => 20, 1 => 32, _ => 0 };
        if (expectedLength == 0 || payloadLength < expectedLength ||
            !ReadExactly(file, head.Slice(4, expectedLength - 4)))
            return new MediaMetadata();

        var timescale = version == 0
            ? BinaryPrimitives.ReadUInt32BigEndian(head[12..16])
            : BinaryPrimitives.ReadUInt32BigEndian(head[20..24]);
        var duration = version == 0
            ? (ulong)BinaryPrimitives.ReadUInt32BigEndian(head[16..20])
            : BinaryPrimitives.ReadUInt64BigEndian(head[24..32]);
        if (timescale == 0 || duration == 0 ||
            (version == 0 && duration == uint.MaxValue) ||
            (version == 1 && duration == ulong.MaxValue))
            return new MediaMetadata();

        // Decimal preserves the 100ns project clock and prevents integer overflow.
        var ticks = (decimal)duration * TimeSpan.TicksPerSecond / timescale;
        if (ticks <= 0 || ticks > MaxMeasuredDurationTicks) return new MediaMetadata();
        return new MediaMetadata(DurationTicks: (long)decimal.Round(ticks,
            0, MidpointRounding.AwayFromZero));
    }

    private static uint IsoType(ReadOnlySpan<byte> fourCc) =>
        BinaryPrimitives.ReadUInt32BigEndian(fourCc);

    private static bool TryReadIsoBox(Stream file, long parentEnd, out uint boxType,
        out long boxEnd, out long payloadLength)
    {
        boxType = 0;
        boxEnd = 0;
        payloadLength = 0;
        var beginning = file.Position;
        if (parentEnd - beginning < 8) return false;

        Span<byte> header = stackalloc byte[16];
        if (!ReadExactly(file, header[..8])) return false;
        var shortSize = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        boxType = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);

        ulong totalLength = shortSize;
        ulong headerLength = 8;
        if (shortSize == 1)
        {
            if (parentEnd - beginning < 16 || !ReadExactly(file, header[8..16]))
                return false;
            totalLength = BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
            headerLength = 16;
        }
        else if (shortSize == 0)
            totalLength = (ulong)(parentEnd - beginning);

        if (totalLength < headerLength || totalLength > (ulong)(parentEnd - beginning))
            return false;
        boxEnd = beginning + (long)totalLength;
        payloadLength = (long)(totalLength - headerLength);
        return true;
    }

    private static bool ReadExactly(Stream file, Span<byte> data)
    {
        while (!data.IsEmpty)
        {
            var read = file.Read(data);
            if (read == 0) return false;
            data = data[read..];
        }
        return true;
    }
}
