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
            return;
        }

        bool isUpdate = args.Contains("--update", StringComparer.OrdinalIgnoreCase);
        bool isSilent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase) ||
                        args.Contains("/s", StringComparer.OrdinalIgnoreCase) ||
                        args.Contains("-s", StringComparer.OrdinalIgnoreCase);
        bool restart = args.Contains("--restart", StringComparer.OrdinalIgnoreCase);

        int waitPid = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--wait-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int pid))
            {
                waitPid = pid;
                break;
            }
        }

        string? customTarget = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--target", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("/dir", StringComparison.OrdinalIgnoreCase))
            {
                customTarget = args[i + 1].Trim('\"');
                break;
            }
        }

        if (isSilent)
        {
            var runner = new SilentInstallRunner(customTarget, waitPid, restart);
            runner.Execute();
            return;
        }

        if (isUpdate)
        {
            Application.Run(new InstallerForm(isUpdateMode: true, restartAfter: restart, waitPid: waitPid, targetDirectory: customTarget));
            return;
        }

        Application.Run(new InstallerForm(targetDirectory: customTarget));
    }
}
