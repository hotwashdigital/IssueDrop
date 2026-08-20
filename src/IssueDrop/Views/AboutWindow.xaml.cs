using System.Diagnostics;
using System.Reflection;
using System.Windows;
using IssueDrop.Infrastructure;
using MessageBox = System.Windows.MessageBox;

namespace IssueDrop.Views;

public partial class AboutWindow : Window
{
    private const string ProjectUrl = "https://github.com/hotwashdigital/IssueDrop";

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = version is null ? "Version unavailable" : $"Version {version.ToString(3)}";
    }

    private void Project_Click(object sender, RoutedEventArgs e) => Open(ProjectUrl);

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Open(AppPaths.Root);
    }

    private void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Windows could not open that location.\n\n{ex.Message}", "IssueDrop",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
