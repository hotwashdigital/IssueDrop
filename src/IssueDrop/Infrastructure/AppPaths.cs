namespace IssueDrop.Infrastructure;

public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("ISSUEDROP_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
#if DEBUG
        return Path.Combine(Path.GetTempPath(), "IssueDrop-Debug");
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IssueDrop");
#endif
    }
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string DraftsFile => Path.Combine(Root, "drafts.json");
    public static string Attachments => Path.Combine(Root, "attachments");
    public static string CacheFile => Path.Combine(Root, "repository-cache.json");
    public static string LogFile => Path.Combine(Root, "issuedrop.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Attachments);
    }
}
