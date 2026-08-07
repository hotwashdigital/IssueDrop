using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IssueDrop.Infrastructure;
using IssueDrop.Models;

namespace IssueDrop.Services;

public sealed partial class GitHubService
{
    private const string Gh = "gh";
    private const string AssetBranch = "issuedrop-assets";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(bool Available, string Message)> CheckAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        var version = await ProcessRunner.RunAsync(Gh, ["--version"], cancellationToken: cancellationToken);
        if (!version.Success) return (false, "GitHub CLI (gh) is not installed or is not on PATH.");
        var auth = await ProcessRunner.RunAsync(Gh, ["auth", "status", "--hostname", "github.com"], cancellationToken: cancellationToken);
        return auth.Success
            ? (true, "Connected to GitHub")
            : (false, "Run `gh auth login` once, then refresh IssueDrop.");
    }

    public async Task<string?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(Gh, ["api", "user", "--jq", ".login"], cancellationToken: cancellationToken);
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cache = await JsonFile.ReadAsync(AppPaths.CacheFile, new RepositoryCache());
        if (!forceRefresh && cache.Repositories.Count > 0 && cache.UpdatedAt > DateTimeOffset.Now.AddMinutes(-15))
            return cache.Repositories;

        var result = await ProcessRunner.RunAsync(Gh,
            ["api", "--paginate", "--slurp", "--method", "GET", "user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member"],
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            if (cache.Repositories.Count > 0) return cache.Repositories;
            throw new InvalidOperationException(CleanError(result.Error));
        }

        using var document = JsonDocument.Parse(result.Output);
        var repositories = new List<RepositoryInfo>();
        foreach (var page in document.RootElement.EnumerateArray())
        foreach (var node in page.EnumerateArray())
        {
            var permissions = node.TryGetProperty("permissions", out var permissionNode) &&
                              permissionNode.TryGetProperty("push", out var pushNode) && pushNode.GetBoolean();
            repositories.Add(new RepositoryInfo
            {
                NameWithOwner = node.GetProperty("full_name").GetString()!,
                Description = node.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                IsPrivate = node.TryGetProperty("private", out var privateNode) && privateNode.GetBoolean(),
                IsArchived = node.TryGetProperty("archived", out var archivedNode) && archivedNode.GetBoolean(),
                CanPush = permissions,
                PushedAt = node.TryGetProperty("pushed_at", out var pushed) && pushed.ValueKind == JsonValueKind.String
                    ? pushed.GetDateTimeOffset() : null
            });
        }
        repositories = repositories.Where(r => r.CanPush && !r.IsArchived)
            .DistinctBy(r => r.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(r => r.PushedAt).ToList();
        await JsonFile.WriteAsync(AppPaths.CacheFile, new RepositoryCache { UpdatedAt = DateTimeOffset.Now, Repositories = repositories });
        return repositories;
    }

    public async Task<RepositoryMetadata> GetMetadataAsync(string repository, CancellationToken cancellationToken = default)
    {
        var labelsTask = GetPagedArrayAsync($"repos/{repository}/labels?per_page=100", cancellationToken);
        var assigneesTask = GetPagedArrayAsync($"repos/{repository}/assignees?per_page=100", cancellationToken);
        var milestonesTask = GetPagedArrayAsync($"repos/{repository}/milestones?state=open&per_page=100", cancellationToken);
        var owner = repository.Split('/')[0];
        var projectsTask = GetProjectsAsync(owner, cancellationToken);
        var issueTypesTask = GetIssueTypesAsync(owner, cancellationToken);
        var templatesTask = GetTemplatesAsync(repository, cancellationToken);
        await Task.WhenAll(labelsTask, assigneesTask, milestonesTask, projectsTask, issueTypesTask, templatesTask);

        return new RepositoryMetadata
        {
            Labels = (await labelsTask).Select(x => new LabelInfo(
                x.GetProperty("name").GetString()!,
                x.TryGetProperty("color", out var color) ? color.GetString() ?? "808080" : "808080",
                x.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty)).ToList(),
            Assignees = (await assigneesTask).Select(x => new UserInfo(
                x.GetProperty("login").GetString()!,
                x.TryGetProperty("avatar_url", out var avatar) ? avatar.GetString() ?? string.Empty : string.Empty)).ToList(),
            Milestones = (await milestonesTask).Select(x => new MilestoneInfo(
                x.GetProperty("number").GetInt32(), x.GetProperty("title").GetString()!)).ToList(),
            Projects = await projectsTask,
            IssueTypes = await issueTypesTask,
            Templates = await templatesTask
        };
    }

    public async Task<List<IssueTemplateInfo>> GetTemplatesAsync(string repository, CancellationToken cancellationToken = default)
    {
        var listing = await ProcessRunner.RunAsync(Gh, ["api", $"repos/{repository}/contents/.github/ISSUE_TEMPLATE"], cancellationToken: cancellationToken);
        if (!listing.Success) return [];
        using var document = JsonDocument.Parse(listing.Output);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        var templates = new List<IssueTemplateInfo>();
        foreach (var file in document.RootElement.EnumerateArray())
        {
            var name = file.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? string.Empty : string.Empty;
            var extension = Path.GetExtension(name);
            if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("config.yml", StringComparison.OrdinalIgnoreCase) || name.Equals("config.yaml", StringComparison.OrdinalIgnoreCase)) continue;

            var path = file.GetProperty("path").GetString()!;
            var raw = await ProcessRunner.RunAsync(Gh,
                ["api", "-H", "Accept: application/vnd.github.raw+json", $"repos/{repository}/contents/{path}"], cancellationToken: cancellationToken);
            if (!raw.Success) continue;
            templates.Add(extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                ? ParseMarkdownTemplate(name, raw.Output)
                : ParseIssueForm(name, raw.Output));
        }
        return templates;
    }

    public async Task<SubmissionResult> SubmitAsync(IssueDraft draft, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draft.Repository)) return new(false, null, "Choose a repository.");
        if (string.IsNullOrWhiteSpace(draft.Title)) return new(false, null, "Add an issue title.");

        try
        {
            var body = draft.Body.TrimEnd();
            if (draft.Attachments.Count > 0)
            {
                progress?.Report("Uploading attachments…");
                var markdown = new List<string>();
                foreach (var attachment in draft.Attachments)
                {
                    if (string.IsNullOrWhiteSpace(attachment.UploadedUrl))
                        attachment.UploadedUrl = await UploadAttachmentAsync(draft.Repository, draft.Id, attachment, cancellationToken);
                    var escapedName = attachment.FileName.Replace("[", "\\[").Replace("]", "\\]");
                    markdown.Add(attachment.IsImage
                        ? $"![{escapedName}]({attachment.UploadedUrl})"
                        : $"[{escapedName}]({attachment.UploadedUrl})");
                }
                body = string.Join(Environment.NewLine + Environment.NewLine,
                    new[] { body, "## Attachments", string.Join(Environment.NewLine + Environment.NewLine, markdown) }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            progress?.Report("Creating issue…");
            var bodyFile = Path.Combine(Path.GetTempPath(), $"issuedrop-{draft.Id:N}.md");
            await File.WriteAllTextAsync(bodyFile, body, new UTF8Encoding(false), cancellationToken);
            try
            {
                var args = new List<string> { "issue", "create", "--repo", draft.Repository, "--title", draft.Title.Trim(), "--body-file", bodyFile };
                foreach (var label in draft.Labels) { args.Add("--label"); args.Add(label); }
                foreach (var assignee in draft.Assignees) { args.Add("--assignee"); args.Add(assignee); }
                foreach (var project in draft.Projects) { args.Add("--project"); args.Add(project); }
                if (!string.IsNullOrWhiteSpace(draft.Milestone)) { args.Add("--milestone"); args.Add(draft.Milestone); }
                if (!string.IsNullOrWhiteSpace(draft.IssueType)) { args.Add("--type"); args.Add(draft.IssueType); }
                if (!string.IsNullOrWhiteSpace(draft.ParentIssue)) { args.Add("--parent"); args.Add(draft.ParentIssue); }
                var submissionStarted = DateTimeOffset.UtcNow;
                ProcessResult result;
                using (var commandTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    commandTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                    try { result = await ProcessRunner.RunAsync(Gh, args, cancellationToken: commandTimeout.Token); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var recoveredUrl = await FindRecentlyCreatedIssueAsync(draft.Repository, draft.Title.Trim(), submissionStarted);
                        return recoveredUrl is not null
                            ? new(true, recoveredUrl, null)
                            : new(false, null, "GitHub stopped responding after issue creation. The issue may already exist; check the repository before retrying.");
                    }
                }
                if (!result.Success) return new(false, null, CleanError(result.Error));
                var url = UrlRegex().Match(result.Output).Value;
                return new(true, string.IsNullOrWhiteSpace(url) ? result.Output.Trim() : url, null);
            }
            finally { try { File.Delete(bodyFile); } catch (IOException) { } }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new(false, null, ex.Message);
        }
    }

    private static async Task<string?> FindRecentlyCreatedIssueAsync(string repository, string title, DateTimeOffset startedAt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var result = await ProcessRunner.RunAsync(Gh,
                ["issue", "list", "--repo", repository, "--state", "all", "--author", "@me", "--limit", "20", "--json", "title,url,createdAt"],
                cancellationToken: timeout.Token);
            if (!result.Success) return null;
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.EnumerateArray()
                .Where(x => x.TryGetProperty("title", out var titleNode) && titleNode.GetString()?.Equals(title, StringComparison.Ordinal) == true)
                .Where(x => x.TryGetProperty("createdAt", out var createdNode) && createdNode.GetDateTimeOffset() >= startedAt.AddMinutes(-2))
                .OrderByDescending(x => x.GetProperty("createdAt").GetDateTimeOffset())
                .Select(x => x.GetProperty("url").GetString())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task<string> UploadAttachmentAsync(string repository, Guid draftId, AttachmentItem attachment, CancellationToken cancellationToken)
    {
        await EnsureAssetBranchAsync(repository, cancellationToken);
        var content = Convert.ToBase64String(await File.ReadAllBytesAsync(attachment.LocalPath, cancellationToken));
        var safeName = Uri.EscapeDataString(attachment.FileName.Replace(' ', '-'));
        var path = $".issuedrop/{draftId:N}/{attachment.Id:N}-{safeName}";
        var payload = JsonSerializer.Serialize(new
        {
            message = $"IssueDrop attachment: {attachment.FileName}",
            content,
            branch = AssetBranch
        });
        var result = await ProcessRunner.RunAsync(Gh,
            ["api", "--method", "PUT", $"repos/{repository}/contents/{path}", "--input", "-"], payload, cancellationToken);
        if (!result.Success) throw new InvalidOperationException($"Could not upload {attachment.FileName}: {CleanError(result.Error)}");
        return $"https://github.com/{repository}/raw/{AssetBranch}/{path}";
    }

    private async Task EnsureAssetBranchAsync(string repository, CancellationToken cancellationToken)
    {
        var existing = await ProcessRunner.RunAsync(Gh, ["api", $"repos/{repository}/git/ref/heads/{AssetBranch}"], cancellationToken: cancellationToken);
        if (existing.Success) return;

        var repo = await ProcessRunner.RunAsync(Gh, ["api", $"repos/{repository}", "--jq", ".default_branch"], cancellationToken: cancellationToken);
        if (!repo.Success) throw new InvalidOperationException(CleanError(repo.Error));
        var defaultBranch = repo.Output.Trim();
        var head = await ProcessRunner.RunAsync(Gh, ["api", $"repos/{repository}/git/ref/heads/{defaultBranch}", "--jq", ".object.sha"], cancellationToken: cancellationToken);
        if (!head.Success) throw new InvalidOperationException(CleanError(head.Error));
        var payload = JsonSerializer.Serialize(new { @ref = $"refs/heads/{AssetBranch}", sha = head.Output.Trim() });
        var created = await ProcessRunner.RunAsync(Gh,
            ["api", "--method", "POST", $"repos/{repository}/git/refs", "--input", "-"], payload, cancellationToken);
        if (!created.Success && !created.Error.Contains("Reference already exists", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"IssueDrop needs repository contents permission to upload attachments. {CleanError(created.Error)}");
    }

    private static async Task<List<JsonElement>> GetPagedArrayAsync(string endpoint, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Gh, ["api", "--paginate", "--slurp", endpoint], cancellationToken: cancellationToken);
        if (!result.Success) return [];
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.EnumerateArray().SelectMany(page => page.EnumerateArray()).Select(x => x.Clone()).ToList();
    }

    private static async Task<List<ProjectInfo>> GetProjectsAsync(string owner, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Gh,
            ["project", "list", "--owner", owner, "--limit", "100", "--format", "json"], cancellationToken: cancellationToken);
        if (!result.Success) return [];
        using var document = JsonDocument.Parse(result.Output);
        if (!document.RootElement.TryGetProperty("projects", out var projects)) return [];
        return projects.EnumerateArray()
            .Where(x => !x.TryGetProperty("closed", out var closed) || !closed.GetBoolean())
            .Select(x => new ProjectInfo(x.GetProperty("title").GetString()!, x.GetProperty("number").GetInt32())).ToList();
    }

    private static async Task<List<IssueTypeInfo>> GetIssueTypesAsync(string owner, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Gh, ["api", $"orgs/{owner}/issue-types"], cancellationToken: cancellationToken);
        if (!result.Success) return [];
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        var array = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("issue_types", out var types) ? types : default;
        if (array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(x => new IssueTypeInfo(
            x.GetProperty("name").GetString()!,
            x.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
            x.TryGetProperty("color", out var color) ? color.GetString() ?? "GRAY" : "GRAY")).ToList();
    }

    private static IssueTemplateInfo ParseMarkdownTemplate(string fileName, string content)
    {
        var title = string.Empty;
        var about = string.Empty;
        var name = HumanizeFileName(fileName);
        var labels = new List<string>();
        var assignees = new List<string>();
        var body = content;
        if (content.StartsWith("---", StringComparison.Ordinal))
        {
            var end = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (end >= 0)
            {
                var frontMatter = content[3..end];
                body = content[(end + 3)..].TrimStart('\r', '\n');
                foreach (var line in frontMatter.Replace("\r\n", "\n").Split('\n'))
                {
                    var separator = line.IndexOf(':');
                    if (separator < 0) continue;
                    var key = line[..separator].Trim().ToLowerInvariant();
                    var value = line[(separator + 1)..].Trim().Trim('"', '\'');
                    switch (key)
                    {
                        case "name": name = value; break;
                        case "about": about = value; break;
                        case "title": title = value; break;
                        case "labels": labels = SplitFrontMatterList(value); break;
                        case "assignees": assignees = SplitFrontMatterList(value); break;
                    }
                }
            }
        }
        return new IssueTemplateInfo(name, fileName, about, title, labels, assignees, body, false);
    }

    private static IssueTemplateInfo ParseIssueForm(string fileName, string content)
    {
        string ReadScalar(string key)
        {
            var line = content.Replace("\r\n", "\n").Split('\n')
                .FirstOrDefault(x => x.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
            return line is null ? string.Empty : line[(line.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
        }
        var displayName = ReadScalar("name");
        return new IssueTemplateInfo(
            string.IsNullOrWhiteSpace(displayName) ? HumanizeFileName(fileName) : displayName,
            fileName, ReadScalar("description"), ReadScalar("title"), [], [], string.Empty, true);
    }

    private static List<string> SplitFrontMatterList(string value) => value
        .Trim('[', ']')
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim('"', '\''))
        .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

    private static string HumanizeFileName(string fileName) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ').Replace('_', ' '));

    private static string CleanError(string error)
    {
        var value = error.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "GitHub did not return an error message.";
        return value.Replace("gh: ", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"https://github\.com/[^\s]+/issues/\d+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    private sealed class RepositoryCache
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public List<RepositoryInfo> Repositories { get; set; } = [];
    }
}
