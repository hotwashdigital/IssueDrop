using IssueDrop.Infrastructure;
using IssueDrop.Models;
using IssueDrop.Services;
using System.Text.RegularExpressions;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Default hotkey parses and round-trips", () =>
    {
        Assert(HotkeyGesture.TryParse(AppSettings.DefaultHotkey, out var gesture), "Default shortcut should parse");
        Assert(gesture.ToString() == "Alt+Space", "Shortcut should round-trip");
        Assert(!HotkeyGesture.TryParse("Space", out _), "Unmodified global keys should be rejected");
        return Task.CompletedTask;
    }),
    ("Legacy default hotkey migrates without changing custom shortcuts", () =>
    {
        var legacy = new AppSettings { Hotkey = AppSettings.LegacyDefaultHotkey };
        Assert(SettingsService.UpgradeLegacyDefaults(legacy), "The legacy default should be recognized");
        Assert(legacy.Hotkey == AppSettings.DefaultHotkey, "The legacy shortcut should migrate to Alt+Space");
        var custom = new AppSettings { Hotkey = "Ctrl+Shift+I" };
        Assert(!SettingsService.UpgradeLegacyDefaults(custom) && custom.Hotkey == "Ctrl+Shift+I",
            "A customized shortcut must never be overwritten by migration");
        return Task.CompletedTask;
    }),
    ("Quick-token query is conservative", () =>
    {
        var query = QuickTokenService.FindQuery("Fix ordinary@email then #bu", 27);
        Assert(query?.Token == "#bu", "Only the final explicit token should be recognized");
        Assert(QuickTokenService.FindQuery("plain title", 11) is null, "Plain titles should not create a query");
        return Task.CompletedTask;
    }),
    ("Quick-token suggestions map repository fields", () =>
    {
        var metadata = new RepositoryMetadata
        {
            Labels = [new LabelInfo("bug", "ff0000", ""), new LabelInfo("priority: high", "ff8800", "")],
            Assignees = [new UserInfo("octocat", "")],
            Milestones = [new MilestoneInfo(1, "Version 1")],
            IssueTypes = [new IssueTypeInfo("Task", "", "GRAY")]
        };
        var label = QuickTokenService.Suggest(new QuickTokenQuery(0, 2, "#b"), [], metadata);
        Assert(label.Count == 1 && label[0].Value == "bug", "Label search should filter values");
        var person = QuickTokenService.Suggest(new QuickTokenQuery(0, 2, "@o"), [], metadata);
        Assert(person.Count == 1 && person[0].Kind == QuickTokenKind.Assignee, "Assignee token should map users");
        var priority = QuickTokenService.Suggest(new QuickTokenQuery(0, 1, "!"), [], metadata);
        Assert(priority[0].Value == "priority: high", "Priority-like labels should sort first");
        return Task.CompletedTask;
    }),
    ("Draft model starts safe and active", () =>
    {
        var draft = new IssueDraft();
        Assert(draft.State == DraftState.Active, "New drafts must be active");
        Assert(draft.Attachments.Count == 0 && draft.Labels.Count == 0, "New drafts must not inherit mutable collections globally");
        return Task.CompletedTask;
    }),
    ("JSON file writes atomically and reads back", async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "IssueDrop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "value.json");
        try
        {
            var expected = new AppSettings { Hotkey = "Ctrl+Shift+I", HistoryRetentionDays = 90 };
            await JsonFile.WriteAsync(path, expected);
            var actual = await JsonFile.ReadAsync(path, new AppSettings());
            Assert(actual.Hotkey == expected.Hotkey && actual.HistoryRetentionDays == 90, "JSON should round-trip");
            Assert(!File.Exists(path + ".tmp"), "Atomic temp file should not remain");
        }
        finally { Directory.Delete(directory, true); }
    }),
    ("Attachment service copies supported images into draft storage", async () =>
    {
        var source = Path.Combine(Path.GetTempPath(), $"issuedrop-source-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(source, [137, 80, 78, 71, 13, 10, 26, 10]);
        var service = new AttachmentService();
        try
        {
            var items = await service.AddFilesAsync(Guid.NewGuid(), [source]);
            Assert(items.Count == 1 && items[0].IsImage, "PNG should be copied and recognized as an image");
            Assert(File.Exists(items[0].LocalPath) && items[0].LocalPath != source, "Draft must own a durable local copy");
            service.Remove(items[0]);
            Assert(!File.Exists(items[0].LocalPath), "Removing an attachment should remove its draft copy");
        }
        finally { File.Delete(source); }
    }),
    ("Theme dictionaries expose the same complete semantic contract", () =>
    {
        var themes = Path.Combine(FindRepositoryRoot(), "src", "IssueDrop", "Themes");
        var keyPattern = new Regex("x:Key=\"(?<key>[^\"]+)\"", RegexOptions.CultureInvariant);
        var dark = keyPattern.Matches(File.ReadAllText(Path.Combine(themes, "Dark.xaml")))
            .Select(match => match.Groups["key"].Value).Order().ToArray();
        var light = keyPattern.Matches(File.ReadAllText(Path.Combine(themes, "Light.xaml")))
            .Select(match => match.Groups["key"].Value).Order().ToArray();
        Assert(dark.SequenceEqual(light), "Light and dark themes must define exactly the same token keys");
        var required = new[]
        {
            "WindowBackgroundBrush", "SurfaceBrush", "InputBackgroundBrush", "TextPrimaryBrush",
            "TextSecondaryBrush", "TextOnAccentBrush", "BorderBrush", "AccentBrush", "DangerBrush",
            "SuccessBrush", "OverlayBrush", "SelectionBrush", "CodeBackgroundBrush", "ShadowColor"
        };
        Assert(required.All(dark.Contains), "Theme dictionaries are missing required semantic tokens");
        return Task.CompletedTask;
    }),
    ("Application XAML contains no literal palette colors", () =>
    {
        var source = Path.Combine(FindRepositoryRoot(), "src", "IssueDrop");
        var files = Directory.GetFiles(source, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        var literal = new Regex(
            "#[0-9a-fA-F]{3,8}\\b|(?:Background|Foreground|BorderBrush|CaretBrush|SelectionBrush|SelectionTextBrush|Color)=\"(?:Transparent|Black|White|Gray|Grey|Red|Blue|Green)\"",
            RegexOptions.CultureInvariant);
        var violations = files.SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, line, number: index + 1)))
            .Where(item => literal.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(source, item.path)}:{item.number}")
            .ToArray();
        Assert(violations.Length == 0, $"Literal UI colors found outside themes: {string.Join(", ", violations)}");
        return Task.CompletedTask;
    }),
    ("Composer persistence is explicit while pickers dismiss on focus loss", () =>
    {
        var views = Path.Combine(FindRepositoryRoot(), "src", "IssueDrop", "Views");
        var mainXaml = File.ReadAllText(Path.Combine(views, "MainWindow.xaml"));
        var mainCode = File.ReadAllText(Path.Combine(views, "MainWindow.xaml.cs"));
        var pickerXaml = File.ReadAllText(Path.Combine(views, "PickerWindow.xaml"));
        var pickerCode = File.ReadAllText(Path.Combine(views, "PickerWindow.xaml.cs"));
        Assert(!mainXaml.Contains("Deactivated=", StringComparison.Ordinal), "The composer must not dismiss when another app receives focus");
        Assert(!mainCode.Contains("Composer auto-dismissed", StringComparison.Ordinal), "Legacy focus-loss dismissal code must stay removed");
        Assert(mainCode.Contains("e.Key == Key.Escape", StringComparison.Ordinal) && mainCode.Contains("Hide();", StringComparison.Ordinal),
            "Escape must remain the explicit unfinished-entry dismissal path");
        Assert(pickerXaml.Contains("Deactivated=\"Window_Deactivated\"", StringComparison.Ordinal) &&
               pickerCode.Contains("DismissedByDeactivation", StringComparison.Ordinal),
            "Transient field pickers must still close independently when focus moves elsewhere");
        return Task.CompletedTask;
    }),
    ("Checkboxes use the semantic text and state palette", () =>
    {
        var appXaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "IssueDrop", "App.xaml"));
        var styleStart = appXaml.IndexOf("<Style TargetType=\"CheckBox\">", StringComparison.Ordinal);
        var styleEnd = appXaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal);
        Assert(styleStart >= 0 && styleEnd > styleStart, "A dedicated CheckBox style is required for programmatic picker items");
        var style = appXaml[styleStart..styleEnd];
        Assert(style.Contains("TextPrimaryBrush", StringComparison.Ordinal) &&
               style.Contains("TextOnAccentBrush", StringComparison.Ordinal) &&
               style.Contains("AccentBrush", StringComparison.Ordinal),
            "Checkbox text, check glyph, and checked state must all use semantic resources");
        return Task.CompletedTask;
    }),
    ("Description is visible by default while advanced fields stay collapsed", () =>
    {
        var mainXaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "IssueDrop", "Views", "MainWindow.xaml"));
        var bodyStart = mainXaml.IndexOf("<Grid x:Name=\"BodySection\"", StringComparison.Ordinal);
        var bodyEnd = mainXaml.IndexOf('>', bodyStart);
        var moreStart = mainXaml.IndexOf("<Border x:Name=\"MorePanel\"", StringComparison.Ordinal);
        var moreEnd = mainXaml.IndexOf('>', moreStart);
        Assert(bodyStart >= 0 && bodyEnd > bodyStart &&
               !mainXaml[bodyStart..bodyEnd].Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal),
            "The description area must be present in the initial quick composer");
        Assert(moreStart >= 0 && moreEnd > moreStart &&
               mainXaml[moreStart..moreEnd].Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal),
            "Advanced fields should remain hidden until the More button is used");
        return Task.CompletedTask;
    }),
    ("Repository pinning refreshes the open picker before persistence", () =>
    {
        var views = Path.Combine(FindRepositoryRoot(), "src", "IssueDrop", "Views");
        var mainCode = File.ReadAllText(Path.Combine(views, "MainWindow.xaml.cs"));
        var pickerCode = File.ReadAllText(Path.Combine(views, "PickerWindow.xaml.cs"));
        var pinHandler = mainCode.IndexOf("picker.PinRequested +=", StringComparison.Ordinal);
        var liveRefresh = mainCode.IndexOf("picker.ReplaceItems(CreateRepositoryPickerItems(), repo);", pinHandler, StringComparison.Ordinal);
        var persistence = mainCode.IndexOf("await _settings.SaveAsync();", pinHandler, StringComparison.Ordinal);
        Assert(pinHandler >= 0 && liveRefresh > pinHandler && persistence > liveRefresh,
            "Pinning must rebuild and reorder the visible repository picker before waiting for disk persistence");
        Assert(pickerCode.Contains("public void ReplaceItems", StringComparison.Ordinal) &&
               pickerCode.Contains("selectedValue", StringComparison.Ordinal) &&
               pickerCode.Contains("★ Unpin", StringComparison.Ordinal),
            "The picker refresh must preserve selection and immediately reflect the new pin state");
        return Task.CompletedTask;
    })
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS  {name}"); }
    catch (Exception ex) { failures.Add($"FAIL  {name}: {ex.Message}"); Console.WriteLine(failures[^1]); }
}

Console.WriteLine($"{tests.Count - failures.Count}/{tests.Count} tests passed");
return failures.Count == 0 ? 0 : 1;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "IssueDrop.sln"))) return directory.FullName;
    }
    throw new DirectoryNotFoundException("Could not locate the IssueDrop repository root.");
}
