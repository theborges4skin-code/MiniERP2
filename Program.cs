using MiniERP2.Forms;

namespace MiniERP2;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetColorMode(SystemColorMode.System);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainHub());
    }
}
