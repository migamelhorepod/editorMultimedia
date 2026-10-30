namespace Editor.Timeline.Tests;

/// <summary>
/// Wait for the Windows kernel to finish releasing AppContainer/FFmpeg inherited
/// file handles after process exit. A delayed close is not a test failure;
/// permanent outstanding handles still fail after the bounded retry period.
/// </summary>
internal static class WindowsMediaFixtureCleanup
{
    internal static void Remove(string folder)
    {
        for (int attempt = 0; attempt < 25; attempt++)
        {
            if (!Directory.Exists(folder)) return;
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 24)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 24)
            {
                Thread.Sleep(200);
            }
        }
    }
}
