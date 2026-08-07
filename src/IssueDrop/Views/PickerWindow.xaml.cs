using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace IssueDrop.Views;

public sealed class PickerItem
{
    public required string Display { get; init; }
    public required object Value { get; init; }
    public bool Selected { get; init; }
    public bool IsPinned { get; init; }
}

public partial class PickerWindow : Window
{
    private readonly List<PickerItem> _all;
    private readonly bool _multiSelect;
    private readonly bool _allowPin;
    private readonly HashSet<object> _selected;
    private TaskCompletionSource<bool>? _completion;
    private bool _accepted;
    private bool _closing;

    public IReadOnlyCollection<object> SelectedValues => _selected;
    public object? ChosenValue { get; private set; }
    public bool DismissedByDeactivation { get; private set; }
    public event EventHandler<object>? PinRequested;

    public PickerWindow(string title, IEnumerable<PickerItem> items, bool multiSelect = false, bool allowPin = false)
    {
        InitializeComponent();
        Title = title;
        SearchBox.ToolTip = $"Search {title.ToLowerInvariant()}";
        _all = items.ToList();
        _multiSelect = multiSelect;
        _allowPin = allowPin;
        _selected = _all.Where(x => x.Selected).Select(x => x.Value).ToHashSet();
        PinButton.Visibility = allowPin ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
        Loaded += (_, _) => { SearchBox.Focus(); Keyboard.Focus(SearchBox); };
    }

    public Task<bool> ShowPickerAsync()
    {
        if (_completion is not null) throw new InvalidOperationException("This picker has already been shown.");
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => _completion.TrySetResult(_accepted);
        Show();
        return _completion.Task;
    }

    public void ReplaceItems(IEnumerable<PickerItem> items, object? selectedValue = null)
    {
        _all.Clear();
        _all.AddRange(items);
        Refresh(selectedValue);
    }

    private void Refresh(object? selectedValue = null)
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = _all.Where(x => string.IsNullOrWhiteSpace(query) || x.Display.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (_multiSelect)
        {
            ItemsList.ItemTemplate = null;
            ItemsList.Items.Clear();
            foreach (var item in filtered)
            {
                var checkBox = new System.Windows.Controls.CheckBox { Content = item.Display, IsChecked = _selected.Contains(item.Value), Tag = item, Padding = new Thickness(4) };
                checkBox.Click += (_, _) => { if (checkBox.IsChecked == true) _selected.Add(item.Value); else _selected.Remove(item.Value); };
                ItemsList.Items.Add(checkBox);
            }
        }
        else
        {
            var valueToKeep = selectedValue ?? (ItemsList.SelectedItem as PickerItem)?.Value;
            ItemsList.ItemsSource = new ObservableCollection<PickerItem>(filtered);
            var selectedIndex = valueToKeep is null ? -1 : filtered.FindIndex(x => Equals(x.Value, valueToKeep));
            ItemsList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : filtered.Count > 0 ? 0 : -1;
        }
        UpdatePinButton();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();
    private void ItemsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdatePinButton();
    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ChooseCurrent();
    private void ItemsList_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ChooseCurrent(); e.Handled = true; } }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { ClosePicker(_multiSelect); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement == SearchBox && !_multiSelect) { ChooseCurrent(); e.Handled = true; }
        else if (e.Key == Key.Enter && _multiSelect) { ClosePicker(true); e.Handled = true; }
        else if (e.Key == Key.Down && Keyboard.FocusedElement == SearchBox && ItemsList.Items.Count > 0) { ItemsList.SelectedIndex = 0; ItemsList.Focus(); }
    }

    private void ChooseCurrent()
    {
        if (_multiSelect) { ClosePicker(true); return; }
        if (ItemsList.SelectedItem is not PickerItem selected) return;
        ChosenValue = selected.Value;
        ClosePicker(true);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is PickerItem selected) PinRequested?.Invoke(this, selected.Value);
    }

    private void UpdatePinButton()
    {
        if (!_allowPin) return;
        var selected = ItemsList.SelectedItem as PickerItem;
        PinButton.IsEnabled = selected is not null;
        PinButton.Content = selected?.IsPinned == true ? "★ Unpin" : "☆ Pin";
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!IsVisible || _closing) return;
        DismissedByDeactivation = true;
        ClosePicker(_multiSelect);
    }

    private void ClosePicker(bool accepted)
    {
        if (_closing) return;
        _closing = true;
        _accepted = accepted;
        Close();
    }
}
