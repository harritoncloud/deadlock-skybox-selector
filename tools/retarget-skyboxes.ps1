[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$DeadlockRoot = "C:\Program Files (x86)\Steam\steamapps\common\Deadlock"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# The City Never Sleeps map requests this material. The old packages still
# provide sky_dl_dusk03, which the new map no longer uses.
$oldMaterial = "materials/skybox/sky_dl_dusk03.vmat_c"
$newMaterial = "materials/skybox/dl_midtown_dusk_02.vmat_c"

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

public static class DeadlockSkyboxVpk
{
    public const string OldPath = "materials/skybox/sky_dl_dusk03.vmat_c";
    public const string NewPath = "materials/skybox/dl_midtown_dusk_02.vmat_c";
    private const int HeaderSize = 28;

    private sealed class Index
    {
        public int TreeSize;
        public int DataSize;
        public int ArchiveHashSize;
        public int OtherOffset;
        public int OldNameStart = -1;
        public int OldNameLength;
        public int OldCount;
        public int NewCount;
    }

    private static string ReadZ(byte[] data, ref int position, int limit, out int start, out int length)
    {
        start = position;
        while (position < limit && data[position] != 0) position++;
        if (position >= limit) throw new InvalidDataException("Unterminated VPK tree string.");
        length = position - start;
        string value = Encoding.UTF8.GetString(data, start, length);
        position++;
        return value;
    }

    private static Index Parse(byte[] data, bool verifyHashes)
    {
        if (data.Length < HeaderSize || BitConverter.ToUInt32(data, 0) != 0x55AA1234 ||
            BitConverter.ToUInt32(data, 4) != 2)
            throw new InvalidDataException("Expected a version-2 VPK.");

        var index = new Index();
        index.TreeSize = checked((int)BitConverter.ToUInt32(data, 8));
        index.DataSize = checked((int)BitConverter.ToUInt32(data, 12));
        index.ArchiveHashSize = checked((int)BitConverter.ToUInt32(data, 16));
        int otherSize = checked((int)BitConverter.ToUInt32(data, 20));
        int signatureSize = checked((int)BitConverter.ToUInt32(data, 24));
        index.OtherOffset = checked(HeaderSize + index.TreeSize + index.DataSize + index.ArchiveHashSize);
        if (otherSize != 48 || signatureSize != 0 || index.OtherOffset + 48 != data.Length)
            throw new InvalidDataException("Unsupported VPK sections or length.");

        int position = HeaderSize;
        int treeEnd = HeaderSize + index.TreeSize;
        int start, length;
        while (true)
        {
            string extension = ReadZ(data, ref position, treeEnd, out start, out length);
            if (extension.Length == 0) break;
            while (true)
            {
                string directory = ReadZ(data, ref position, treeEnd, out start, out length);
                if (directory.Length == 0) break;
                while (true)
                {
                    string name = ReadZ(data, ref position, treeEnd, out start, out length);
                    if (name.Length == 0) break;
                    string path = (directory == " " ? "" : directory + "/") + name +
                        (extension == " " ? "" : "." + extension);
                    if (path == OldPath)
                    {
                        index.OldCount++;
                        index.OldNameStart = start;
                        index.OldNameLength = length;
                    }
                    if (path == NewPath) index.NewCount++;
                    if (position + 18 > treeEnd || BitConverter.ToUInt16(data, position + 16) != 0xffff)
                        throw new InvalidDataException("Invalid VPK tree entry.");
                    int preload = BitConverter.ToUInt16(data, position + 4);
                    position += 18 + preload;
                    if (position > treeEnd) throw new InvalidDataException("Invalid VPK preload length.");
                }
            }
        }
        if (position != treeEnd)
            throw new InvalidDataException("VPK tree length does not match its header.");

        if (verifyHashes)
        {
            using (var md5 = MD5.Create())
            {
                Check(md5.ComputeHash(data, HeaderSize, index.TreeSize), data, index.OtherOffset);
                Check(md5.ComputeHash(data, HeaderSize + index.TreeSize + index.DataSize,
                    index.ArchiveHashSize), data, index.OtherOffset + 16);
                Check(md5.ComputeHash(data, 0, index.OtherOffset + 32), data, index.OtherOffset + 32);
            }
        }
        return index;
    }

    private static void Check(byte[] actual, byte[] data, int offset)
    {
        for (int i = 0; i < actual.Length; i++)
            if (actual[i] != data[offset + i])
                throw new InvalidDataException("VPK MD5 checksum mismatch.");
    }

    public static string Inspect(string path)
    {
        var index = Parse(File.ReadAllBytes(path), true);
        if (index.OldCount == 1 && index.NewCount == 0) return "old";
        if (index.OldCount == 0 && index.NewCount == 1) return "new";
        return "unknown";
    }

    public static void Retarget(string source, string destination)
    {
        byte[] original = File.ReadAllBytes(source);
        var index = Parse(original, true);
        if (index.OldCount != 1 || index.NewCount != 0)
            throw new InvalidDataException("The VPK has no unique old skybox material.");

        byte[] newName = Encoding.UTF8.GetBytes("dl_midtown_dusk_02");
        int difference = newName.Length - index.OldNameLength;
        byte[] updated = new byte[checked(original.Length + difference)];
        Array.Copy(original, 0, updated, 0, index.OldNameStart);
        Array.Copy(newName, 0, updated, index.OldNameStart, newName.Length);
        Array.Copy(original, index.OldNameStart + index.OldNameLength, updated,
            index.OldNameStart + newName.Length,
            original.Length - index.OldNameStart - index.OldNameLength);
        Array.Copy(BitConverter.GetBytes(checked(index.TreeSize + difference)), 0, updated, 8, 4);

        int newOtherOffset = index.OtherOffset + difference;
        using (var md5 = MD5.Create())
        {
            Array.Copy(md5.ComputeHash(updated, HeaderSize, index.TreeSize + difference), 0,
                updated, newOtherOffset, 16);
            Array.Copy(md5.ComputeHash(updated, 0, newOtherOffset + 32), 0,
                updated, newOtherOffset + 32, 16);
        }
        var check = Parse(updated, true);
        if (check.NewCount != 1 || check.OldCount != 0 || check.DataSize != index.DataSize)
            throw new InvalidDataException("Retargeted VPK failed validation.");
        File.WriteAllBytes(destination, updated);
    }
}
'@

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$assetRoot = Join-Path $repositoryRoot "unpacked\assets"
$manifestPath = Join-Path $assetRoot "manifest.json"
$gamePak = Join-Path $DeadlockRoot "game\citadel\pak01_dir.vpk"
if (-not (Test-Path -LiteralPath $gamePak -PathType Leaf)) {
    throw "Deadlock game archive is missing: $gamePak"
}
# The game archive has different VPK sections from the mod packages and still
# contains the old material. Read only its index to check the new name.
$gameTreeSize = 0
$gameFile = [IO.File]::OpenRead($gamePak)
try {
    $reader = [IO.BinaryReader]::new($gameFile)
    if ($reader.ReadUInt32() -ne 0x55AA1234 -or $reader.ReadUInt32() -ne 2) {
        throw "Unsupported Deadlock archive header: $gamePak"
    }
    $gameTreeSize = $reader.ReadInt32()
    if ($gameTreeSize -le 0 -or $gameTreeSize -gt 64MB) {
        throw "Unexpected Deadlock archive index size: $gameTreeSize"
    }
    $gameFile.Position = 28
    $gameTree = [Text.Encoding]::ASCII.GetString($reader.ReadBytes($gameTreeSize))
    if (-not $gameTree.Contains("dl_midtown_dusk_02")) {
        throw "The installed Deadlock archive does not contain $newMaterial."
    }
} finally {
    $gameFile.Dispose()
}

$manifestText = [IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json
$variants = @($manifest.variants)
if ($manifest.formatVersion -ne 2 -or $variants.Count -ne 32) {
    throw "Expected a format-2 manifest with 32 variants."
}

$entries = @()
foreach ($variant in $variants) {
    $path = [IO.Path]::GetFullPath((Join-Path $assetRoot ([string]$variant.entry)))
    $assetPrefix = [IO.Path]::GetFullPath($assetRoot).TrimEnd('\') + '\'
    if (-not $path.StartsWith($assetPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Unsafe or missing variant: $($variant.id)"
    }
    if ((Get-Item -LiteralPath $path).Length -ne [long]$variant.bytes -or
        (Get-Sha256 $path) -ne ([string]$variant.sha256).ToLowerInvariant()) {
        throw "Manifest hash/size mismatch: $($variant.id)"
    }
    $state = [DeadlockSkyboxVpk]::Inspect($path)
    if ($state -eq "unknown") { throw "Unknown skybox material in $($variant.id)" }
    $entries += [pscustomobject]@{ Id = [string]$variant.id; Path = $path; Entry = [string]$variant.entry;
        State = $state; OldHash = [string]$variant.sha256; OldBytes = [long]$variant.bytes }
}

$old = @($entries | Where-Object State -eq "old")
$new = @($entries | Where-Object State -eq "new")
if ($old.Count -eq 0) {
    if ($new.Count -ne 32) { throw "Incomplete skybox library." }
    Write-Host "All 32 skybox VPKs already target $newMaterial."
    exit 0
}
if ($new.Count -ne 0) { throw "Mixed old/new skybox library; refusing a partial rewrite." }
Write-Host "Found 32 old skybox VPKs targeting $oldMaterial."
if (-not $Apply) {
    Write-Host "Audit only. Re-run with -Apply to retarget and back up the original library."
    exit 0
}

$backupRoot = Join-Path $repositoryRoot ".retarget-backup"
$snapshotRoot = Join-Path $backupRoot ((Get-Date -Format "yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $snapshotRoot -Force | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $snapshotRoot "manifest.json")
$temporaryFiles = @()
$replacementFiles = @()
$prepared = @()
try {
    foreach ($entry in $entries) {
        $backup = Join-Path $snapshotRoot ($entry.Entry.Replace('/', '\'))
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $entry.Path -Destination $backup
        if ((Get-Sha256 $backup) -ne $entry.OldHash.ToLowerInvariant()) {
            throw "Backup verification failed: $($entry.Id)"
        }
        $temporary = $entry.Path + ".retarget.tmp"
        if (Test-Path -LiteralPath $temporary) {
            throw "Temporary file already exists: $temporary"
        }
        $temporaryFiles += $temporary
        [DeadlockSkyboxVpk]::Retarget($entry.Path, $temporary)
        $prepared += [pscustomobject]@{ Id = $entry.Id; Path = $entry.Path;
            Entry = $entry.Entry; OldHash = $entry.OldHash; OldBytes = $entry.OldBytes;
            NewHash = (Get-Sha256 $temporary); NewBytes = (Get-Item -LiteralPath $temporary).Length;
            Temporary = $temporary; Backup = $backup }
    }

    foreach ($item in $prepared) {
        $needle = '"id":  "' + $item.Id + '"'
        $idAt = $manifestText.IndexOf($needle, [StringComparison]::Ordinal)
        if ($idAt -lt 0) { throw "Manifest formatting changed for $($item.Id)" }
        $start = $manifestText.LastIndexOf('{', $idAt)
        $end = $manifestText.IndexOf('}', $idAt)
        if ($start -lt 0 -or $end -lt 0) { throw "Malformed manifest block for $($item.Id)" }
        $block = $manifestText.Substring($start, $end - $start + 1)
        $bytesMatch = [regex]::Match($block, '"bytes"\s*:\s*(\d+)')
        $hashMatch = [regex]::Match($block, '(?m)^([ \t]*)"sha256"\s*:\s*"([0-9a-fA-F]{64})"')
        if (-not $bytesMatch.Success -or -not $hashMatch.Success -or
            [long]$bytesMatch.Groups[1].Value -ne $item.OldBytes -or
            $hashMatch.Groups[2].Value.ToLowerInvariant() -ne $item.OldHash.ToLowerInvariant()) {
            throw "Manifest metadata changed for $($item.Id)"
        }
        $block = $block.Remove($bytesMatch.Groups[1].Index, $bytesMatch.Groups[1].Length).
            Insert($bytesMatch.Groups[1].Index, [string]$item.NewBytes)
        $hashMatch = [regex]::Match($block, '(?m)^([ \t]*)"sha256"\s*:\s*"([0-9a-fA-F]{64})"')
        $block = $block.Remove($hashMatch.Groups[2].Index, $hashMatch.Groups[2].Length).
            Insert($hashMatch.Groups[2].Index, $item.NewHash)
        $hashMatch = [regex]::Match($block, '(?m)^([ \t]*)"sha256"\s*:\s*"([0-9a-fA-F]{64})"')
        $insertAt = $hashMatch.Index + $hashMatch.Length
        $newline = if ($manifestText.Contains("`r`n")) { "`r`n" } else { "`n" }
        $legacyLine = ',' + $newline + $hashMatch.Groups[1].Value +
            '"legacySha256":  "' + $item.OldHash.ToLowerInvariant() + '"'
        $block = $block.Insert($insertAt, $legacyLine)
        $manifestText = $manifestText.Remove($start, $end - $start + 1).Insert($start, $block)
    }
    $updatedManifest = $manifestText | ConvertFrom-Json
    if (@($updatedManifest.variants).Count -ne 32) { throw "Updated manifest is invalid." }

    foreach ($item in $prepared) {
        $replaced = $item.Backup + ".replaced"
        if (Test-Path -LiteralPath $replaced) { throw "Replacement backup already exists: $replaced" }
        [IO.File]::Replace($item.Temporary, $item.Path, $replaced)
        $replacementFiles += $replaced
        if ((Get-Sha256 $item.Path) -ne $item.NewHash) {
            throw "Retargeted VPK verification failed: $($item.Id)"
        }
    }
    $manifestTemporary = $manifestPath + ".retarget.tmp"
    if (Test-Path -LiteralPath $manifestTemporary) { throw "Temporary manifest already exists." }
    [IO.File]::WriteAllText($manifestTemporary, $manifestText, [Text.UTF8Encoding]::new($false))
    $temporaryFiles += $manifestTemporary
    $manifestReplaced = Join-Path $snapshotRoot "manifest.replaced.json"
    [IO.File]::Replace($manifestTemporary, $manifestPath, $manifestReplaced)
    $replacementFiles += $manifestReplaced
    Write-Host "Retargeted all 32 skyboxes. Original library backup: $snapshotRoot"
} catch {
    foreach ($item in $prepared) {
        if (Test-Path -LiteralPath $item.Backup -PathType Leaf) {
            Copy-Item -LiteralPath $item.Backup -Destination $item.Path -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $snapshotRoot "manifest.json") -Destination $manifestPath -Force
    throw
} finally {
    foreach ($temporary in $temporaryFiles) {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
    foreach ($replaced in $replacementFiles) {
        if (Test-Path -LiteralPath $replaced -PathType Leaf) {
            Remove-Item -LiteralPath $replaced -Force
        }
    }
}
