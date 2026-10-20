using System.Windows;
using System.Windows.Controls;
using FCP.App.ViewModels;

namespace FCP.App.Views;

public partial class LaunchPage : UserControl
{
    public LaunchPage() => InitializeComponent();
}

public partial class InstanceSelectPage : UserControl
{
    public InstanceSelectPage() => InitializeComponent();

    // 让当前选中的文件夹显示为已勾选状态
    private void FolderItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: FolderItem folder } item && DataContext is InstanceSelectViewModel vm)
            item.IsChecked = Equals(folder, vm.State.SelectedFolder);
    }
}

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
}

public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage() => InitializeComponent();
}

public partial class InstanceSettingsPage : UserControl
{
    public InstanceSettingsPage() => InitializeComponent();
}

public partial class MessageDialog : UserControl
{
    public MessageDialog() => InitializeComponent();
}

public partial class AccountDialog : UserControl
{
    public AccountDialog()
    {
        InitializeComponent();
        // 打开后直接可以输入玩家名
        Loaded += (_, _) => NameBox.Focus();
    }
}
