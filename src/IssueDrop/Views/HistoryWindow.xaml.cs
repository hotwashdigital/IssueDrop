using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IssueDrop.Models;
using IssueDrop.Services;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace IssueDrop.Views;

public partial class HistoryWindow : Window
{
    private readonly DraftStore _store;
    public event EventHandler<IssueDraft>? EditRequested;

    public HistoryWindow(DraftStore store)
    {
        InitializeComponent();
        _store = store;
        Refresh();
    }

    private void Refresh()
    {
        var items = _store.Search(SearchBox?.Text).ToList();
        HistoryList.ItemsSource = items;
        CountText.Text = $"{items.Count} item{(items.Count == 1 ? string.Empty : "s")}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IssueDraft draft }) return;
        Open(draft);
    }

    private void Open(IssueDraft draft)
    {
        if (draft.State == DraftState.Submitted && !string.IsNullOrWhiteSpace(draft.IssueUrl))
            Process.Start(new ProcessStartInfo(draft.IssueUrl) { UseShellExecute = true });
        else { EditRequested?.Invoke(this, draft); Close(); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IssueDraft draft }) return;
        if (MessageBox.Show(this, $"Delete “{draft.DisplayTitle}” and its local attachments?", "Delete IssueDrop item",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _store.DeleteAsync(draft.Id); Refresh();
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is IssueDraft draft) Open(draft);
    }
}
