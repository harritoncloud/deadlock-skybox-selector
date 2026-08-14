using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class StartupSyntheticTest
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        int workCalls = 0;
        PreparationForm loading = new PreparationForm(delegate
        {
            Interlocked.Increment(ref workCalls);
            Thread.Sleep(120);
        }, "Loading skybox library", "Verifying files and preparing previews");

        Stopwatch elapsed = Stopwatch.StartNew();
        loading.ShowDialog();
        elapsed.Stop();
        long loaderVisibleMilliseconds = elapsed.ElapsedMilliseconds;
        bool loaderPassed = workCalls == 1 && loading.WorkError == null && loaderVisibleMilliseconds >= 400;
        loading.Dispose();

        FirstRunInstallForm consent = new FirstRunInstallForm(@"C:\Games\Deadlock");
        consent.Shown += delegate
        {
            System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 80;
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                consent.DialogResult = DialogResult.Cancel;
                consent.Close();
            };
            closeTimer.Start();
        };
        DialogResult result = consent.ShowDialog();
        bool consentPassed = result == DialogResult.Cancel && consent.ClientSize == new Size(500, 380);
        consent.Dispose();

        PermissionRequestForm permission = new PermissionRequestForm();
        elapsed.Restart();
        permission.ShowDialog();
        elapsed.Stop();
        bool permissionPassed = elapsed.ElapsedMilliseconds >= 240 && elapsed.ElapsedMilliseconds < 1500;
        permission.Dispose();

        Console.WriteLine("loader_work_calls={0}", workCalls);
        Console.WriteLine("loader_visible_ms={0}", loaderVisibleMilliseconds);
        Console.WriteLine("loader_passed={0}", loaderPassed);
        Console.WriteLine("consent_passed={0}", consentPassed);
        Console.WriteLine("permission_passed={0}", permissionPassed);
        return loaderPassed && consentPassed && permissionPassed ? 0 : 1;
    }
}
