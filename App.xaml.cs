using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Tonghopbansung;

public partial class App : Application
{
    private static bool _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is ViewModels.MainViewModel main)
            main.Session.FlushDeferredPersist();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowError(e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ShowError(ex);
        // Không gọi Shutdown — cố giữ app chạy nếu còn được.
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ShowError(e.Exception.GetBaseException());
    }

    private static void ShowError(Exception ex)
    {
        // Tránh MessageBox chồng chéo nếu nhiều lỗi liên tiếp
        if (_showingError) return;

        try
        {
            _showingError = true;
            var message = BuildMessage(ex);

            void Show()
            {
                MessageBox.Show(
                    message,
                    "Đã xảy ra lỗi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            var app = Current;
            if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                dispatcher.Invoke(Show);
            else
                Show();
        }
        catch
        {
            // Không để chính hộp thoại lỗi làm crash lần nữa
        }
        finally
        {
            _showingError = false;
        }
    }

    private static string BuildMessage(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ứng dụng gặp lỗi nhưng sẽ tiếp tục chạy.");
        sb.AppendLine();
        sb.AppendLine(ex.Message);

        var inner = ex.InnerException;
        while (inner is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Chi tiết: {inner.Message}");
            inner = inner.InnerException;
        }

        return sb.ToString();
    }
}
