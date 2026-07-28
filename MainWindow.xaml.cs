using System.ComponentModel;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Session.Persist();
    }
}
