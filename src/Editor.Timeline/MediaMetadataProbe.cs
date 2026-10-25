using System.Buffers.Binary;

namespace Editor.Timeline;

/// <summary>Metadata read from bounded headers, not from decoding audiovisual content.</summary>
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
            if (kind != MediaKind.Audio && kind != MediaKind.Image)
                return new MediaMetadata();
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            return kind switch
            {
                MediaKind.Audio when Path.GetExtension(path).Equals(".wav",
                    StringComparison.OrdinalIgnoreCase) => ReadWave(file),
                MediaKind.Image when Path.GetExtension(path).Equals(".png",
                    StringComparison.OrdinalIgnoreCase) => ReadPng(file),
                _ => new MediaMetadata()
            };
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

        for (var i = 0; i < MaxWaveChunks && file.Position + 8 <= riffEnd &&
            file.Position < MaxWaveHeaderScan; i++)
        {
            Span<byte> chunk = stackalloc byte[8];
            if (!ReadExactly(file, chunk)) return new MediaMetadata();
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8]);
            var end = file.Position + size;
            if (end > riffEnd) return new MediaMetadata();

            if (chunk[..4].SequenceEqual("fmt "u8) && !foundFmt)
            {
                if (size < 16) return new MediaMetadata();
                Span<byte> fmt = stackalloc byte[16];
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
