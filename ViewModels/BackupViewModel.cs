using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Tonghopbansung.Views;

namespace Tonghopbansung.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly AppSession _session;

    [ObservableProperty]
    private string _infoText = string.Empty;

    public BackupViewModel(AppSession session)
    {
        _session = session;
        RefreshInfo();
    }

    public void RefreshInfo()
    {
        var dbPath = Path.Combine(_session.DataDirectory, "data.db");
        InfoText = $"CSDL: SQLite\n"
                   + $"File: {dbPath}\n\n"
                   + $"Số nhóm: {_session.Groups.Count}\n"
                   + $"Số đợt bắn: {_session.Sessions.Count}\n"
                   + $"Tổng số người: {_session.Sessions.Sum(s => s.Shooters.Count)}";
    }

    [RelayCommand]
    private async Task Backup()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Sao lưu SQLite (*.thbs)|*.thbs|SQLite (*.db)|*.db",
            FileName = $"Tonghopbansung_{DateTime.Now:yyyyMMdd_HHmm}.thbs",
            DefaultExt = ".thbs"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await _session.ExportBackupAsync(dlg.FileName);
            MessageBox.Show("Sao lưu thành công.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi sao lưu:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task Restore()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Sao lưu Tonghopbansung (*.thbs;*.db;*.json)|*.thbs;*.db;*.json|SQLite (*.thbs;*.db)|*.thbs;*.db|JSON cũ (*.json)|*.json|Tất cả (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        if (MessageBox.Show(
                "Phục hồi sẽ ghi đè toàn bộ dữ liệu hiện tại. Tiếp tục?",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            await _session.ImportBackupAsync(dlg.FileName);
            MessageBox.Show("Phục hồi thành công. Các tab sẽ cập nhật theo dữ liệu mới.", "Thành công",
                MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshInfo();
            if (Application.Current.MainWindow?.DataContext is MainViewModel main)
            {
                main.ScoreEntry.RefreshShooters();
                main.GroupsPresets.NotifyAfterDataChange();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi phục hồi:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteData()
    {
        var vm = new DeleteDataViewModel(_session);
        var dialog = new DeleteDataDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };

        if (dialog.ShowDialog() == true)
        {
            RefreshInfo();
            if (Application.Current.MainWindow?.DataContext is MainViewModel main)
            {
                main.ScoreEntry.RefreshShooters();
                main.GroupsPresets.NotifyAfterDataChange();
            }
            MessageBox.Show("Đã xóa các mục đã chọn.", "Xóa dữ liệu",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(_session.DataDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _session.DataDirectory,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task SaveNow()
    {
        try
        {
            await _session.PersistAsync("Đang lưu dữ liệu...");
            MessageBox.Show("Đã lưu dữ liệu.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi lưu:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
