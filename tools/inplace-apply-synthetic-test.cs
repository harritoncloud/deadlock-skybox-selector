using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class InPlaceApplySyntheticTest
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
            return 2;

        string deadlockRoot = Path.GetFullPath(args[0]);
        string cacheRoot = Path.Combine(deadlockRoot, "dlskybox");
        string runtimeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlockSkyboxSelector",
            "runtime-v2");
        string assetHash = File.ReadAllText(Path.Combine(cacheRoot, ".ready.sha256")).Trim();
        string selectedPath = Path.Combine(cacheRoot, "selected-skybox.txt");
        bool selectedFileExisted = File.Exists(selectedPath);
        byte[] selectedFileContents = selectedFileExisted ? File.ReadAllBytes(selectedPath) : null;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SelectorForm form = null;

        try
        {
            form = new SelectorForm(runtimeRoot, deadlockRoot, cacheRoot, assetHash);

            FieldInfo variantsField = typeof(SelectorForm).GetField("variants", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo selectedField = typeof(SelectorForm).GetField("selectedVariant", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo currentField = typeof(SelectorForm).GetField("currentSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo applyInPlace = typeof(SelectorForm).GetMethod("ApplySelectionInPlace", BindingFlags.Instance | BindingFlags.NonPublic);
            List<SkyboxVariant> variants = (List<SkyboxVariant>)variantsField.GetValue(form);
            SkyboxVariant selected = variants[0];
            selectedField.SetValue(form, selected);

            applyInPlace.Invoke(form, new object[] { selected.id });
            string current = (string)currentField.GetValue(form);
            int processIdBefore = System.Diagnostics.Process.GetCurrentProcess().Id;
            Application.DoEvents();
            int processIdAfter = System.Diagnostics.Process.GetCurrentProcess().Id;

            bool passed = current == selected.id && processIdBefore == processIdAfter && !form.IsDisposed;
            Console.WriteLine("selection={0}", current);
            Console.WriteLine("same_process={0}", processIdBefore == processIdAfter);
            Console.WriteLine("form_alive={0}", !form.IsDisposed);
            return passed ? 0 : 1;
        }
        finally
        {
            if (form != null)
                form.Dispose();
            if (selectedFileExisted)
                File.WriteAllBytes(selectedPath, selectedFileContents);
            else if (File.Exists(selectedPath))
                File.Delete(selectedPath);
        }
    }
}
