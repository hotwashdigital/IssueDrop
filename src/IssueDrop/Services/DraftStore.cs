using IssueDrop.Infrastructure;
using IssueDrop.Models;

namespace IssueDrop.Services;

public sealed class DraftStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<IssueDraft> _drafts = [];

    public IReadOnlyList<IssueDraft> All => _drafts;

    public async Task LoadAsync(int retentionDays)
    {
        AppPaths.EnsureCreated();
        _drafts = await JsonFile.ReadAsync(AppPaths.DraftsFile, new List<IssueDraft>());
        var cutoff = DateTimeOffset.Now.AddDays(-Math.Max(1, retentionDays));
        _drafts.RemoveAll(d => d.State == DraftState.Submitted && d.SubmittedAt < cutoff);
        await SaveAllAsync();
    }

    public IssueDraft GetLatestActive() =>
        _drafts.Where(d => d.State is DraftState.Active or DraftState.Failed)
            .OrderByDescending(d => d.UpdatedAt)
            .FirstOrDefault() ?? new IssueDraft();

    public async Task SaveAsync(IssueDraft draft)
    {
        draft.UpdatedAt = DateTimeOffset.Now;
        var index = _drafts.FindIndex(d => d.Id == draft.Id);
        if (index < 0) _drafts.Add(draft);
        else _drafts[index] = draft;
        await SaveAllAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var draft = _drafts.FirstOrDefault(d => d.Id == id);
        if (draft is null) return;
        _drafts.Remove(draft);
        var attachmentDirectory = Path.Combine(AppPaths.Attachments, id.ToString("N"));
        if (Directory.Exists(attachmentDirectory)) Directory.Delete(attachmentDirectory, true);
        await SaveAllAsync();
    }

    public IEnumerable<IssueDraft> Search(string? query)
    {
        var ordered = _drafts.OrderByDescending(d => d.UpdatedAt);
        if (string.IsNullOrWhiteSpace(query)) return ordered;
        return ordered.Where(d =>
            d.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            d.Body.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (d.Repository?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private async Task SaveAllAsync()
    {
        await _gate.WaitAsync();
        try { await JsonFile.WriteAsync(AppPaths.DraftsFile, _drafts); }
        finally { _gate.Release(); }
    }
}
