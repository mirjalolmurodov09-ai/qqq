using System.IO;
using System.Windows;
using AiUstozPro.Infrastructure;

namespace AiUstozPro.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        string? dataDir = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--data-dir") dataDir = args[i + 1];

        if (args.Contains("--smoke-test"))
        {
            dataDir ??= Path.Combine(Path.GetTempPath(), "AiUstozPro-smoke");
            Shutdown(RunSmoke(dataDir));
            return;
        }

        MessageBox.Show("AI Ustoz Pro — interfeys yig'ilmoqda.", "AI Ustoz Pro");
        Shutdown(0);
    }

    private static int RunSmoke(string dataDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(dataDir))!;
        Directory.CreateDirectory(parent);
        var logPath = Path.Combine(parent, Path.GetFileName(dataDir) + "-smoke.log");
        var lines = new List<string>();
        int code;
        try
        {
            SmokeScenario.Run(dataDir, lines.Add);
            code = 0;
        }
        catch (Exception ex)
        {
            lines.Add("XATO: " + ex);
            code = 1;
        }
        Directory.CreateDirectory(dataDir);
        File.WriteAllLines(Path.Combine(dataDir, "smoke-test.log"), lines);
        File.WriteAllLines(logPath, lines);
        return code;
    }
}
