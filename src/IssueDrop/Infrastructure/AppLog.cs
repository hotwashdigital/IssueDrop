namespace IssueDrop.Infrastructure;

public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Length > 1_000_000)
                {
                    var lines = File.ReadLines(AppPaths.LogFile).TakeLast(1000).ToArray();
                    File.WriteAllLines(AppPaths.LogFile, lines);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
