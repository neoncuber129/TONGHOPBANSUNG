using System.Windows;

namespace Tonghopbansung.Views;

public partial class ClassificationReportDialog : Window
{
    public ClassificationReportDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
