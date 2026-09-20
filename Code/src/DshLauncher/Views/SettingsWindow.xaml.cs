using System.Windows;
using DshLauncher.ViewModels;

namespace DshLauncher.Views;

public partial class SettingsWindow : HandyControl.Controls.Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
