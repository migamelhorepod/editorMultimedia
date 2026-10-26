using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Text.Json;

namespace Editor.Timeline;

/// <summary>
/// Optional isolated-by-process metadata invocation (NOT an OS sandbox).
/// The binary must be installed and trusted by the user; no command shell,
/// media decoding in-process, project-supplied binary path or network request.
/// </summary>
public static class FfprobeDuration
{
    public const int TimeoutMilliseconds = 12000;

    public static long? ParseDurationSeconds(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || output.Length > 128)
            return null;
        if (!decimal.TryParse(output.Trim(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0 || seconds > 7m * 24 * 3600)
            return null;
        var exactTicks = seconds * TimeSpan.TicksPerSecond;
        var ticks = (long)decimal.Round(exactTicks, 0, MidpointRounding.AwayFromZero);
        return ticks > 0 ? ticks : null;
    }

    public static long? ParseProbeJson(string? json, MediaKind expectedKind)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) return null;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (!root.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array) return null;
            var expected = expectedKind == MediaKind.Audio ? "audio" :
                expectedKind == MediaKind.Video ? "video" : "";
            if (expected.Length == 0) return null;

            long? streamTicks = null;
            var matched = false;
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out var type) ||
                    type.GetString() != expected) continue;
                matched = true;
                if (stream.TryGetProperty("duration", out var duration))
                    streamTicks = ParseDurationSeconds(duration.ToString());
            }
            if (!matched) return null;
            if (root.TryGetProperty("format", out var format) &&
                format.TryGetProperty("duration", out var declared))
            {
                var formatTicks = ParseDurationSeconds(declared.ToString());
                if (formatTicks is not null) return formatTicks;
            }
            return streamTicks;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public static long? Read(string chosenLocalFile, MediaKind expectedKind)
    {
        // Users can configure an absolute executable path in their local machine.
        // This path is never read from a saved project or media file.
        var configured = Environment.GetEnvironmentVariable("EDITOR_FFPROBE_PATH");
        var executable = !string.IsNullOrWhiteSpace(configured) &&
            Path.IsPathFullyQualified(configured) ? configured : "ffprobe";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false
                }
            };
            foreach (var arg in new[] {
                "-hide_banner", "-v", "quiet",
                "-select_streams", expectedKind == MediaKind.Audio ? "a:0" : "v:0",
                "-show_entries", "format=duration:stream=codec_type,duration",
                "-of", "json",
                "-i", chosenLocalFile
            })
                process.StartInfo.ArgumentList.Add(arg);
            if (!process.Start()) return null;
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return null;
            }
            if (process.ExitCode != 0) return null;
            var text = process.StandardOutput.ReadToEnd();
            return ParseProbeJson(text, expectedKind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            Win32Exception or InvalidOperationException or ArgumentException or
            System.Security.SecurityException)
        {
            return null;
        }
    }
}
