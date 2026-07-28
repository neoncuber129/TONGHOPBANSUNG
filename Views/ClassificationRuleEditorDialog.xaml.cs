using System.Windows;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class ClassificationRuleEditorDialog : Window
{
    public ClassificationRuleEditorDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ClassificationRuleEditorViewModel vm)
        {
            DialogResult = false;
            return;
        }

        if (!vm.Apply(out var error))
        {
            MessageBox.Show(error, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
