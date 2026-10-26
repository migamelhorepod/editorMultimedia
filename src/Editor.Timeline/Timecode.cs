namespace Editor.Timeline;

/// <summary>Presentation-only time formatter. Engine and persistence always keep 100ns ticks.</summary>
public static class Timecode
{
    /// <summary>HH:MM:SS with optional comma and up to three millisecond digits.</summary>
    public static string Format(MediaTime time)
    {
        var negative = time.Ticks < 0;
        var millis = (long)decimal.Round(decimal.Abs((decimal)time.Ticks) /
            TimeSpan.TicksPerMillisecond, 0, MidpointRounding.AwayFromZero);
        var seconds = millis / 1000;
        var hours = seconds / 3600;
        var minutes = seconds / 60 % 60;
        var sec = seconds % 60;
        var fraction = millis % 1000;
        var suffix = fraction == 0 ? "" : "," + fraction.ToString("D3").TrimEnd('0');
        return $"{(negative ? "-" : "")}{hours:00}:{minutes:00}:{sec:00}{suffix}";
    }
}
