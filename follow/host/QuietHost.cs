using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Usage Widget Background Host")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

internal static class QuietHost
{
    [STAThread]
    private static int Main(string[] args)
    {
        string mode = args.Length == 0 ? "--start" : args[0];
        bool background = mode == "--monitor";
        try
        {
            string script;
            string state = null;
            if (background && args.Length == 3 && args[1] == "--state-directory")
            {
                script = "FollowCodex.ps1";
                state = Path.GetFullPath(args[2]);
            }
            else if (mode == "--start" && args.Length <= 1) script = "Launch-FollowCodex.ps1";
            else if (mode == "--enable" && args.Length == 1) script = "Enable-FollowCodex.ps1";
            else if (mode == "--disable" && args.Length == 1) script = "Disable-FollowCodex.ps1";
            else throw new ArgumentException("Unknown launcher arguments.");

            string folder = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(folder, script);
            if (!File.Exists(path)) throw new FileNotFoundException("The companion script is missing.");
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(windows, @"System32\WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(path)
                    + (state == null ? "" : " -StateDirectory " + Quote(state)),
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process child = new Process { StartInfo = start })
            {
                // Drain output without persisting script output or account data.
                child.OutputDataReceived += delegate { };
                child.ErrorDataReceived += delegate { };
                child.Start();
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                child.WaitForExit();
                int code = child.ExitCode;
                if (code != 0 && !background) ShowFailure();
                // Staying alive until the monitor exits lets Task Scheduler own
                // its real lifetime and apply its configured restart policy.
                return code;
            }
        }
        catch
        {
            if (!background) ShowFailure();
            return 1;
        }
    }

    private static void ShowFailure()
    {
        MessageBox.Show("The Codex quota companion operation failed. Check that the complete package is extracted and Windows Task Scheduler is available.",
            "Codex quota companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string Quote(string value)
    {
        // Paths cannot contain a quote. Double terminal backslashes before the
        // closing quote so a root directory remains a single Windows argument.
        if (value.IndexOf('"') >= 0) throw new ArgumentException("Invalid path.");
        int trailing = 0;
        for (int i = value.Length - 1; i >= 0 && value[i] == '\\'; i--) trailing++;
        return "\"" + value + new string('\\', trailing) + "\"";
    }
}
