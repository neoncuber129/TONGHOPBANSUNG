using System.Windows;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class DeleteDataDialog : Window
{
    public DeleteDataDialog()
    {
        InitializeComponent();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeleteDataViewModel vm) return;

        var ok = await vm.DeleteSelectedAsync();
        if (ok)
            DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
