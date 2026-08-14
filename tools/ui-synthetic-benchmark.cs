using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class UiSyntheticBenchmark
{
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: benchmark <Deadlock root>");
            return 2;
        }

        string deadlockRoot = Path.GetFullPath(args[0]);
        string cacheRoot = Path.Combine(deadlockRoot, "dlskybox");
        string runtimeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlockSkyboxSelector",
            "runtime-v2");
        string assetHash = File.ReadAllText(Path.Combine(cacheRoot, ".ready.sha256")).Trim();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        SelectorForm form = new SelectorForm(runtimeRoot, deadlockRoot, cacheRoot, assetHash);
        form.Show();
        PumpMessages(3000);

        Process process = Process.GetCurrentProcess();
        process.Refresh();
        long workingSetBefore = process.WorkingSet64;
        long privateBefore = process.PrivateMemorySize64;
        uint gdiBefore = GetGuiResources(process.Handle, 0);
        uint userBefore = GetGuiResources(process.Handle, 1);
        TimeSpan cpuBefore = process.TotalProcessorTime;

        MethodInfo selectVariant = typeof(SelectorForm).GetMethod(
            "SelectVariant",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo variantsField = typeof(SelectorForm).GetField(
            "variants",
            BindingFlags.Instance | BindingFlags.NonPublic);
        List<SkyboxVariant> variants = (List<SkyboxVariant>)variantsField.GetValue(form);

        Stopwatch stress = Stopwatch.StartNew();
        for (int index = 0; index < 180; index++)
        {
            selectVariant.Invoke(form, new object[] { variants[index % variants.Count] });
            if ((index % 6) == 0)
                PumpMessages(10);
        }
        PumpMessages(3500);
        stress.Stop();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        PumpMessages(250);

        process.Refresh();
        long workingSetAfter = process.WorkingSet64;
        long privateAfter = process.PrivateMemorySize64;
        uint gdiAfter = GetGuiResources(process.Handle, 0);
        uint userAfter = GetGuiResources(process.Handle, 1);
        TimeSpan cpuAfter = process.TotalProcessorTime;

        Console.WriteLine("selection_cycles=180");
        Console.WriteLine("stress_elapsed_ms={0:F1}", stress.Elapsed.TotalMilliseconds);
        Console.WriteLine("stress_cpu_ms={0:F1}", (cpuAfter - cpuBefore).TotalMilliseconds);
        Console.WriteLine("gdi_before={0}", gdiBefore);
        Console.WriteLine("gdi_after={0}", gdiAfter);
        Console.WriteLine("gdi_delta={0}", (long)gdiAfter - gdiBefore);
        Console.WriteLine("user_before={0}", userBefore);
        Console.WriteLine("user_after={0}", userAfter);
        Console.WriteLine("user_delta={0}", (long)userAfter - userBefore);
        Console.WriteLine("working_set_before_mb={0:F1}", workingSetBefore / 1048576D);
        Console.WriteLine("working_set_after_mb={0:F1}", workingSetAfter / 1048576D);
        Console.WriteLine("private_before_mb={0:F1}", privateBefore / 1048576D);
        Console.WriteLine("private_after_mb={0:F1}", privateAfter / 1048576D);

        bool passed = gdiAfter <= gdiBefore + 4 &&
            userAfter <= userBefore + 4 &&
            privateAfter <= privateBefore + (8L * 1024L * 1024L);
        form.Close();
        form.Dispose();
        return passed ? 0 : 1;
    }

    private static void PumpMessages(int milliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }
}
