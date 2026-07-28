using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Tonghopbansung.Models;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class ScoreEntryView
{
    private static readonly HashSet<string> EditableInfoHeaders =
    [
        "Họ tên", "Cấp bậc", "Chức vụ", "Đơn vị"
    ];

    private ScoreEntryViewModel? _vm;
    private bool _startEditOnTextInput;
    private string _builtHeaderSignature = string.Empty;

    public ScoreEntryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
        Loaded += (_, _) => HookViewModel();
    }

    private void HookViewModel()
    {
        if (DataContext is not ScoreEntryViewModel vm) return;
        if (ReferenceEquals(_vm, vm)) return;
        _vm = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScoreEntryViewModel.TargetHeaders))
                BuildColumns();
        };
        BuildColumns();
    }

    private void BuildColumns()
    {
        if (_vm is null) return;

        var signature = string.Join("|",
            _vm.TargetHeaders.Select((h, i) =>
            {
                var rounds = i < _vm.TargetRoundCounts.Count ? _vm.TargetRoundCounts[i] : 0;
                var kind = i < _vm.TargetKinds.Count ? _vm.TargetKinds[i] : TargetKind.Scored;
                return $"{h}:{kind}:{rounds}";
            }));
        if (signature == _builtHeaderSignature && MainGrid.Columns.Count > 0)
            return;

        try
        {
            MainGrid.CancelEdit(DataGridEditingUnit.Cell);
            MainGrid.CancelEdit(DataGridEditingUnit.Row);
        }
        catch
        {
            // bỏ qua nếu không đang sửa
        }

        MainGrid.SelectedCells.Clear();
        MainGrid.CurrentCell = new DataGridCellInfo();
        MainGrid.Columns.Clear();
        _builtHeaderSignature = signature;

        var selectCol = new DataGridTemplateColumn
        {
            Header = "Chọn",
            Width = 52,
            CanUserSort = false
        };
        var checkFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkFactory.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding("Shooter.IsSelected")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        checkFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        checkFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        checkFactory.SetValue(UIElement.FocusableProperty, false);
        checkFactory.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(SelectCheckBox_OnPreviewMouseLeftButtonDown));
        checkFactory.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(SelectCheckBox_OnChanged));
        checkFactory.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(SelectCheckBox_OnChanged));
        selectCol.CellTemplate = new DataTemplate { VisualTree = checkFactory };
        MainGrid.Columns.Add(selectCol);

        var textCenter = (Style)FindResource("DataGridTextCenter");
        var editCenter = (Style)FindResource("DataGridEditCenter");
        var numberTextCenter = new Style(typeof(TextBlock), textCenter);
        numberTextCenter.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        numberTextCenter.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        var numberEditCenter = new Style(typeof(TextBox), editCenter);
        numberEditCenter.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Center));
        numberEditCenter.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        var headerCenter = new Style(typeof(DataGridColumnHeader), MainGrid.ColumnHeaderStyle);
        headerCenter.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));

        var sttCol = CreateTextColumn("STT", "Index", 48, numberTextCenter, numberEditCenter, isReadOnly: true);
        sttCol.HeaderStyle = headerCenter;
        MainGrid.Columns.Add(sttCol);
        MainGrid.Columns.Add(CreateTextColumn("Họ tên", "Shooter.Name", 160, textCenter, editCenter));
        MainGrid.Columns.Add(CreateTextColumn("Cấp bậc", "Shooter.Rank", 100, textCenter, editCenter));
        MainGrid.Columns.Add(CreateTextColumn("Chức vụ", "Shooter.Position", 120, textCenter, editCenter));
        MainGrid.Columns.Add(CreateTextColumn("Đơn vị", "Shooter.Unit", 140, textCenter, editCenter));

        var entryCol = new DataGridTemplateColumn { Header = "Nhập", Width = 72, IsReadOnly = true, CanUserSort = false };
        var factory = new FrameworkElementFactory(typeof(Button));
        factory.SetValue(Button.ContentProperty, "Nhập");
        factory.SetValue(Button.PaddingProperty, new Thickness(8, 3, 8, 3));
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetBinding(Button.CommandProperty,
            new Binding("DataContext.OpenEntryCommand")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScoreEntryView), 1)
            });
        factory.SetBinding(Button.CommandParameterProperty, new Binding());
        entryCol.CellTemplate = new DataTemplate { VisualTree = factory };
        MainGrid.Columns.Add(entryCol);

        for (var i = 0; i < _vm.TargetHeaders.Count; i++)
        {
            var header = _vm.TargetHeaders[i];
            var rounds = i < _vm.TargetRoundCounts.Count ? _vm.TargetRoundCounts[i] : 1;
            var kind = i < _vm.TargetKinds.Count ? _vm.TargetKinds[i] : TargetKind.Scored;
            var width = EstimateTargetColumnWidth(header, kind, rounds);

            MainGrid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"TargetCells[{i}].ScoresText"),
                Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
                MinWidth = width,
                IsReadOnly = true,
                CanUserSort = false,
                FontFamily = new FontFamily("Consolas"),
                ElementStyle = textCenter
            });
        }

        var totalCol = CreateTextColumn("Tổng", "Total", 56, numberTextCenter, numberEditCenter, isReadOnly: true);
        totalCol.HeaderStyle = headerCenter;
        MainGrid.Columns.Add(totalCol);
        var knockDownCol = CreateTextColumn("Bia đổ", "KnockDownCount", 64, numberTextCenter, numberEditCenter, isReadOnly: true);
        knockDownCol.HeaderStyle = headerCenter;
        MainGrid.Columns.Add(knockDownCol);
        MainGrid.Columns.Add(CreateTextColumn("Xếp loại", "Classification", 90, textCenter, editCenter, isReadOnly: true));
        MainGrid.Columns.Add(CreateTextColumn("Tiến độ", "ProgressText", 72, textCenter, editCenter, isReadOnly: true));
    }

    private static DataGridTextColumn CreateTextColumn(
        string header,
        string bindingPath,
        double width,
        Style textCenter,
        Style editCenter,
        bool isReadOnly = false)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath)
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
            IsReadOnly = isReadOnly,
            CanUserSort = false,
            ElementStyle = textCenter,
            EditingElementStyle = editCenter
        };
    }

    /// <summary>Độ rộng đủ hiện toàn bộ điểm viên đạn (vd. 10,9,8,7,6) và tên cột.</summary>
    private static double EstimateTargetColumnWidth(string header, TargetKind kind, int rounds)
    {
        const double charWidth = 8.5;
        const double pad = 28;

        double contentWidth;
        if (kind == TargetKind.KnockDown)
        {
            contentWidth = "Không".Length * charWidth + pad;
        }
        else
        {
            var r = Math.Max(1, rounds);
            // Trường hợp rộng nhất: toàn "10" cách nhau bằng dấu phẩy
            var chars = r * 2 + Math.Max(0, r - 1);
            contentWidth = chars * charWidth + pad;
        }

        var headerWidth = Math.Max(header.Length, 3) * 8.0 + pad;
        return Math.Max(Math.Max(contentWidth, headerWidth), 72);
    }

    private void SelectCheckBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        // Một lần bấm: đảo trạng thái ngay, không để DataGrid nuốt click đầu
        cb.IsChecked = cb.IsChecked != true;
        e.Handled = true;
        if (DataContext is ScoreEntryViewModel vm)
            vm.PersistEdits();
    }

    private void SelectCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ScoreEntryViewModel vm)
            vm.PersistEdits();
    }

    private void MainGrid_OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ScoreEntryViewModel vm) return;
        if (MainGrid.CurrentItem is ShooterRowViewModel row)
            vm.SelectedRow = row;
    }

    private void MainGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MainGrid.IsReadOnly) return;
        if (DataContext is not ScoreEntryViewModel vm) return;

        // Đang sửa ô → để DataGrid xử lý
        if (MainGrid.CurrentCell.IsValid && IsEditing())
            return;

        // Ctrl+V dán như Excel
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var header = MainGrid.CurrentColumn?.Header as string;
            if (header is not null && EditableInfoHeaders.Contains(header))
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var startIndex = GetCurrentRowIndex();
                    if (startIndex < 0) startIndex = 0;
                    vm.PasteNamesFromClipboard(text, startIndex);
                    e.Handled = true;
                    return;
                }
            }
        }

        // Delete / Backspace: xóa nội dung ô đang chọn (không xóa cả dòng)
        if (e.Key is Key.Delete or Key.Back)
        {
            if (ClearSelectedEditableCells())
            {
                vm.PersistEdits();
                e.Handled = true;
            }
            return;
        }

        // F2 / Enter: vào chế độ sửa ô hiện tại
        if (e.Key is Key.F2 or Key.Return)
        {
            if (CanEditCurrentCell())
            {
                MainGrid.BeginEdit();
                e.Handled = true;
            }
        }
    }

    /// <summary>Gõ chữ trực tiếp vào ô (giống Excel) → bắt đầu sửa và thay nội dung.</summary>
    private void MainGrid_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsEditing()) return;
        if (!CanEditCurrentCell()) return;
        if (string.IsNullOrEmpty(e.Text)) return;
        if (MainGrid.CurrentItem is null || MainGrid.CurrentColumn is null) return;
        if (!MainGrid.CurrentCell.IsValid) return;

        _startEditOnTextInput = true;
        try
        {
            MainGrid.BeginEdit();
        }
        catch
        {
            _startEditOnTextInput = false;
        }
    }

    private void MainGrid_OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is TextBox tb)
        {
            tb.VerticalAlignment = VerticalAlignment.Stretch;
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            tb.HorizontalAlignment = HorizontalAlignment.Stretch;
            tb.BorderThickness = new Thickness(0);
            tb.Background = Brushes.Transparent;
            tb.Padding = new Thickness(6, 4, 6, 4);
            tb.Margin = new Thickness(0);

            if (_startEditOnTextInput)
            {
                _startEditOnTextInput = false;
                tb.SelectAll();
            }

            tb.Focus();
        }
    }

    private void MainGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is ScoreEntryViewModel vm)
            vm.PersistEdits();
    }

    private void MainGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Header = string.Empty;
    }

    private void MainGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ScoreEntryViewModel vm) return;

        var header = FindColumnHeader(e.OriginalSource as DependencyObject);
        if (header?.Column?.Header is not string columnName) return;
        if (!vm.CanSortColumn(columnName)) return;

        e.Handled = true;

        var menu = new ContextMenu();
        var asc = new MenuItem { Header = "Sắp xếp A → Z" };
        asc.Click += (_, _) => vm.SortRows(columnName, ascending: true);
        var desc = new MenuItem { Header = "Sắp xếp Z → A" };
        desc.Click += (_, _) => vm.SortRows(columnName, ascending: false);
        menu.Items.Add(asc);
        menu.Items.Add(desc);
        menu.PlacementTarget = header;
        menu.IsOpen = true;
    }

    private static DataGridColumnHeader? FindColumnHeader(DependencyObject? source)
    {
        while (source is not null and not DataGridColumnHeader)
            source = VisualTreeHelper.GetParent(source);
        return source as DataGridColumnHeader;
    }

    private int GetCurrentRowIndex()
    {
        if (MainGrid.CurrentItem is null) return -1;
        return MainGrid.Items.IndexOf(MainGrid.CurrentItem);
    }

    private bool CanEditCurrentCell()
    {
        var col = MainGrid.CurrentColumn;
        if (col is null || col.IsReadOnly) return false;
        if (MainGrid.CurrentItem is null) return false;
        if (col.DisplayIndex < 0) return false;
        return col.Header is string h && EditableInfoHeaders.Contains(h);
    }

    private bool IsEditing()
    {
        if (!MainGrid.CurrentCell.IsValid) return false;
        if (MainGrid.CurrentItem is null || MainGrid.CurrentColumn is null) return false;
        if (MainGrid.CurrentColumn.DisplayIndex < 0) return false;

        try
        {
            return MainGrid.CurrentColumn.GetCellContent(MainGrid.CurrentItem) is TextBox { IsKeyboardFocusWithin: true }
                   || Keyboard.FocusedElement is TextBox;
        }
        catch
        {
            return Keyboard.FocusedElement is TextBox;
        }
    }

    private bool ClearSelectedEditableCells()
    {
        var cleared = false;
        var cells = MainGrid.SelectedCells.Count > 0
            ? MainGrid.SelectedCells.ToList()
            : MainGrid.CurrentCell.IsValid
                ? [MainGrid.CurrentCell]
                : [];

        foreach (var cellInfo in cells)
        {
            if (cellInfo.Column is null || cellInfo.Column.IsReadOnly) continue;
            if (cellInfo.Column.Header is not string header || !EditableInfoHeaders.Contains(header))
                continue;
            if (cellInfo.Item is not ShooterRowViewModel row) continue;

            switch (header)
            {
                case "Họ tên":
                    row.Shooter.Name = string.Empty;
                    cleared = true;
                    break;
                case "Cấp bậc":
                    row.Shooter.Rank = string.Empty;
                    cleared = true;
                    break;
                case "Chức vụ":
                    row.Shooter.Position = string.Empty;
                    cleared = true;
                    break;
                case "Đơn vị":
                    row.Shooter.Unit = string.Empty;
                    cleared = true;
                    break;
            }
        }

        return cleared;
    }
}
