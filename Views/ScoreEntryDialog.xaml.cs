using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class ScoreEntryDialog : Window
{
    private const double ScreenMargin = 16;

    public ScoreEntryDialog()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Save_Click(SaveButton, e);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Mở tối đa trong vùng làm việc màn hình; chỉ cuộn khi nội dung vẫn vượt quá.
    /// </summary>
    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        var maxW = Math.Max(MinWidth, work.Width - ScreenMargin * 2);
        var maxH = Math.Max(MinHeight, work.Height - ScreenMargin * 2);

        MaxWidth = maxW;
        MaxHeight = maxH;
        Width = maxW;
        Height = maxH;

        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;

        MainScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        MainScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ScoreEntryDialogViewModel vm)
            vm.ApplyToShooter();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ScoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScoreButtonViewModel scoreBtn } fe)
            return;
        if (DataContext is not ScoreEntryDialogViewModel vm)
            return;

        var round = FindAncestorDataContext<RoundColumnViewModel>(fe);
        if (round is null) return;

        vm.SetScoreCommand.Execute(new ScorePickParameter { Round = round, Score = scoreBtn.Score });
    }

    /// <summary>Lăn chuột = cuộn ngang (Shift+lăn = cuộn dọc).</summary>
    private void MainScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        }
        else
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        }

        e.Handled = true;
    }

    private static T? FindAncestorDataContext<T>(DependencyObject? start) where T : class
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: T match })
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
