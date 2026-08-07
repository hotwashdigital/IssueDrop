using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IssueDrop.Models;

public enum ThemePreference { System, Light, Dark }
public enum DraftState { Active, Submitted, Failed }

public sealed class AppSettings
{
    public const string DefaultHotkey = "Alt+Space";
    public const string LegacyDefaultHotkey = "Shift+Space";

    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public string Hotkey { get; set; } = DefaultHotkey;
    public bool LaunchAtStartup { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 180;
    public string? LastRepository { get; set; }
    public List<string> PinnedRepositories { get; set; } = [];
}

public sealed class IssueDraft : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _body = string.Empty;
    private string? _repository;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Body { get => _body; set => Set(ref _body, value); }
    public string? Repository { get => _repository; set => Set(ref _repository, value); }
    public string? IssueType { get; set; }
    public List<string> Labels { get; set; } = [];
    public List<string> Assignees { get; set; } = [];
    public string? Milestone { get; set; }
    public List<string> Projects { get; set; } = [];
    public string? ParentIssue { get; set; }
    public List<AttachmentItem> Attachments { get; set; } = [];
    public DraftState State { get; set; } = DraftState.Active;
    public string? IssueUrl { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? SubmittedAt { get; set; }

    [JsonIgnore] public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled issue" : Title.Trim();
    [JsonIgnore] public string DisplayRepository => Repository ?? "No repository";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class AttachmentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FileName { get; set; }
    public required string LocalPath { get; set; }
    public long Size { get; set; }
    public string? ContentType { get; set; }
    public bool IsImage { get; set; }
    public string? UploadedUrl { get; set; }
    [JsonIgnore] public string SizeText => Size < 1024 * 1024 ? $"{Math.Max(1, Size / 1024)} KB" : $"{Size / 1024d / 1024d:0.0} MB";
}

public sealed class RepositoryInfo
{
    public required string NameWithOwner { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsPrivate { get; init; }
    public bool IsArchived { get; init; }
    public bool CanPush { get; init; }
    public DateTimeOffset? PushedAt { get; init; }
    public bool IsPinned { get; set; }
    public override string ToString() => NameWithOwner;
}

public sealed record LabelInfo(string Name, string Color, string Description);
public sealed record UserInfo(string Login, string AvatarUrl);
public sealed record MilestoneInfo(int Number, string Title);
public sealed record ProjectInfo(string Title, int Number);
public sealed record IssueTypeInfo(string Name, string Description, string Color);
public sealed record IssueTemplateInfo(
    string Name,
    string FileName,
    string About,
    string TitlePrefix,
    List<string> Labels,
    List<string> Assignees,
    string Body,
    bool IsIssueForm);

public sealed class RepositoryMetadata
{
    public List<LabelInfo> Labels { get; init; } = [];
    public List<UserInfo> Assignees { get; init; } = [];
    public List<MilestoneInfo> Milestones { get; init; } = [];
    public List<ProjectInfo> Projects { get; init; } = [];
    public List<IssueTypeInfo> IssueTypes { get; init; } = [];
    public List<IssueTemplateInfo> Templates { get; init; } = [];
}

public sealed record SubmissionResult(bool Success, string? Url, string? Error);
