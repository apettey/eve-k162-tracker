using System.Diagnostics;
using System.Windows.Controls;

namespace K162.App.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private void DevPortal_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://developers.eveonline.com/") { UseShellExecute = true });
        }
        catch (Exception) { /* no default browser — ignore */ }
    }
}
