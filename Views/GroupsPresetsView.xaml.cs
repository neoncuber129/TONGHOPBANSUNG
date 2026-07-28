using System.Windows.Controls;
using System.Windows.Input;
using Tonghopbansung.ViewModels;

namespace Tonghopbansung.Views;

public partial class GroupsPresetsView
{
    public GroupsPresetsView()
    {
        InitializeComponent();
    }

    private void GroupList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GroupsPresetsViewModel vm)
            vm.EditGroupPresetCommand.Execute(null);
    }
}
