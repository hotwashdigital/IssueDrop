using IssueDrop.Models;

namespace IssueDrop.Services;

public enum QuickTokenKind { Label, Assignee, Repository, Milestone, IssueType, Command }
public sealed record QuickTokenSuggestion(string Display, string Value, QuickTokenKind Kind);
public sealed record QuickTokenQuery(int Start, int Length, string Token);

public static class QuickTokenService
{
    public static QuickTokenQuery? FindQuery(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length) return null;
        var start = caretIndex - 1;
        while (start >= 0 && !char.IsWhiteSpace(text[start])) start--;
        start++;
        var token = text[start..caretIndex];
        if (token.Length == 0 || token[0] is not ('#' or '@' or '!' or '/')) return null;
        return new QuickTokenQuery(start, caretIndex - start, token);
    }

    public static IReadOnlyList<QuickTokenSuggestion> Suggest(
        QuickTokenQuery query, IEnumerable<RepositoryInfo> repositories, RepositoryMetadata metadata)
    {
        var token = query.Token;
        if (token.StartsWith("/repo:", StringComparison.OrdinalIgnoreCase))
            return Match(repositories.Select(x => new QuickTokenSuggestion(x.NameWithOwner, x.NameWithOwner, QuickTokenKind.Repository)), token[6..]);
        if (token.StartsWith("/milestone:", StringComparison.OrdinalIgnoreCase))
            return Match(metadata.Milestones.Select(x => new QuickTokenSuggestion(x.Title, x.Title, QuickTokenKind.Milestone)), token[11..]);
        if (token.StartsWith("/type:", StringComparison.OrdinalIgnoreCase))
            return Match(metadata.IssueTypes.Select(x => new QuickTokenSuggestion(x.Name, x.Name, QuickTokenKind.IssueType)), token[6..]);
        if (token.StartsWith('/'))
        {
            var commands = new[]
            {
                new QuickTokenSuggestion("/repo:  Change repository", "/repo:", QuickTokenKind.Command),
                new QuickTokenSuggestion("/milestone:  Set milestone", "/milestone:", QuickTokenKind.Command),
                new QuickTokenSuggestion("/type:  Set issue type", "/type:", QuickTokenKind.Command)
            };
            return Match(commands, token);
        }
        if (token.StartsWith('#'))
            return Match(metadata.Labels.Select(x => new QuickTokenSuggestion($"#{x.Name}", x.Name, QuickTokenKind.Label)), token[1..]);
        if (token.StartsWith('@'))
            return Match(metadata.Assignees.Select(x => new QuickTokenSuggestion($"@{x.Login}", x.Login, QuickTokenKind.Assignee)), token[1..]);
        if (token.StartsWith('!'))
        {
            var priority = metadata.Labels.OrderByDescending(x => IsPriorityName(x.Name));
            return Match(priority.Select(x => new QuickTokenSuggestion($"!{x.Name}", x.Name, QuickTokenKind.Label)), token[1..]);
        }
        return [];
    }

    private static IReadOnlyList<QuickTokenSuggestion> Match(IEnumerable<QuickTokenSuggestion> source, string query) => source
        .Where(x => string.IsNullOrWhiteSpace(query) || x.Display.Contains(query, StringComparison.OrdinalIgnoreCase))
        .Take(8).ToList();

    private static bool IsPriorityName(string name) =>
        new[] { "priority", "urgent", "critical", "high", "medium", "low", "p0", "p1", "p2", "p3" }
            .Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
}
