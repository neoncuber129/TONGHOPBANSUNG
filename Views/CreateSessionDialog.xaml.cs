using System.Windows;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class CreateSessionDialog : Window
{
    public CreateSessionDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateSessionViewModel vm)
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
