using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NextXLR.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").ToLowerInvariant();
        bool isUninstall = args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase) ||
                           exeName.Contains("uninstall") ||
                           exeName.Contains("deinstall");

        if (isUninstall)
        {
            Application.Run(new UninstallerForm());
        }
        else
        {
            Application.Run(new InstallerForm());
        }
    }
}
