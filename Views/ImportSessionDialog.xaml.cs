using System.Windows;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class ImportSessionDialog : Window
{
    public ImportSessionDialog()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportSessionViewModel vm) return;

        SummaryText.Text = $"File: {vm.PackName} · {vm.ShooterCount} người";
        AppendLabel.Text = string.IsNullOrWhiteSpace(vm.ActiveSessionName)
            ? "Nối vào đợt hiện tại"
            : $"Nối vào đợt hiện tại («{vm.ActiveSessionName}»)";

        CreateRadio.IsEnabled = vm.MatchingGroups.Count > 0;
        AppendRadio.IsEnabled = vm.CanAppend;

        if (vm.Mode == ImportSessionMode.Append && vm.CanAppend)
        {
            AppendRadio.IsChecked = true;
            GroupCombo.IsEnabled = false;
        }
        else
        {
            CreateRadio.IsChecked = true;
            GroupCombo.IsEnabled = vm.MatchingGroups.Count > 0;
        }
    }

    private void CreateMode_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImportSessionViewModel vm)
            vm.Mode = ImportSessionMode.CreateNew;
        GroupCombo.IsEnabled = true;
    }

    private void AppendMode_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is ImportSessionViewModel vm)
            vm.Mode = ImportSessionMode.Append;
        GroupCombo.IsEnabled = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportSessionViewModel vm)
        {
            DialogResult = false;
            return;
        }

        if (!vm.Validate(out var error))
        {
            MessageBox.Show(error, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
