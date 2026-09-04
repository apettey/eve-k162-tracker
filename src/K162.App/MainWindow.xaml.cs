using System.Windows;
using K162.App.Services;
using K162.App.ViewModels;
using K162.App.Views;

namespace K162.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.AlertRaised += () => WindowFlash.Flash(this);
        vm.SettingsRequested += OpenSettings;
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow { Owner = this, DataContext = _vm };
        dialog.ShowDialog();
        _vm.OnSettingsSaved();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
