using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class Program
{
    private const string ResourcePrefix = "SkyboxSelector.Payload.";
    private const string AssetResource = ResourcePrefix + "skyboxes.7z";
    private const string CacheDirectoryName = "patchwin.cc-skyboxes";

    private static readonly IDictionary<string, string> RuntimePayloads =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SkyboxSelector.cmd", "SkyboxSelector.cmd" },
            { "select-skybox.ps1", "select-skybox.ps1" },
            { "DeadlockGameInfoInstaller.exe", "DeadlockGameInfoInstaller.exe" },
            { "7z.exe", "7z.exe" },
            { "7z.dll", "7z.dll" },
            { "7zip-License.txt", "7zip-License.txt" },
            { "assets.sha256", "assets.sha256" },
            { "runtime-checksums.sha256", "runtime-checksums.sha256" }
        };

    private sealed class Options
    {
        public bool VerifyOnly;
        public bool PrepareOnly;
        public bool Elevated;
        public string DeadlockRoot;

        public bool NonInteractive
        {
            get { return VerifyOnly || PrepareOnly; }
        }
    }

    private sealed class AssetManifest
    {
        public int formatVersion { get; set; }
        public AssetVariant[] variants { get; set; }
    }

    private sealed class AssetVariant
    {
        public string id { get; set; }
        public string category { get; set; }
        public string displayName { get; set; }
        public string entry { get; set; }
        public long bytes { get; set; }
        public string sha256 { get; set; }
    }

    private static int Main(string[] args)
    {
        Options options = null;
        try
        {
            options = ParseOptions(args);
            string deadlockRoot = null;
            if (!options.VerifyOnly)
            {
                deadlockRoot = FindDeadlockRoot(options.DeadlockRoot);
                if (!options.Elevated && !IsAdministrator() && RequiresElevation(deadlockRoot))
                    return RestartElevated(args);
            }

            using (Mutex mutex = new Mutex(false, @"Local\DeadlockSkyboxSelector-OneFile-v2"))
            {
                if (!mutex.WaitOne(0, false))
                {
                    Console.WriteLine("Skybox Selector is already running.");
                    return 2;
                }

                string runtimeRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeadlockSkyboxSelector",
                    "runtime-v2");

                Console.WriteLine("Preparing Skybox Selector...");
                ExtractRuntimePayloads(runtimeRoot);
                VerifyRuntime(runtimeRoot);
                string expectedAssetHash = ReadAssetHash(runtimeRoot);

                if (options.VerifyOnly)
                {
                    VerifyEmbeddedAsset(expectedAssetHash);
                    Console.WriteLine("Embedded one-file verification passed.");
                    return 0;
                }

                string cacheRoot = Path.Combine(deadlockRoot, CacheDirectoryName);
                EnsureAssetCache(runtimeRoot, deadlockRoot, cacheRoot, expectedAssetHash);

                if (options.PrepareOnly)
                {
                    Console.WriteLine("Asset cache preparation passed: {0}", cacheRoot);
                    return 0;
                }

                string commandPath = Path.Combine(runtimeRoot, "SkyboxSelector.cmd");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                    Arguments = "/d /c call " + QuoteArgument(commandPath),
                    WorkingDirectory = runtimeRoot,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                startInfo.EnvironmentVariables["DEADLOCK_ROOT"] = deadlockRoot;
                startInfo.EnvironmentVariables["SKYBOX_CACHE_ROOT"] = cacheRoot;
                startInfo.EnvironmentVariables["SKYBOX_ASSET_SHA256"] = expectedAssetHash;

                using (Process child = Process.Start(startInfo))
                {
                    child.WaitForExit();
                    return child.ExitCode;
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: {0}", ex.Message);
            Console.ResetColor();
            if (options == null || !options.NonInteractive)
            {
                Console.WriteLine("Press any key to close...");
                Console.ReadKey(true);
            }
            return 1;
        }
    }

    private static Options ParseOptions(string[] args)
    {
        Options options = new Options();
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (String.Equals(argument, "--verify-only", StringComparison.OrdinalIgnoreCase))
                options.VerifyOnly = true;
            else if (String.Equals(argument, "--prepare-only", StringComparison.OrdinalIgnoreCase))
                options.PrepareOnly = true;
            else if (String.Equals(argument, "--elevated", StringComparison.OrdinalIgnoreCase))
                options.Elevated = true;
            else if (String.Equals(argument, "--deadlock-root", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length)
                    throw new ArgumentException("--deadlock-root requires a directory path.");
                options.DeadlockRoot = args[index];
            }
            else
                throw new ArgumentException("Unknown command-line argument: " + argument);
        }

        if (options.VerifyOnly && options.PrepareOnly)
            throw new ArgumentException("--verify-only and --prepare-only cannot be combined.");
        return options;
    }

    private static void ExtractRuntimePayloads(string runtimeRoot)
    {
        Directory.CreateDirectory(runtimeRoot);
        Assembly assembly = Assembly.GetExecutingAssembly();

        foreach (KeyValuePair<string, string> payload in RuntimePayloads)
        {
            string targetPath = Path.Combine(runtimeRoot, payload.Key);
            string targetDirectory = Path.GetDirectoryName(targetPath);
            if (!String.IsNullOrEmpty(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            string temporaryPath = targetPath + ".onefile-new";
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            using (Stream input = assembly.GetManifestResourceStream(ResourcePrefix + payload.Value))
            {
                if (input == null)
                    throw new InvalidDataException("Embedded runtime file is missing: " + payload.Key);
                using (FileStream output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }

            if (File.Exists(targetPath) && FilesEqual(targetPath, temporaryPath))
            {
                File.Delete(temporaryPath);
                continue;
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(temporaryPath, targetPath);
        }
    }

    private static void VerifyRuntime(string runtimeRoot)
    {
        string checksumPath = Path.Combine(runtimeRoot, "runtime-checksums.sha256");
        foreach (string line in File.ReadAllLines(checksumPath))
        {
            if (line.Length < 67 || line.Substring(64, 2) != "  ")
                continue;

            string expected = line.Substring(0, 64).ToUpperInvariant();
            string name = line.Substring(66).Replace('/', Path.DirectorySeparatorChar);
            string path = Path.Combine(runtimeRoot, name);
            if (!File.Exists(path) || ComputeSha256(path) != expected)
                throw new InvalidDataException("Extracted runtime file failed verification: " + name);
        }
    }

    private static string ReadAssetHash(string runtimeRoot)
    {
        string text = File.ReadAllText(Path.Combine(runtimeRoot, "assets.sha256")).Trim();
        Match match = Regex.Match(text, "^([0-9a-fA-F]{64})\\s{2}skyboxes\\.7z$");
        if (!match.Success)
            throw new InvalidDataException("Embedded asset checksum metadata is invalid.");
        return match.Groups[1].Value.ToUpperInvariant();
    }

    private static void VerifyEmbeddedAsset(string expectedHash)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AssetResource))
        {
            if (stream == null)
                throw new InvalidDataException("Embedded skybox archive is missing.");
            string actual = ComputeSha256(stream);
            if (actual != expectedHash)
                throw new InvalidDataException("Embedded skybox archive failed SHA-256 verification.");
        }
    }

    private static void EnsureAssetCache(
        string runtimeRoot,
        string deadlockRoot,
        string cacheRoot,
        string expectedAssetHash)
    {
        if (IsReadyCache(cacheRoot, expectedAssetHash))
        {
            ValidateCache(cacheRoot, false);
            Console.WriteLine("Skybox cache is ready: {0}", cacheRoot);
            return;
        }

        Console.WriteLine("First run: extracting 32 compressed skyboxes...");
        string token = Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N");
        string stagingRoot = cacheRoot + ".installing-" + token;
        string temporaryArchive = cacheRoot + ".assets-" + token + ".7z";

        try
        {
            Directory.CreateDirectory(stagingRoot);
            ExtractEmbeddedAsset(temporaryArchive, expectedAssetHash);
            ExtractWithSevenZip(runtimeRoot, temporaryArchive, stagingRoot);
            ValidateCache(stagingRoot, true);
            File.WriteAllText(Path.Combine(stagingRoot, ".ready.sha256"), expectedAssetHash + Environment.NewLine, Encoding.ASCII);

            if (Directory.Exists(cacheRoot))
            {
                string quarantine = cacheRoot + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.Move(cacheRoot, quarantine);
                Console.WriteLine("Previous incomplete cache was preserved: {0}", quarantine);
            }

            Directory.Move(stagingRoot, cacheRoot);
            Console.WriteLine("Skybox cache created: {0}", cacheRoot);
        }
        finally
        {
            if (File.Exists(temporaryArchive))
                File.Delete(temporaryArchive);
            DeleteOwnedStagingDirectory(stagingRoot, deadlockRoot);
        }
    }

    private static bool IsReadyCache(string cacheRoot, string expectedAssetHash)
    {
        string markerPath = Path.Combine(cacheRoot, ".ready.sha256");
        string manifestPath = Path.Combine(cacheRoot, "manifest.json");
        if (!File.Exists(markerPath) || !File.Exists(manifestPath))
            return false;

        string marker = File.ReadAllText(markerPath).Trim();
        return String.Equals(marker, expectedAssetHash, StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractEmbeddedAsset(string outputPath, string expectedHash)
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(AssetResource))
        {
            if (input == null)
                throw new InvalidDataException("Embedded skybox archive is missing.");

            using (SHA256 sha = SHA256.Create())
            using (FileStream output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (CryptoStream hashingOutput = new CryptoStream(output, sha, CryptoStreamMode.Write))
            {
                input.CopyTo(hashingOutput);
                hashingOutput.FlushFinalBlock();
                string actualHash = BytesToHex(sha.Hash);
                if (actualHash != expectedHash)
                    throw new InvalidDataException("Embedded skybox archive failed SHA-256 verification.");
            }
        }
    }

    private static void ExtractWithSevenZip(string runtimeRoot, string archivePath, string destinationRoot)
    {
        string sevenZipPath = Path.Combine(runtimeRoot, "7z.exe");
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            Arguments = "x -y -bd -bb0 -o" + QuoteArgument(destinationRoot) + " -- " + QuoteArgument(archivePath),
            WorkingDirectory = runtimeRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using (Process process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidDataException("7-Zip extraction failed: " + (error.Length > 0 ? error : output).Trim());
        }
    }

    private static void ValidateCache(string cacheRoot, bool verifyFiles)
    {
        string manifestPath = Path.Combine(cacheRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("Skybox cache manifest is missing.");

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        AssetManifest manifest = serializer.Deserialize<AssetManifest>(File.ReadAllText(manifestPath));
        if (manifest == null || manifest.formatVersion != 2 || manifest.variants == null || manifest.variants.Length != 32)
            throw new InvalidDataException("Skybox cache manifest is unsupported or incomplete.");

        int animeCount = 0;
        int realisticCount = 0;
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetVariant variant in manifest.variants)
        {
            if (variant == null || !Regex.IsMatch(variant.id ?? "", "^(anime_(0[1-9]|1[0-3])|realistic_(0[1-9]|1[0-9]))$"))
                throw new InvalidDataException("Skybox cache contains an invalid variant id.");
            if (!ids.Add(variant.id) || !hashes.Add(variant.sha256 ?? ""))
                throw new InvalidDataException("Skybox cache contains duplicate variant metadata.");
            if (!Regex.IsMatch(variant.sha256 ?? "", "^[0-9a-fA-F]{64}$") || variant.bytes <= 0)
                throw new InvalidDataException("Skybox cache contains invalid hash or size metadata: " + variant.id);

            if (String.Equals(variant.category, "anime", StringComparison.OrdinalIgnoreCase))
                animeCount++;
            else if (String.Equals(variant.category, "realistic", StringComparison.OrdinalIgnoreCase))
                realisticCount++;
            else
                throw new InvalidDataException("Skybox cache contains an invalid category: " + variant.id);

            string path = ResolveSafeCacheEntry(cacheRoot, variant.entry);
            if (!File.Exists(path))
                throw new InvalidDataException("Skybox cache file is missing: " + variant.entry);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Skybox cache file must not be a reparse point: " + variant.entry);
            if (new FileInfo(path).Length != variant.bytes)
                throw new InvalidDataException("Skybox cache file has an invalid size: " + variant.id);
            if (verifyFiles && !String.Equals(ComputeSha256(path), variant.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Skybox cache file failed SHA-256 verification: " + variant.id);
        }

        if (animeCount != 13 || realisticCount != 19)
            throw new InvalidDataException("Skybox cache category counts are invalid.");

        string animePreview = Path.Combine(cacheRoot, "previews", "anime-contact-sheet.jpg");
        string realisticPreview = Path.Combine(cacheRoot, "previews", "realistic-contact-sheet.jpg");
        if (!File.Exists(animePreview) || !File.Exists(realisticPreview))
            throw new InvalidDataException("Skybox cache preview sheets are missing.");
    }

    private static string ResolveSafeCacheEntry(string cacheRoot, string entry)
    {
        if (String.IsNullOrWhiteSpace(entry) || Path.IsPathRooted(entry))
            throw new InvalidDataException("Skybox cache contains an unsafe entry path.");

        string normalized = entry.Replace('/', Path.DirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(cacheRoot, normalized));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Skybox cache entry escapes the managed cache: " + entry);
        return fullPath;
    }

    private static void DeleteOwnedStagingDirectory(string path, string deadlockRoot)
    {
        if (!Directory.Exists(path))
            return;

        string fullRoot = Path.GetFullPath(deadlockRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        string leaf = Path.GetFileName(fullPath);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
            !leaf.StartsWith(CacheDirectoryName + ".installing-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to remove an unexpected staging directory: " + fullPath);
        Directory.Delete(fullPath, true);
    }

    private static string FindDeadlockRoot(string explicitRoot)
    {
        if (!String.IsNullOrWhiteSpace(explicitRoot))
            return ValidateDeadlockRoot(explicitRoot);

        List<string> candidates = new List<string>();
        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Deadlock");
        foreach (string steamRoot in FindSteamRoots())
        {
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", "Deadlock"));
            string librariesPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(librariesPath))
                continue;

            try
            {
                string contents = File.ReadAllText(librariesPath);
                foreach (Match match in Regex.Matches(contents, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    string library = match.Groups[1].Value.Replace("\\\\", "\\");
                    candidates.Add(Path.Combine(library, "steamapps", "common", "Deadlock"));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }
            if (!visited.Add(fullPath))
                continue;
            if (File.Exists(Path.Combine(fullPath, "game", "citadel", "gameinfo.gi")))
                return fullPath.TrimEnd(Path.DirectorySeparatorChar);
        }

        throw new DirectoryNotFoundException(
            "Deadlock was not found. Run with --deadlock-root \"D:\\...\\Deadlock\".");
    }

    private static IEnumerable<string> FindSteamRoots()
    {
        List<string> roots = new List<string>();
        AddSteamRoot(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddSteamRoot(roots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
        AddSteamRoot(roots, Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath");
        return roots;
    }

    private static void AddSteamRoot(List<string> roots, RegistryKey hive, string keyName, string valueName)
    {
        try
        {
            using (RegistryKey key = hive.OpenSubKey(keyName))
            {
                object value = key == null ? null : key.GetValue(valueName);
                if (value != null && !String.IsNullOrWhiteSpace(value.ToString()))
                    roots.Add(value.ToString().Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
    }

    private static string ValidateDeadlockRoot(string path)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(fullPath, "game", "citadel", "gameinfo.gi")))
            throw new DirectoryNotFoundException("The selected directory is not a valid Deadlock installation: " + fullPath);
        return fullPath;
    }

    private static bool RequiresElevation(string deadlockRoot)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return IsPathInside(deadlockRoot, programFiles) || IsPathInside(deadlockRoot, programFilesX86);
    }

    private static bool IsPathInside(string path, string root)
    {
        if (String.IsNullOrWhiteSpace(root))
            return false;
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdministrator()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RestartElevated(string[] originalArgs)
    {
        List<string> arguments = new List<string>(originalArgs);
        arguments.Add("--elevated");
        StringBuilder commandLine = new StringBuilder();
        foreach (string argument in arguments)
        {
            if (commandLine.Length > 0)
                commandLine.Append(' ');
            commandLine.Append(QuoteArgument(argument));
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = Assembly.GetExecutingAssembly().Location,
            Arguments = commandLine.ToString(),
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory
        };

        try
        {
            using (Process elevated = Process.Start(startInfo))
            {
                elevated.WaitForExit();
                return elevated.ExitCode;
            }
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                Console.WriteLine("Administrator permission was cancelled. No files were changed.");
                return 5;
            }
            throw;
        }
    }

    private static bool FilesEqual(string first, string second)
    {
        FileInfo a = new FileInfo(first);
        FileInfo b = new FileInfo(second);
        return a.Length == b.Length && ComputeSha256(first) == ComputeSha256(second);
    }

    private static string ComputeSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
            return ComputeSha256(stream);
    }

    private static string ComputeSha256(Stream stream)
    {
        using (SHA256 sha = SHA256.Create())
            return BytesToHex(sha.ComputeHash(stream));
    }

    private static string BytesToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
