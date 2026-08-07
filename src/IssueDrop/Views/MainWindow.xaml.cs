using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IssueDrop.Models;
using IssueDrop.Services;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using IssueDrop.Infrastructure;

namespace IssueDrop.Views;

public partial class MainWindow : Window
{
    private readonly GitHubService _github;
    private readonly DraftStore _draftStore;
    private readonly SettingsService _settings;
    private readonly AttachmentService _attachments;
    private readonly Action<string, string, bool> _notify;
    private readonly DispatcherTimer _saveTimer;
    private readonly ObservableCollection<AttachmentItem> _attachmentItems = [];
    private IssueDraft _draft = new();
    private List<RepositoryInfo> _repositories = [];
    private RepositoryMetadata _metadata = new();
    private bool _loading;
    private bool _authenticated;
    private bool _submitting;
    private string? _lastIssueUrl;
    private QuickTokenQuery? _tokenQuery;
    private CancellationTokenSource? _submissionCts;
    private int _submissionGeneration;

    public MainWindow(GitHubService github, DraftStore draftStore, SettingsService settings,
        AttachmentService attachments, Action<string, string, bool> notify)
    {
        InitializeComponent();
#if DEBUG
        ShowInTaskbar = true;
#endif
        _github = github;
        _draftStore = draftStore;
        _settings = settings;
        _attachments = attachments;
        _notify = notify;
        AttachmentList.ItemsSource = _attachmentItems;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            var savingDraftId = _draft.Id;
            await SaveDraftAsync();
            if (_draft.Id == savingDraftId) StatusText.Text = "Draft saved";
        };
        LoadDraft(CreateFreshDraft());
        Loaded += (_, _) => WindowPositioner.CenterOnActiveMonitor(this);
    }

    public async Task InitializeAsync()
    {
        await RefreshAuthenticationAsync();
    }

    public async void ShowFreshComposer()
    {
        await StartFreshDraftAsync();
        ShowCurrentComposer();
    }

    private void ShowCurrentComposer()
    {
        AppLog.Write("ShowComposer requested.");
        SuccessPanel.Visibility = Visibility.Collapsed;
        ComposerPanel.Visibility = Visibility.Visible;
        Show();
        UpdateLayout();
        WindowPositioner.CenterOnActiveMonitor(this);
        Activate();
        Topmost = true;
        TitleBox.Focus();
        Keyboard.Focus(TitleBox);
        TitleBox.CaretIndex = TitleBox.Text.Length;
    }

    public async void EditDraft(IssueDraft draft)
    {
        var current = _draft;
        try
        {
            if (_submitting)
            {
                CancelActiveSubmission();
                current.State = DraftState.Failed;
                current.LastError = "Submission was interrupted. Check GitHub before retrying this draft.";
                await _draftStore.SaveAsync(current);
            }
            else await SaveDraftAsync();
        }
        catch (Exception ex) { AppLog.Write($"Could not save the current draft before opening another: {ex}"); }
        finally
        {
            _submitting = false;
            SetBusy(false);
            LoadDraft(draft);
            ShowCurrentComposer();
        }
    }

    private IssueDraft CreateFreshDraft() => new() { Repository = _settings.Current.LastRepository };

    private async Task StartFreshDraftAsync()
    {
        var previous = _draft;
        try
        {
            if (_submitting)
            {
                CancelActiveSubmission();
                previous.State = DraftState.Failed;
                previous.LastError = "Submission was interrupted. Check GitHub before retrying this draft.";
                await _draftStore.SaveAsync(previous);
            }
            else await SaveDraftAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not save the previous draft while opening a fresh composer: {ex}");
            _notify("Draft could not be saved", "IssueDrop still opened a fresh issue. Check the log before closing the app.", true);
        }
        finally
        {
            _submitting = false;
            SetBusy(false);
            LoadDraft(CreateFreshDraft());
            SuccessPanel.Visibility = Visibility.Collapsed;
            ComposerPanel.Visibility = Visibility.Visible;
        }
    }

    private void CancelActiveSubmission()
    {
        Interlocked.Increment(ref _submissionGeneration);
        _submissionCts?.Cancel();
        _submissionCts = null;
    }

    private void LoadDraft(IssueDraft draft)
    {
        _saveTimer.Stop();
        _loading = true;
        _draft = draft;
        TitleBox.Text = draft.Title;
        BodyBox.Text = draft.Body;
        ParentIssueBox.Text = draft.ParentIssue ?? string.Empty;
        MorePanel.Visibility = HasAdvancedFields(draft) ? Visibility.Visible : Visibility.Collapsed;
        _attachmentItems.Clear();
        foreach (var attachment in draft.Attachments.Where(x => File.Exists(x.LocalPath))) _attachmentItems.Add(attachment);
        UpdateAttachmentVisibility();
        UpdatePlaceholders();
        UpdateFieldButtons();
        UpdateDraftCount();
        _loading = false;
    }

    private async Task RefreshAuthenticationAsync(bool forceRepositories = false)
    {
        SetBusy(true, "Connecting to GitHub…");
        try
        {
            var auth = await _github.CheckAuthenticationAsync();
            _authenticated = auth.Available;
            AuthBanner.Visibility = auth.Available ? Visibility.Collapsed : Visibility.Visible;
            AuthText.Text = auth.Message;
            StatusText.Text = auth.Available ? auth.Message : "GitHub sign-in needed";
            if (!auth.Available) return;
            _repositories = (await _github.GetRepositoriesAsync(forceRepositories)).ToList();
            ApplyRepositoryOrdering();
            var desired = _draft.Repository ?? _settings.Current.LastRepository;
            if (!string.IsNullOrWhiteSpace(desired) && _repositories.Any(r => r.NameWithOwner.Equals(desired, StringComparison.OrdinalIgnoreCase)))
            {
                _draft.Repository = desired;
                await LoadMetadataAsync(desired);
            }
            UpdateFieldButtons();
        }
        catch (Exception ex)
        {
            _authenticated = false;
            AuthBanner.Visibility = Visibility.Visible;
            AuthText.Text = ex.Message;
        }
        finally { SetBusy(false); UpdateSubmitState(); }
    }

    private void ApplyRepositoryOrdering()
    {
        var pins = _settings.Current.PinnedRepositories;
        foreach (var repo in _repositories) repo.IsPinned = pins.Contains(repo.NameWithOwner, StringComparer.OrdinalIgnoreCase);
        _repositories = _repositories
            .OrderByDescending(r => r.IsPinned)
            .ThenByDescending(r => r.NameWithOwner.Equals(_settings.Current.LastRepository, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.PushedAt).ToList();
    }

    private async Task LoadMetadataAsync(string repository)
    {
        SetBusy(true, "Loading repository fields…");
        try
        {
            _metadata = await _github.GetMetadataAsync(repository);
            _draft.Labels = _draft.Labels.Where(label => _metadata.Labels.Any(x => x.Name.Equals(label, StringComparison.OrdinalIgnoreCase))).ToList();
            _draft.Assignees = _draft.Assignees.Where(login => _metadata.Assignees.Any(x => x.Login.Equals(login, StringComparison.OrdinalIgnoreCase))).ToList();
            if (!string.IsNullOrWhiteSpace(_draft.Milestone) && !_metadata.Milestones.Any(x => x.Title.Equals(_draft.Milestone, StringComparison.OrdinalIgnoreCase))) _draft.Milestone = null;
            _draft.Projects = _draft.Projects.Where(project => _metadata.Projects.Any(x => x.Title.Equals(project, StringComparison.OrdinalIgnoreCase))).ToList();
            if (_metadata.IssueTypes.Count > 0 && !string.IsNullOrWhiteSpace(_draft.IssueType) && !_metadata.IssueTypes.Any(x => x.Name.Equals(_draft.IssueType, StringComparison.OrdinalIgnoreCase))) _draft.IssueType = null;
            MilestoneCombo.ItemsSource = new[] { new MilestoneInfo(0, "No milestone") }.Concat(_metadata.Milestones);
            ProjectCombo.ItemsSource = new[] { new ProjectInfo("No project", 0) }.Concat(_metadata.Projects);
            TemplateCombo.ItemsSource = new[] { new IssueTemplateInfo("No template", string.Empty, string.Empty, string.Empty, [], [], string.Empty, false) }.Concat(_metadata.Templates);
            MilestoneCombo.SelectedIndex = Math.Max(0, _metadata.Milestones.FindIndex(x => x.Title == _draft.Milestone) + 1);
            ProjectCombo.SelectedIndex = Math.Max(0, _metadata.Projects.FindIndex(x => _draft.Projects.Contains(x.Title)) + 1);
            TemplateCombo.SelectedIndex = 0;
            UpdateFieldButtons();
            if (HasMeaningfulContent(_draft)) await _draftStore.SaveAsync(_draft);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void RepoButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = CreatePicker("Repositories", CreateRepositoryPickerItems(), allowPin: true);
        picker.PinRequested += async (_, value) =>
        {
            if (value is not RepositoryInfo repo) return;
            var wasPinned = _settings.Current.PinnedRepositories.Contains(repo.NameWithOwner, StringComparer.OrdinalIgnoreCase);
            if (wasPinned)
                _settings.Current.PinnedRepositories.RemoveAll(x => x.Equals(repo.NameWithOwner, StringComparison.OrdinalIgnoreCase));
            else _settings.Current.PinnedRepositories.Add(repo.NameWithOwner);
            ApplyRepositoryOrdering();
            picker.ReplaceItems(CreateRepositoryPickerItems(), repo);
            try
            {
                await _settings.SaveAsync();
            }
            catch (Exception ex)
            {
                _settings.Current.PinnedRepositories.RemoveAll(x => x.Equals(repo.NameWithOwner, StringComparison.OrdinalIgnoreCase));
                if (wasPinned) _settings.Current.PinnedRepositories.Add(repo.NameWithOwner);
                ApplyRepositoryOrdering();
                picker.ReplaceItems(CreateRepositoryPickerItems(), repo);
                StatusText.Text = $"Could not save repository pin: {ex.Message}";
            }
        };
        if (await picker.ShowPickerAsync() && picker.ChosenValue is RepositoryInfo selected)
        {
            _draft.Repository = selected.NameWithOwner;
            _settings.Current.LastRepository = selected.NameWithOwner;
            await _settings.SaveAsync();
            UpdateFieldButtons();
            ScheduleSave();
            await LoadMetadataAsync(selected.NameWithOwner);
        }
        if (!picker.DismissedByDeactivation) Activate();
    }

    private IEnumerable<PickerItem> CreateRepositoryPickerItems() => _repositories.Select(r => new PickerItem
    {
        Display = $"{(r.IsPinned ? "★ " : string.Empty)}{r.NameWithOwner}{(r.IsPrivate ? "  · Private" : string.Empty)}",
        Value = r,
        Selected = r.NameWithOwner.Equals(_draft.Repository, StringComparison.OrdinalIgnoreCase),
        IsPinned = r.IsPinned
    });

    private async void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        var types = new List<PickerItem> { new() { Display = "No issue type", Value = string.Empty, Selected = string.IsNullOrWhiteSpace(_draft.IssueType) } };
        types.AddRange(_metadata.IssueTypes.Select(x => new PickerItem { Display = x.Name, Value = x.Name, Selected = x.Name == _draft.IssueType }));
        var picker = CreatePicker("Issue types", types);
        if (await picker.ShowPickerAsync() && picker.ChosenValue is string selected)
        {
            _draft.IssueType = string.IsNullOrWhiteSpace(selected) ? null : selected;
            UpdateFieldButtons(); ScheduleSave();
        }
        if (!picker.DismissedByDeactivation) Activate();
    }

    private async void LabelsButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _metadata.Labels.Select(x => new PickerItem { Display = x.Name, Value = x.Name, Selected = _draft.Labels.Contains(x.Name) });
        var picker = CreatePicker("Labels", items, multiSelect: true);
        if (await picker.ShowPickerAsync())
        {
            _draft.Labels = picker.SelectedValues.Cast<string>().ToList();
            UpdateFieldButtons(); ScheduleSave();
        }
        if (!picker.DismissedByDeactivation) Activate();
    }

    private async void AssigneeButton_Click(object sender, RoutedEventArgs e)
    {
        var items = _metadata.Assignees.Select(x => new PickerItem { Display = $"@{x.Login}", Value = x.Login, Selected = _draft.Assignees.Contains(x.Login) });
        var picker = CreatePicker("Assignees", items, multiSelect: true);
        if (await picker.ShowPickerAsync())
        {
            _draft.Assignees = picker.SelectedValues.Cast<string>().ToList();
            UpdateFieldButtons(); ScheduleSave();
        }
        if (!picker.DismissedByDeactivation) Activate();
    }

    private PickerWindow CreatePicker(string title, IEnumerable<PickerItem> items, bool multiSelect = false, bool allowPin = false)
    {
        var picker = new PickerWindow(title, items, multiSelect, allowPin)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Left + 24,
            Top = Top + ActualHeight - 18
        };
        return picker;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        MorePanel.Visibility = MorePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (MorePanel.Visibility == Visibility.Visible) ExpandBody();
    }

    private async void DraftsButton_Click(object sender, RoutedEventArgs e)
    {
        var drafts = _draftStore.Search(null)
            .Where(x => x.Id != _draft.Id && (x.State is DraftState.Active or DraftState.Failed))
            .Where(HasMeaningfulContent)
            .ToList();
        if (drafts.Count == 0)
        {
            StatusText.Text = "No saved drafts";
            return;
        }

        var items = drafts.Select(x => new PickerItem
        {
            Display = $"{x.DisplayTitle}  ·  {x.DisplayRepository}  ·  {x.UpdatedAt:g}",
            Value = x,
            Selected = false
        });
        var picker = CreatePicker("Saved drafts", items);
        if (await picker.ShowPickerAsync() && picker.ChosenValue is IssueDraft selected)
        {
            await SaveDraftAsync();
            LoadDraft(selected);
            if (!string.IsNullOrWhiteSpace(selected.Repository)) await LoadMetadataAsync(selected.Repository);
            StatusText.Text = selected.LastError ?? "Draft opened";
        }
        if (!picker.DismissedByDeactivation) Activate();
    }

    private async void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Attach files to the GitHub issue", Filter = "Supported files|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.pdf;*.docx;*.xlsx;*.txt;*.md;*.log;*.json;*.csv;*.zip|All files|*.*" };
        if (dialog.ShowDialog(this) == true) await AddFilesAsync(dialog.FileNames);
    }

    private async Task AddFilesAsync(IEnumerable<string> files)
    {
        try
        {
            foreach (var item in await _attachments.AddFilesAsync(_draft.Id, files)) _attachmentItems.Add(item);
            SyncAttachments(); ExpandBody(); ScheduleSave();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task PasteAttachmentAsync()
    {
        try
        {
            foreach (var item in await _attachments.AddFromClipboardAsync(_draft.Id)) _attachmentItems.Add(item);
            SyncAttachments(); ExpandBody(); ScheduleSave();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AttachmentItem item })
        {
            _attachments.Remove(item); _attachmentItems.Remove(item); SyncAttachments(); ScheduleSave();
        }
    }

    private void SyncAttachments()
    {
        _draft.Attachments = _attachmentItems.ToList();
        UpdateAttachmentVisibility();
    }

    private void UpdateAttachmentVisibility() => AttachmentList.Visibility = _attachmentItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Shell_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private async void Shell_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await AddFilesAsync(files);
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
        if (_loading) return;
        _draft.Title = TitleBox.Text; ScheduleSave(); UpdateSubmitState();
        Dispatcher.BeginInvoke(UpdateTokenSuggestions, DispatcherPriority.Input);
    }

    private void BodyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
        if (_loading) return;
        _draft.Body = BodyBox.Text; ScheduleSave();
    }

    private void ParentIssueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _draft.ParentIssue = string.IsNullOrWhiteSpace(ParentIssueBox.Text) ? null : ParentIssueBox.Text.Trim(); ScheduleSave();
    }

    private void MilestoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MilestoneCombo.SelectedItem is not MilestoneInfo value) return;
        _draft.Milestone = value.Number == 0 ? null : value.Title; ScheduleSave();
    }

    private void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProjectCombo.SelectedItem is not ProjectInfo value) return;
        _draft.Projects = value.Number == 0 ? [] : [value.Title]; ScheduleSave();
    }

    private void TemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TemplateCombo.SelectedItem is not IssueTemplateInfo template || string.IsNullOrWhiteSpace(template.FileName)) return;
        if (template.IsIssueForm)
        {
            if (!string.IsNullOrWhiteSpace(_draft.Repository))
            {
                var url = $"https://github.com/{_draft.Repository}/issues/new?template={Uri.EscapeDataString(template.FileName)}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                StatusText.Text = "Opened GitHub issue form";
            }
            return;
        }
        if (!string.IsNullOrWhiteSpace(template.TitlePrefix) && !TitleBox.Text.StartsWith(template.TitlePrefix, StringComparison.OrdinalIgnoreCase))
            TitleBox.Text = template.TitlePrefix + TitleBox.Text;
        if (!string.IsNullOrWhiteSpace(template.Body))
            BodyBox.Text = string.IsNullOrWhiteSpace(BodyBox.Text) ? template.Body : BodyBox.Text.TrimEnd() + Environment.NewLine + Environment.NewLine + template.Body;
        _draft.Labels = _draft.Labels.Concat(template.Labels).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _draft.Assignees = _draft.Assignees.Concat(template.Assignees).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ExpandBody(); UpdateFieldButtons(); ScheduleSave();
    }

    private void TitleBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExpandBody(); BodyBox.Focus(); BodyBox.CaretIndex = BodyBox.Text.Length; e.Handled = true;
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TokenPopup.IsOpen)
        {
            if (e.Key == Key.Escape) { TokenPopup.IsOpen = false; e.Handled = true; return; }
            if (e.Key == Key.Down) { TokenList.SelectedIndex = Math.Min(TokenList.Items.Count - 1, TokenList.SelectedIndex + 1); e.Handled = true; return; }
            if (e.Key == Key.Up) { TokenList.SelectedIndex = Math.Max(0, TokenList.SelectedIndex - 1); e.Handled = true; return; }
            if (e.Key == Key.Enter && TokenList.SelectedItem is QuickTokenSuggestion suggestion) { await ApplyTokenSuggestionAsync(suggestion); e.Handled = true; return; }
        }
        if (e.Key == Key.Escape) { await SaveDraftAsync(); Hide(); e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { await SubmitAsync(); e.Handled = true; return; }
        if (e.Key == Key.Space && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { ExpandBody(); MorePanel.Visibility = Visibility.Visible; e.Handled = true; return; }
        if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()) { await PasteAttachmentAsync(); e.Handled = true; return; }
            if (TitleBox.IsKeyboardFocusWithin && Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                var lines = text.Replace("\r\n", "\n").Split('\n');
                if (lines.Length > 1)
                {
                    TitleBox.Text = string.IsNullOrWhiteSpace(TitleBox.Text) ? lines[0] : TitleBox.Text + lines[0];
                    BodyBox.Text = string.Join(Environment.NewLine, lines.Skip(1));
                    ExpandBody(); BodyBox.Focus(); e.Handled = true;
                }
            }
        }
    }

    private void UpdateTokenSuggestions()
    {
        if (!TitleBox.IsKeyboardFocusWithin) { TokenPopup.IsOpen = false; return; }
        _tokenQuery = QuickTokenService.FindQuery(TitleBox.Text, TitleBox.CaretIndex);
        if (_tokenQuery is null) { TokenPopup.IsOpen = false; return; }
        var suggestions = QuickTokenService.Suggest(_tokenQuery, _repositories, _metadata);
        TokenList.ItemsSource = suggestions;
        TokenList.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
        TokenPopup.IsOpen = suggestions.Count > 0;
    }

    private async void TokenList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TokenList.SelectedItem is QuickTokenSuggestion suggestion) await ApplyTokenSuggestionAsync(suggestion);
    }

    private async Task ApplyTokenSuggestionAsync(QuickTokenSuggestion suggestion)
    {
        if (_tokenQuery is null) return;
        var query = _tokenQuery;
        var replacement = suggestion.Kind == QuickTokenKind.Command ? suggestion.Value : string.Empty;
        TitleBox.Text = TitleBox.Text.Remove(query.Start, query.Length).Insert(query.Start, replacement);
        TitleBox.CaretIndex = query.Start + replacement.Length;
        TokenPopup.IsOpen = false;
        switch (suggestion.Kind)
        {
            case QuickTokenKind.Label:
                if (!_draft.Labels.Contains(suggestion.Value, StringComparer.OrdinalIgnoreCase)) _draft.Labels.Add(suggestion.Value);
                break;
            case QuickTokenKind.Assignee:
                if (!_draft.Assignees.Contains(suggestion.Value, StringComparer.OrdinalIgnoreCase)) _draft.Assignees.Add(suggestion.Value);
                break;
            case QuickTokenKind.Repository:
                _draft.Repository = suggestion.Value; _settings.Current.LastRepository = suggestion.Value; await _settings.SaveAsync(); await LoadMetadataAsync(suggestion.Value);
                break;
            case QuickTokenKind.Milestone: _draft.Milestone = suggestion.Value; break;
            case QuickTokenKind.IssueType: _draft.IssueType = suggestion.Value; break;
            case QuickTokenKind.Command:
                UpdateTokenSuggestions();
                break;
        }
        UpdateFieldButtons(); ScheduleSave(); TitleBox.Focus();
    }

    private void ExpandBody() => BodySection.Visibility = Visibility.Visible;
    private void UpdatePlaceholders()
    {
        TitlePlaceholder.Visibility = string.IsNullOrEmpty(TitleBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BodyPlaceholder.Visibility = string.IsNullOrEmpty(BodyBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFieldButtons()
    {
        RepoButtonText.Text = string.IsNullOrWhiteSpace(_draft.Repository) ? "Repository" : ShortRepository(_draft.Repository);
        TypeButtonText.Text = string.IsNullOrWhiteSpace(_draft.IssueType) ? "Type" : _draft.IssueType;
        LabelsButtonText.Text = _draft.Labels.Count == 0 ? "Labels" : _draft.Labels.Count == 1 ? _draft.Labels[0] : $"{_draft.Labels.Count} labels";
        AssigneeButtonText.Text = _draft.Assignees.Count == 0 ? "Assignee" : _draft.Assignees.Count == 1 ? _draft.Assignees[0] : $"{_draft.Assignees.Count} people";
        UpdateSubmitState();
    }

    private static string ShortRepository(string value) => value.Contains('/') ? value[(value.IndexOf('/') + 1)..] : value;
    private static bool HasAdvancedFields(IssueDraft draft) => !string.IsNullOrWhiteSpace(draft.Milestone) || draft.Projects.Count > 0 || !string.IsNullOrWhiteSpace(draft.ParentIssue);

    private void ScheduleSave()
    {
        if (_loading) return;
        StatusText.Text = "Saving…";
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private async Task SaveDraftAsync()
    {
        if (_draft.State == DraftState.Submitted) return;
        _draft.Title = TitleBox.Text; _draft.Body = BodyBox.Text; SyncAttachments();
        if (!HasMeaningfulContent(_draft))
        {
            if (_draftStore.All.Any(x => x.Id == _draft.Id)) await _draftStore.DeleteAsync(_draft.Id);
        }
        else await _draftStore.SaveAsync(_draft);
        UpdateDraftCount();
    }

    private static bool HasMeaningfulContent(IssueDraft draft) =>
        !string.IsNullOrWhiteSpace(draft.Title) || !string.IsNullOrWhiteSpace(draft.Body) || draft.Attachments.Count > 0 ||
        draft.Labels.Count > 0 || draft.Assignees.Count > 0 || !string.IsNullOrWhiteSpace(draft.Milestone) ||
        draft.Projects.Count > 0 || !string.IsNullOrWhiteSpace(draft.IssueType) || !string.IsNullOrWhiteSpace(draft.ParentIssue) ||
        draft.State == DraftState.Failed;

    private void UpdateDraftCount()
    {
        var count = _draftStore.All.Count(x =>
            x.Id != _draft.Id &&
            (x.State is DraftState.Active or DraftState.Failed) &&
            HasMeaningfulContent(x));
        DraftsButton.Content = count > 0 ? $"▤ {count}" : "▤";
        DraftsButton.ToolTip = count > 0 ? $"Saved drafts ({count})" : "Saved drafts";
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async Task SubmitAsync()
    {
        if (_submitting) return;
        if (string.IsNullOrWhiteSpace(_draft.Repository)) { StatusText.Text = "Choose a repository"; RepoButton_Click(this, new RoutedEventArgs()); return; }
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { StatusText.Text = "Add a title"; TitleBox.Focus(); return; }

        var submittingDraft = _draft;
        submittingDraft.Title = TitleBox.Text.Trim();
        submittingDraft.Body = BodyBox.Text;
        submittingDraft.State = DraftState.Active;
        submittingDraft.LastError = null;
        SyncAttachments();

        var generation = Interlocked.Increment(ref _submissionGeneration);
        using var submissionCts = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        _submissionCts = submissionCts;
        _submitting = true;
        AppLog.Write($"Submitting draft {submittingDraft.Id:N} to {submittingDraft.Repository}.");
        SetBusy(true, "Creating issue…");
        UpdateSubmitState();
        var progress = new Progress<string>(message =>
        {
            if (generation != _submissionGeneration || _draft.Id != submittingDraft.Id) return;
            BusyText.Text = message;
            StatusText.Text = message;
        });

        SubmissionResult result;
        try
        {
            result = await _github.SubmitAsync(submittingDraft, progress, submissionCts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new SubmissionResult(false, null,
                "Submission was cancelled or timed out. The issue may already exist; check GitHub before retrying.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Unexpected issue submission failure: {ex}");
            result = new SubmissionResult(false, null,
                "IssueDrop hit an unexpected error. Your draft was saved so you can safely retry.");
        }
        finally
        {
            if (ReferenceEquals(_submissionCts, submissionCts)) _submissionCts = null;
            if (generation == _submissionGeneration)
            {
                _submitting = false;
                SetBusy(false);
                UpdateSubmitState();
            }
        }

        if (!result.Success)
        {
            submittingDraft.State = DraftState.Failed;
            submittingDraft.LastError = result.Error;
            await _draftStore.SaveAsync(submittingDraft);
            AppLog.Write($"Submission failed for draft {submittingDraft.Id:N}: {result.Error}");
            if (generation == _submissionGeneration && _draft.Id == submittingDraft.Id)
            {
                StatusText.Text = result.Error;
                _notify("IssueDrop could not submit", result.Error ?? "Unknown GitHub error", true);
                UpdateSubmitState();
            }
            return;
        }

        submittingDraft.State = DraftState.Submitted;
        submittingDraft.IssueUrl = result.Url;
        submittingDraft.SubmittedAt = DateTimeOffset.Now;
        submittingDraft.LastError = null;
        await _draftStore.SaveAsync(submittingDraft);
        AppLog.Write($"Submission completed for draft {submittingDraft.Id:N}: {result.Url}");
        _notify("Issue created", $"{submittingDraft.Title} in {submittingDraft.Repository}", false);
        if (generation != _submissionGeneration || _draft.Id != submittingDraft.Id) return;

        _lastIssueUrl = result.Url;
        SuccessRepositoryText.Text = $"{submittingDraft.Repository} · Link ready";
        ComposerPanel.Visibility = Visibility.Collapsed; SuccessPanel.Visibility = Visibility.Visible;
    }

    private void UpdateSubmitState() => SubmitButton.IsEnabled = _authenticated && !_submitting && !string.IsNullOrWhiteSpace(_draft.Title);
    private void SetBusy(bool busy, string text = "Working…")
    {
        BusyText.Text = text; BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; BusyOverlay.IsHitTestVisible = busy;
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastIssueUrl)) { Clipboard.SetText(_lastIssueUrl); SuccessRepositoryText.Text = "Link copied to clipboard"; }
    }

    private void OpenIssue_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastIssueUrl)) Process.Start(new ProcessStartInfo(_lastIssueUrl) { UseShellExecute = true });
    }

    private async void NewIssue_Click(object sender, RoutedEventArgs e)
    {
        await StartFreshDraftAsync();
        ShowCurrentComposer();
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var preview = new MarkdownPreviewWindow(_draft.Title, BodyBox.Text) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        preview.ShowDialog(); Activate();
    }

    private async void RefreshAuth_Click(object sender, RoutedEventArgs e) => await RefreshAuthenticationAsync(true);
}
