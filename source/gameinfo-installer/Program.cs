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

            if (IsManagedProcessRunning())
            {
                throw new InvalidOperationException("Close Deadlock and Deadlock Mod Manager before installing the config.");
            }

            bool elevatedMode = HasArgument(args, "--elevated");
            if (elevatedMode)
            {
                if (!IsAdministrator())
                {
                    throw new UnauthorizedAccessException("Administrator permission was not granted.");
                }

                int elevatedResult = RunElevatedInstall(targetPath, payload);
                return Finish(elevatedResult, noPause || HasArgument(args, "--child"));
            }

            PrintInstallationSummary(targetPath, payload);
            if (!AskYesNo("Request administrator permission and install this config? [y/N]: "))
            {
                Console.WriteLine("Installation cancelled. No files were changed.");
                return Finish(0, noPause);
            }

            if (IsAdministrator())
            {
                int directResult = RunElevatedInstall(targetPath, payload);
                return Finish(directResult, noPause);
            }

            int childResult = RequestElevation(deadlockRoot);
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

    private static int RunElevatedInstall(string targetPath, byte[] payload)
    {
        PrintInstallationSummary(targetPath, payload);
        if (!AskYesNo("Final confirmation: replace gameinfo.gi and create a backup? [y/N]: "))
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

    private static int RequestElevation(string deadlockRoot)
    {
        string executablePath = Assembly.GetExecutingAssembly().Location;
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.FileName = executablePath;
        startInfo.Arguments = "--elevated --child --deadlock-root " + QuoteArgument(deadlockRoot);
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
            { "r_size_cull_threshold", "0.8" },
            { "r_render_hair", "true" },
            { "r_pixelvisibility_partial", "true" },
            { "engine_max_ticks_to_simulate", "-1" },
            { "panorama_max_fps", "165" },
            { "citadel_unit_status_use_new", "true" }
        };

        for (int i = 0; i < requiredAssignments.GetLength(0); i++)
        {
            string pattern = @"(?im)^\s*" + Regex.Escape(requiredAssignments[i, 0]) + @"\s+""" + Regex.Escape(requiredAssignments[i, 1]) + @"""";
            if (!Regex.IsMatch(text, pattern))
            {
                throw new InvalidDataException("Embedded config is missing required assignment: " + requiredAssignments[i, 0]);
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
