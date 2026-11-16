using System.Windows.Controls;
using System.Windows;
namespace SharkBabyDrivers.Views;
public partial class R5View : UserControl
{
    public R5View() => InitializeComponent();

    private void ShowSensorHelp(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ToolTip: ToolTip help } button)
        {
            help.PlacementTarget = button;
            help.IsOpen = true;
        }
    }

    private void SelectMouseButton(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index)
            && index >= 0 && index < ButtonAssignments.Items.Count)
        {
            ButtonAssignments.SelectedIndex = index;
            ButtonAssignments.Focus();
        }
    }
}
