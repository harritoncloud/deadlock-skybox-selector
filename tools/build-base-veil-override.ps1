[CmdletBinding()]
param(
    [string]$DeadlockRoot = "C:\Program Files (x86)\Steam\steamapps\common\Deadlock",
    [ValidateSet("base-veil", "factory-smoke", "pickup-book")]
    [string]$Profile = "base-veil",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputPath) {
    $OutputPath = if ($Profile -eq "pickup-book") {
        Join-Path (Split-Path $repositoryRoot -Parent) "mods\hide-gained-book\pak06_dir.vpk"
    } else {
        Join-Path $repositoryRoot "unpacked\assets\overrides\$Profile-off_dir.vpk"
    }
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$gamePak = Join-Path $DeadlockRoot "game\citadel\pak01_dir.vpk"
if (-not (Test-Path -LiteralPath $gamePak -PathType Leaf)) {
    throw "Deadlock game archive is missing: $gamePak"
}
if (Test-Path -LiteralPath $OutputPath) {
    throw "Refusing to overwrite an existing override: $OutputPath"
}

# The pickup-book profile replaces only the model-rendering child of the gained
# pickup effect. The pre-pickup model, reward, beam, glow and burst remain intact.
# The compiled invisible material is a stock 1x1 alpha-zero material.
$sourcePaths = if ($Profile -eq "base-veil") {
    @("materials/dev/invisible.vmat_c", "particles/empty.vpcf_c")
} elseif ($Profile -eq "pickup-book") {
    @("particles/empty.vpcf_c")
} else {
    @("materials/dev/invisible.vmat_c")
}
$targetPaths = if ($Profile -eq "base-veil") {
    [ordered]@{
        "models/clouds/materials/cloud_funnel_01_amber.vmat_c" = "materials/dev/invisible.vmat_c"
        "models/clouds/materials/cloud_funnel_01_sapphire.vmat_c" = "materials/dev/invisible.vmat_c"
        "models/clouds/materials/cloud_ring_01.vmat_c" = "materials/dev/invisible.vmat_c"
        "models/clouds/materials/funnelcloudtest_ring_00.vmat_c" = "materials/dev/invisible.vmat_c"
        "particles/environmental/storm_glow_01.vpcf_c" = "particles/empty.vpcf_c"
    }
} elseif ($Profile -eq "factory-smoke") {
    [ordered]@{
        "models/factory/factory_pollution_cloud_01.vmat_c" = "materials/dev/invisible.vmat_c"
    }
} else {
    [ordered]@{
        "particles/generic/powerup_spawner_gained_model.vpcf_c" = "particles/empty.vpcf_c"
    }
}
$expectedSourceHashes = @{
    "materials/dev/invisible.vmat_c" = "8e2429b87275b80a0eb4261b63a04e8842be6b6085f4d5ce9e204cb510e91d7b"
    "particles/empty.vpcf_c" = "baa60070476e2c8a3d817c1e626955c62bcc8e821d8c7c35f3e75cdb7a153531"
}

function Read-ZString([IO.BinaryReader]$Reader) {
    $bytes = [Collections.Generic.List[byte]]::new()
    while ($true) {
        $value = $Reader.ReadByte()
        if ($value -eq 0) { break }
        $bytes.Add($value)
    }
    return [Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

function Write-ZString([IO.BinaryWriter]$Writer, [string]$Value) {
    $Writer.Write([Text.Encoding]::UTF8.GetBytes($Value))
    $Writer.Write([byte]0)
}

function Get-BytesSha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace("-", "").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

$entries = @{}
$stream = [IO.File]::OpenRead($gamePak)
try {
    $reader = [IO.BinaryReader]::new($stream)
    if ($reader.ReadUInt32() -ne 0x55AA1234 -or $reader.ReadUInt32() -ne 2) {
        throw "Unsupported Deadlock VPK header: $gamePak"
    }
    $treeSize = $reader.ReadInt32()
    if ($treeSize -le 0 -or $treeSize -gt 64MB) {
        throw "Unexpected Deadlock VPK tree size: $treeSize"
    }
    $stream.Position = 28
    $treeEnd = 28 + $treeSize
    while ($stream.Position -lt $treeEnd) {
        $extension = Read-ZString $reader
        if (-not $extension) { break }
        while ($true) {
            $directory = Read-ZString $reader
            if (-not $directory) { break }
            while ($true) {
                $name = Read-ZString $reader
                if (-not $name) { break }
                $path = "$directory/$name.$extension"
                $crc = $reader.ReadUInt32()
                $preload = $reader.ReadUInt16()
                $archive = $reader.ReadUInt16()
                $offset = $reader.ReadUInt32()
                $length = $reader.ReadUInt32()
                if ($reader.ReadUInt16() -ne 0xffff) {
                    throw "Invalid VPK tree entry for $path"
                }
                if ($sourcePaths -contains $path) {
                    if ($entries.ContainsKey($path) -or $preload -ne 0 -or $length -le 0) {
                        throw "Unexpected source entry: $path"
                    }
                    $entries[$path] = [pscustomobject]@{
                        Archive = $archive; Offset = $offset; Length = $length; Crc = $crc
                    }
                }
                $stream.Position += $preload
            }
        }
    }
    if ($stream.Position -ne $treeEnd -or $entries.Count -ne @($sourcePaths).Count) {
        throw "Could not find all stock invisible resources in the installed game."
    }
} finally {
    $stream.Dispose()
}

$sourceData = @{}
$gameDirectory = Split-Path $gamePak -Parent
foreach ($sourcePath in $sourcePaths) {
    $entry = $entries[$sourcePath]
    $archivePath = if ($entry.Archive -eq 0x7fff) {
        $gamePak
    } else {
        Join-Path $gameDirectory ("pak01_{0:D3}.vpk" -f [int]$entry.Archive)
    }
    $offset = [long]$entry.Offset
    if ($entry.Archive -eq 0x7fff) { $offset += 28 + $treeSize }
    $archiveStream = [IO.File]::OpenRead($archivePath)
    try {
        if ($offset + $entry.Length -gt $archiveStream.Length) {
            throw "Source VPK entry extends beyond its archive: $sourcePath"
        }
        $archiveStream.Position = $offset
        $bytes = [byte[]]::new([int]$entry.Length)
        if ($archiveStream.Read($bytes, 0, $bytes.Length) -ne $bytes.Length) {
            throw "Short read from source archive: $sourcePath"
        }
    } finally {
        $archiveStream.Dispose()
    }
    if ((Get-BytesSha256 $bytes) -ne $expectedSourceHashes[$sourcePath]) {
        throw "Stock resource changed after the patch; inspect it before rebuilding: $sourcePath"
    }
    $sourceData[$sourcePath] = $bytes
}

$files = foreach ($path in $targetPaths.Keys) {
    $sourcePath = [string]$targetPaths[$path]
    $name = $path.Substring($path.LastIndexOf('/') + 1)
    $extension = $name.Substring($name.LastIndexOf('.') + 1)
    [pscustomobject]@{
        Path = $path
        Directory = $path.Substring(0, $path.LastIndexOf('/'))
        Name = $name.Substring(0, $name.LastIndexOf('.'))
        Extension = $extension
        Crc = [uint32]$entries[$sourcePath].Crc
        Bytes = [byte[]]$sourceData[$sourcePath]
    }
}

$treeStream = [IO.MemoryStream]::new()
$dataStream = [IO.MemoryStream]::new()
try {
    $treeWriter = [IO.BinaryWriter]::new($treeStream)
    $dataWriter = [IO.BinaryWriter]::new($dataStream)
    foreach ($extensionGroup in @($files | Group-Object Extension | Sort-Object Name)) {
        Write-ZString $treeWriter $extensionGroup.Name
        foreach ($directoryGroup in @($extensionGroup.Group | Group-Object Directory | Sort-Object Name)) {
            Write-ZString $treeWriter $directoryGroup.Name
            foreach ($file in @($directoryGroup.Group | Sort-Object Name)) {
                Write-ZString $treeWriter $file.Name
                $treeWriter.Write([uint32]$file.Crc)
                $treeWriter.Write([uint16]0)
                $treeWriter.Write([uint16]0x7fff)
                $treeWriter.Write([uint32]$dataStream.Length)
                $treeWriter.Write([uint32]$file.Bytes.Length)
                $treeWriter.Write([uint16]0xffff)
                $dataWriter.Write($file.Bytes)
            }
            Write-ZString $treeWriter ""
        }
        Write-ZString $treeWriter ""
    }
    Write-ZString $treeWriter ""
    $tree = $treeStream.ToArray()
    $data = $dataStream.ToArray()
} finally {
    $treeStream.Dispose()
    $dataStream.Dispose()
}

$packageStream = [IO.MemoryStream]::new()
try {
    $writer = [IO.BinaryWriter]::new($packageStream)
    $writer.Write([uint32]0x55AA1234)
    $writer.Write([uint32]2)
    $writer.Write([uint32]$tree.Length)
    $writer.Write([uint32]$data.Length)
    $writer.Write([uint32]0)
    $writer.Write([uint32]48)
    $writer.Write([uint32]0)
    $writer.Write($tree)
    $writer.Write($data)
    $md5 = [Security.Cryptography.MD5]::Create()
    try {
        $writer.Write($md5.ComputeHash($tree))
        $writer.Write($md5.ComputeHash([byte[]]::new(0)))
        $writer.Write($md5.ComputeHash($packageStream.ToArray()))
    } finally {
        $md5.Dispose()
    }
    $package = $packageStream.ToArray()
} finally {
    $packageStream.Dispose()
}

$outputDirectory = Split-Path $OutputPath -Parent
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
[IO.File]::WriteAllBytes($OutputPath, $package)
Write-Host "Built cosmetic $Profile override: $OutputPath"
Write-Host "Bytes: $($package.Length)"
Write-Host "SHA-256: $(Get-BytesSha256 $package)"
