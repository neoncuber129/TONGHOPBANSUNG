using System.Windows;
using System.Windows.Input;
using Tonghopbansung.Models;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class PresetEditorDialog : Window
{
    public PresetEditorDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PresetEditorViewModel vm)
            DialogResult = !string.IsNullOrWhiteSpace(vm.Preset.Name);
        else
            DialogResult = true;
    }

    private void Tree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not PresetEditorViewModel vm) return;

        switch (e.NewValue)
        {
            case TargetCluster cluster:
                vm.SelectedCluster = cluster;
                vm.SelectedTarget = cluster.Targets.FirstOrDefault();
                break;
            case TargetDefinition target:
                vm.SelectedTarget = target;
                vm.SelectedCluster = vm.Preset.Clusters.FirstOrDefault(c => c.Targets.Contains(target));
                break;
        }
    }

    private void RulesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PresetEditorViewModel vm)
            vm.EditRuleCommand.Execute(null);
    }
}
