using MiniERP2.Forms;
using MiniERP2.UI;

namespace MiniERP2;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetColorMode(SystemColorMode.System);
        ApplicationConfiguration.Initialize();

        var mainHub = new MainHub();
        FormManager.ApplyBoundsTracking(mainHub);
        Application.Run(mainHub);
    }
}
