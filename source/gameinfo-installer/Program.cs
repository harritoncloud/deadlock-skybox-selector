using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

[assembly: AssemblyTitle("Deadlock GameInfo Installer")]
[assembly: AssemblyDescription("Installs the embedded Deadlock gameinfo configuration after explicit consent")]
[assembly: AssemblyCompany("patchwin.cc")]
[assembly: AssemblyProduct("Deadlock GameInfo Installer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class Program
{
    private const string ResourceName = "DeadlockGameInfoInstaller.gameinfo.gi";
    private const string DefaultDeadlockRoot = @"C:\Program Files (x86)\Steam\steamapps\common\Deadlock";

    private static int Main(string[] args)
    {
        bool noPause = HasArgument(args, "--no-pause");
        bool autoConfirm = HasArgument(args, "--yes");
        bool restore = HasArgument(args, "--restore");
        try
        {
            byte[] payload = ReadEmbeddedConfig();
            ValidateConfig(payload);

            if (HasArgument(args, "--inspect"))
            {
                Console.WriteLine("Embedded gameinfo.gi");
                Console.WriteLine("Bytes: {0}", payload.Length);
                Console.WriteLine("SHA-256: {0}", ComputeSha256(payload));
                return Finish(0, noPause);
            }

            if (HasArgument(args, "--self-test"))
            {
                RunSelfTest(payload);
                Console.WriteLine("Self-test passed.");
                return Finish(0, noPause);
            }

            string deadlockRoot = ResolveDeadlockRoot(args);
            string targetPath = Path.Combine(deadlockRoot, @"game\citadel\gameinfo.gi");
            ValidateInstallation(targetPath);
            string restoreBackupPath = restore ? FindOriginalBackup(targetPath) : null;

            if (IsManagedProcessRunning())
            {
                throw new InvalidOperationException("Close Deadlock and Deadlock Mod Manager before changing GameInfo.");
            }

            bool elevatedMode = HasArgument(args, "--elevated");
            if (elevatedMode)
            {
                if (!IsAdministrator())
                {
                    throw new UnauthorizedAccessException("Administrator permission was not granted.");
                }

                int elevatedResult = restore
                    ? RunElevatedRestore(targetPath, restoreBackupPath, autoConfirm)
                    : RunElevatedInstall(targetPath, payload, autoConfirm);
                return Finish(elevatedResult, noPause || HasArgument(args, "--child"));
            }

            if (restore)
                PrintRestoreSummary(targetPath, restoreBackupPath);
            else
                PrintInstallationSummary(targetPath, payload);
            string confirmation = restore
                ? "Request administrator permission and restore the original GameInfo backup? [y/N]: "
                : "Request administrator permission and install this config? [y/N]: ";
            if (!autoConfirm && !AskYesNo(confirmation))
            {
                Console.WriteLine("Operation cancelled. No files were changed.");
                return Finish(0, noPause);
            }

            if (IsAdministrator())
            {
                int directResult = restore
                    ? RunElevatedRestore(targetPath, restoreBackupPath, autoConfirm)
                    : RunElevatedInstall(targetPath, payload, autoConfirm);
                return Finish(directResult, noPause);
            }

            int childResult = RequestElevation(deadlockRoot, autoConfirm, restore);
            if (childResult == 0)
            {
                Console.WriteLine("Elevated installer completed successfully.");
            }
            else if (childResult == 3)
            {
                Console.WriteLine("Installation was cancelled in the elevated installer. No files were changed.");
            }
            else
            {
                Console.WriteLine("Elevated installer returned exit code {0}.", childResult);
            }
            return Finish(childResult, noPause);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: {0}", ex.Message);
            Console.ResetColor();
            return Finish(1, noPause);
        }
    }

    private static int RunElevatedInstall(string targetPath, byte[] payload, bool skipConfirmation)
    {
        PrintInstallationSummary(targetPath, payload);
        if (!skipConfirmation && !AskYesNo("Final confirmation: replace gameinfo.gi and create a backup? [y/N]: "))
        {
            Console.WriteLine("Installation cancelled. No files were changed.");
            return 3;
        }

        string backupPath = InstallPayload(targetPath, payload);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Installed and verified: {0}", targetPath);
        Console.ResetColor();
        if (!String.IsNullOrEmpty(backupPath))
        {
            Console.WriteLine("Backup: {0}", backupPath);
        }
        return 0;
    }

    private static int RunElevatedRestore(string targetPath, string backupPath, bool skipConfirmation)
    {
        PrintRestoreSummary(targetPath, backupPath);
        if (!skipConfirmation && !AskYesNo("Final confirmation: restore the original GameInfo backup? [y/N]: "))
        {
            Console.WriteLine("Restore cancelled. No files were changed.");
            return 3;
        }

        string currentBackupPath = RestoreBackup(targetPath, backupPath);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Restored and verified: {0}", targetPath);
        Console.ResetColor();
        if (!String.IsNullOrEmpty(currentBackupPath))
            Console.WriteLine("Previous current config: {0}", currentBackupPath);
        return 0;
    }

    private static void PrintInstallationSummary(string targetPath, byte[] payload)
    {
        Console.WriteLine("Deadlock GameInfo Installer");
        Console.WriteLine("Target: {0}", targetPath);
        Console.WriteLine("Embedded SHA-256: {0}", ComputeSha256(payload));
        if (File.Exists(targetPath))
        {
            Console.WriteLine("Current SHA-256:  {0}", ComputeSha256(File.ReadAllBytes(targetPath)));
        }
        Console.WriteLine("The installer does not launch Deadlock.");
        Console.WriteLine();
    }

    private static void PrintRestoreSummary(string targetPath, string backupPath)
    {
        Console.WriteLine("Deadlock GameInfo Restore");
        Console.WriteLine("Target: {0}", targetPath);
        Console.WriteLine("Original backup: {0}", backupPath);
        Console.WriteLine("Backup SHA-256: {0}", ComputeSha256(File.ReadAllBytes(backupPath)));
        if (File.Exists(targetPath))
            Console.WriteLine("Current SHA-256: {0}", ComputeSha256(File.ReadAllBytes(targetPath)));
        Console.WriteLine("The current config will be backed up before restoration.");
        Console.WriteLine();
    }

    private static string InstallPayload(string targetPath, byte[] payload)
    {
        string targetDirectory = Path.GetDirectoryName(targetPath);
        if (String.IsNullOrEmpty(targetDirectory))
        {
            throw new InvalidOperationException("Unable to resolve the target directory.");
        }
        Directory.CreateDirectory(targetDirectory);

        string expectedHash = ComputeSha256(payload);
        string temporaryPath = targetPath + ".patchwin-new";
        string backupPath = null;
        byte[] originalData = File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : null;

        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        try
        {
            File.WriteAllBytes(temporaryPath, payload);
            if (!String.Equals(ComputeSha256(File.ReadAllBytes(temporaryPath)), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Temporary config verification failed.");
            }

            if (File.Exists(targetPath))
            {
                backupPath = targetPath + ".patchwin-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                File.Replace(temporaryPath, targetPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }

            if (!String.Equals(ComputeSha256(File.ReadAllBytes(targetPath)), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Installed config verification failed.");
            }
            return backupPath;
        }
        catch
        {
            try
            {
                if (originalData != null)
                {
                    File.WriteAllBytes(targetPath, originalData);
                }
                else if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
            }
            catch
            {
                // Preserve the original installation error for the caller.
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string FindOriginalBackup(string targetPath)
    {
        string directory = Path.GetDirectoryName(targetPath);
        if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("Deadlock citadel directory is missing: " + directory);

        string filter = Path.GetFileName(targetPath) + ".patchwin-backup-*";
        string[] backups = Directory.GetFiles(directory, filter, SearchOption.TopDirectoryOnly);
        Array.Sort(backups, StringComparer.OrdinalIgnoreCase);
        if (backups.Length == 0)
            throw new FileNotFoundException("The original GameInfo backup was not found. Install the component once before using restore.");

        ValidateRestoreCandidate(File.ReadAllBytes(backups[0]));
        return backups[0];
    }

    private static string RestoreBackup(string targetPath, string backupPath)
    {
        byte[] restoredData = File.ReadAllBytes(backupPath);
        ValidateRestoreCandidate(restoredData);

        string expectedHash = ComputeSha256(restoredData);
        string temporaryPath = targetPath + ".patchwin-restore-new";
        string currentBackupPath = null;
        byte[] currentData = File.Exists(targetPath) ? File.ReadAllBytes(targetPath) : null;

        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        try
        {
            File.WriteAllBytes(temporaryPath, restoredData);
            if (!String.Equals(ComputeSha256(File.ReadAllBytes(temporaryPath)), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Temporary restore verification failed.");

            if (File.Exists(targetPath))
            {
                currentBackupPath = targetPath + ".patchwin-pre-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                File.Replace(temporaryPath, targetPath, currentBackupPath, true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }

            if (!String.Equals(ComputeSha256(File.ReadAllBytes(targetPath)), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Restored config verification failed.");
            if (currentData != null && !String.Equals(
                ComputeSha256(File.ReadAllBytes(currentBackupPath)),
                ComputeSha256(currentData),
                StringComparison.OrdinalIgnoreCase))
                throw new IOException("Current config backup verification failed.");
            return currentBackupPath;
        }
        catch
        {
            try
            {
                if (currentData != null)
                    File.WriteAllBytes(targetPath, currentData);
                else if (File.Exists(targetPath))
                    File.Delete(targetPath);
            }
            catch
            {
                // Preserve the original restore error for the caller.
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateRestoreCandidate(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new InvalidDataException("The original GameInfo backup is empty.");

        string text = new UTF8Encoding(false, true).GetString(data);
        if (text.IndexOf("GameInfo", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidDataException("The original backup is not a valid gameinfo.gi file.");

        int openingBraces = 0;
        int closingBraces = 0;
        foreach (char value in text)
        {
            if (value == '{') openingBraces++;
            if (value == '}') closingBraces++;
        }
        if (openingBraces == 0 || openingBraces != closingBraces)
            throw new InvalidDataException("The original GameInfo backup has unbalanced braces.");
    }

    private static int RequestElevation(string deadlockRoot, bool autoConfirm, bool restore)
    {
        string executablePath = Assembly.GetExecutingAssembly().Location;
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = executablePath;
        startInfo.Arguments = "--elevated --child" + (autoConfirm ? " --yes" : "") +
            (restore ? " --restore" : "") +
            " --deadlock-root " + QuoteArgument(deadlockRoot);
        startInfo.UseShellExecute = true;
        startInfo.Verb = "runas";
        startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath);

        try
        {
            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                Console.WriteLine("Administrator permission was declined. No files were changed.");
                return 2;
            }
            throw;
        }
    }

    private static bool IsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private static bool IsManagedProcessRunning()
    {
        string[] names = { "deadlock", "dmm", "deadlock-modmanager" };
        foreach (string name in names)
        {
            if (Process.GetProcessesByName(name).Length > 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string ResolveDeadlockRoot(string[] args)
    {
        string explicitRoot = GetArgumentValue(args, "--deadlock-root");
        if (!String.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot.Trim().TrimEnd(Path.DirectorySeparatorChar));
        }

        if (Directory.Exists(DefaultDeadlockRoot))
        {
            return DefaultDeadlockRoot;
        }

        object steamValue = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
        if (steamValue != null)
        {
            string steamRoot = Convert.ToString(steamValue).Replace('/', Path.DirectorySeparatorChar);
            string candidate = Path.Combine(steamRoot, @"steamapps\common\Deadlock");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            string libraryFile = Path.Combine(steamRoot, @"steamapps\libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                MatchCollection matches = Regex.Matches(File.ReadAllText(libraryFile), @"""path""\s+""([^""]+)""");
                foreach (Match match in matches)
                {
                    string libraryRoot = match.Groups[1].Value.Replace(@"\\", @"\");
                    candidate = Path.Combine(libraryRoot, @"steamapps\common\Deadlock");
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new DirectoryNotFoundException("Deadlock installation was not found. Use --deadlock-root with the game path.");
    }

    private static void ValidateInstallation(string targetPath)
    {
        string citadelDirectory = Path.GetDirectoryName(targetPath);
        if (String.IsNullOrEmpty(citadelDirectory) || !Directory.Exists(citadelDirectory))
        {
            throw new DirectoryNotFoundException("Deadlock citadel directory is missing: " + citadelDirectory);
        }

        if (File.Exists(targetPath))
        {
            string currentText = File.ReadAllText(targetPath);
            if (currentText.IndexOf("GameInfo", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidDataException("The existing target is not a valid gameinfo.gi file.");
            }
        }
    }

    private static byte[] ReadEmbeddedConfig()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
            {
                throw new InvalidDataException("Embedded gameinfo resource is missing.");
            }
            using (MemoryStream memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }

    private static void ValidateConfig(byte[] payload)
    {
        string text = new UTF8Encoding(false, true).GetString(payload);
        if (text.IndexOf("GameInfo", StringComparison.Ordinal) < 0)
        {
            throw new InvalidDataException("Embedded config has no GameInfo root.");
        }
        if (!Regex.IsMatch(text, @"(?im)^\s*Game\s+""citadel/addons""\s*$"))
        {
            throw new InvalidDataException("Embedded config does not mount citadel/addons.");
        }

        string[,] requiredAssignments =
        {
            { "r_farz", "-1" },
            { "r_mapextents", "16384" },
            { "r_nearz", "-1" },
            { "sc_screen_size_lod_scale_override", "-1" },
            { "sc_fade_distance_scale_override", "-1" },
            { "r_size_cull_threshold", "0.85" },
            { "r_render_hair", "true" },
            { "r_pixelvisibility_partial", "true" },
            { "engine_max_ticks_to_simulate", "-1" },
            { "citadel_unit_status_use_new", "true" },
            { "panorama_max_fps", "165" },
            { "r_particle_max_size_cull", "900" },
            { "sc_aggregate_gpu_vis_culling", "true" },
            { "snd_steamaudio_num_diffuse_samples", "1024" },
            { "fog_enable", "false" },
            { "lb_enable_lights", "false" },
            { "lb_enable_sunlight", "false" },
            { "sc_disable_baked_lighting", "true" },
            { "cl_phys_enabled", "true" },
            { "r_hair_ao", "0" }
        };

        for (int i = 0; i < requiredAssignments.GetLength(0); i++)
        {
            string pattern = @"(?im)^\s*" + Regex.Escape(requiredAssignments[i, 0]) + @"\s+""" + Regex.Escape(requiredAssignments[i, 1]) + @"""";
            if (!Regex.IsMatch(text, pattern))
            {
                throw new InvalidDataException("Embedded config is missing required assignment: " + requiredAssignments[i, 0]);
            }
        }

        string[] userOwnedAssignments =
        {
            "citadel_video_preset",
            "r_citadel_upscaling",
            "mat_viewportscale",
            "r_citadel_dlss_settings_mode",
            "r_dlss_preset",
            "r_citadel_fsr_rcas_sharpness",
            "r_citadel_fsr2_sharpness",
            "r_citadel_antialiasing",
            "r_texture_stream_mip_bias",
            "r_dashboard_render_quality",
            "r_citadel_shadow_quality",
            "mat_set_shader_quality",
            "r_citadel_ssao_quality",
            "r_citadel_distancefield_ao_quality",
            "r_citadel_fog_quality",
            "r_depth_of_field",
            "r_effects_bloom",
            "r_post_bloom",
            "r_arealights",
            "r_particle_depth_feathering",
            "fps_max",
            "r_low_latency",
            "r_light_sensitivity_mode",
            "fps_max_ui",
            "fullscreen",
            "nowindowborder",
            "setting.defaultres",
            "setting.defaultresheight",
            "setting.refreshrate_numerator",
            "setting.refreshrate_denominator"
        };

        foreach (string name in userOwnedAssignments)
        {
            string pattern = @"(?im)^\s*" + Regex.Escape(name) + @"\s+(?:""[^""\r\n]*""|[^\s/{][^\r\n/]*)";
            if (Regex.IsMatch(text, pattern))
            {
                throw new InvalidDataException("Embedded config overrides a user-owned setting: " + name);
            }
        }

        int openingBraces = 0;
        int closingBraces = 0;
        foreach (char value in text)
        {
            if (value == '{') openingBraces++;
            if (value == '}') closingBraces++;
        }
        if (openingBraces != closingBraces)
        {
            throw new InvalidDataException("Embedded config has unbalanced braces.");
        }
    }

    private static void RunSelfTest(byte[] payload)
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "DeadlockGameInfoInstaller-selftest-" + Guid.NewGuid().ToString("N"));
        string targetPath = Path.Combine(testRoot, @"game\citadel\gameinfo.gi");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            byte[] originalData = Encoding.ASCII.GetBytes("GameInfo\r\n{\r\n}\r\n");
            File.WriteAllBytes(targetPath, originalData);
            string backupPath = InstallPayload(targetPath, payload);
            if (String.IsNullOrEmpty(backupPath) || !File.Exists(backupPath))
            {
                throw new InvalidOperationException("Self-test did not create a backup.");
            }
            if (!String.Equals(ComputeSha256(File.ReadAllBytes(backupPath)), ComputeSha256(originalData), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Self-test backup hash mismatch.");
            }
            if (!String.Equals(ComputeSha256(File.ReadAllBytes(targetPath)), ComputeSha256(payload), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Self-test target hash mismatch.");
            }

            string discoveredBackup = FindOriginalBackup(targetPath);
            if (!String.Equals(discoveredBackup, backupPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Self-test selected the wrong original backup.");
            }
            string currentBackupPath = RestoreBackup(targetPath, discoveredBackup);
            if (String.IsNullOrEmpty(currentBackupPath) || !File.Exists(currentBackupPath))
            {
                throw new InvalidOperationException("Self-test did not back up the current config before restore.");
            }
            if (!String.Equals(ComputeSha256(File.ReadAllBytes(targetPath)), ComputeSha256(originalData), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Self-test restore target hash mismatch.");
            }
            if (!String.Equals(ComputeSha256(File.ReadAllBytes(currentBackupPath)), ComputeSha256(payload), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Self-test pre-restore backup hash mismatch.");
            }
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }

    private static string ComputeSha256(byte[] data)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(data);
            StringBuilder result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                result.Append(value.ToString("X2"));
            }
            return result.ToString();
        }
    }

    private static bool AskYesNo(string prompt)
    {
        Console.Write(prompt);
        string response = Console.ReadLine();
        return String.Equals(response, "y", StringComparison.OrdinalIgnoreCase) ||
               String.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasArgument(string[] args, string name)
    {
        foreach (string arg in args)
        {
            if (String.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetArgumentValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static int Finish(int exitCode, bool noPause)
    {
        if (!noPause)
        {
            Console.WriteLine();
            Console.Write("Press Enter to close...");
            Console.ReadLine();
        }
        return exitCode;
    }
}
