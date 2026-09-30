param(
    [ValidateRange(0.01, 0.30)]
    [double]$Reduction = 0.12,
    [string]$OutputVpk = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputVpk) {
    $OutputVpk = Join-Path $root 'unpacked\assets\overrides\low-contrast_dir.vpk'
}

$sources = @(
    [pscustomobject]@{
        Name = 'basepostprocess_deadlock'
        Path = Join-Path $root 'source\overrides\basepostprocess_deadlock.vpost_c'
        Sha256 = '0C497E30D4983F613EBBC897CA2350EFA9D9B13061FE5B565E7DF0403B628F63'
        LutOffset = 0
        ToneToeOffset = 2856
    },
    [pscustomobject]@{
        Name = 'caldera'
        Path = Join-Path $root 'source\overrides\caldera.vpost_c'
        Sha256 = 'CA8216CD497382FA9DBFCCE7503D3E9CC6408FE42B42A99CFB82127790C81AB0'
        LutOffset = 2324
        ToneToeOffset = 0
    }
)

$lutBytes = 32 * 32 * 32 * 4
$entries = @()
foreach ($source in $sources) {
    $actualHash = (Get-FileHash -LiteralPath $source.Path -Algorithm SHA256).Hash
    if ($actualHash -ne $source.Sha256) {
        throw "Deadlock postprocess resource changed: $($source.Name), $actualHash"
    }
    $payload = [IO.File]::ReadAllBytes($source.Path)
    $start = [int]$source.LutOffset
    if ($start -gt 0) {
        if ($start + $lutBytes -gt $payload.Length) {
            throw "LUT bounds are invalid: $($source.Name)"
        }
        if ($payload[$start] -ne 0 -or $payload[$start + 1] -ne 0 -or
            $payload[$start + 2] -ne 0 -or $payload[$start + 3] -ne 255) {
            throw "LUT beginning changed: $($source.Name)"
        }

        for ($i = 0; $i -lt $lutBytes; $i += 4) {
            $pixel = $start + $i
            if ($payload[$pixel + 3] -ne 255) {
                throw "LUT alpha changed: $($source.Name) at pixel $($i / 4)"
            }
            $luma = 0.2126 * $payload[$pixel] +
                0.7152 * $payload[$pixel + 1] +
                0.0722 * $payload[$pixel + 2]
            $shift = $Reduction * (127.5 - $luma)
            for ($channel = 0; $channel -lt 3; $channel++) {
                $value = [Math]::Round($payload[$pixel + $channel] + $shift)
                $payload[$pixel + $channel] = [byte][Math]::Max(0, [Math]::Min(255, $value))
            }
        }
    }

    if ($source.ToneToeOffset -gt 0) {
        $toneOffset = [int]$source.ToneToeOffset
        $oldToe = [BitConverter]::ToDouble($payload, $toneOffset)
        if ([Math]::Abs($oldToe - 0.6966) -gt 0.00001) {
            throw "Tone-mapping toe strength changed: $oldToe"
        }
        # The compiled KV3 stores these values as a float rounded to a double.
        $newToe = [BitConverter]::GetBytes([double][single]0.55)
        [Array]::Copy($newToe, 0, $payload, $toneOffset, $newToe.Length)
    }

    $entries += [pscustomobject]@{ Name = $source.Name; Payload = $payload }
}

Add-Type -TypeDefinition @'
using System;
public static class LowContrastVpkCrc32
{
    private static readonly uint[] Table = BuildTable();
    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            table[i] = value;
        }
        return table;
    }
    public static uint Compute(byte[] bytes)
    {
        uint value = 0xFFFFFFFFu;
        foreach (var b in bytes)
            value = Table[(value ^ b) & 0xFF] ^ (value >> 8);
        return ~value;
    }
}
'@

function Write-ZString([IO.BinaryWriter]$Writer, [string]$Value) {
    $Writer.Write([Text.Encoding]::ASCII.GetBytes($Value))
    $Writer.Write([byte]0)
}

$treeStream = [IO.MemoryStream]::new()
$treeWriter = [IO.BinaryWriter]::new($treeStream, [Text.Encoding]::ASCII, $true)
Write-ZString $treeWriter 'vpost_c'
Write-ZString $treeWriter 'postprocessing'
$offset = 0
foreach ($entry in $entries) {
    $payload = [byte[]]$entry.Payload
    Write-ZString $treeWriter $entry.Name
    $treeWriter.Write([uint32][LowContrastVpkCrc32]::Compute($payload))
    $treeWriter.Write([uint16]0)
    $treeWriter.Write([uint16]0x7fff)
    $treeWriter.Write([uint32]$offset)
    $treeWriter.Write([uint32]$payload.Length)
    $treeWriter.Write([uint16]0xffff)
    $offset += $payload.Length
}
$treeWriter.Write([byte]0)
$treeWriter.Write([byte]0)
$treeWriter.Write([byte]0)
$treeWriter.Flush()
$tree = $treeStream.ToArray()
$treeWriter.Dispose()
$treeStream.Dispose()

$outputDirectory = Split-Path -Parent $OutputVpk
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$stream = [IO.File]::Open($OutputVpk, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII, $true)
    $writer.Write([uint32]0x55aa1234)
    $writer.Write([uint32]2)
    $writer.Write([uint32]$tree.Length)
    $writer.Write([uint32]$offset)
    $writer.Write([uint32]0)
    $writer.Write([uint32]48)
    $writer.Write([uint32]0)
    $writer.Write($tree)
    foreach ($entry in $entries) {
        $writer.Write([byte[]]$entry.Payload)
    }
    $md5 = [Security.Cryptography.MD5]::Create()
    $writer.Write($md5.ComputeHash($tree))
    $writer.Write($md5.ComputeHash([byte[]]@()))
    $writer.Flush()
    $writer.Dispose()
} finally {
    $stream.Dispose()
}
$wholeHash = $md5.ComputeHash([IO.File]::ReadAllBytes($OutputVpk))
$md5.Dispose()
$stream = [IO.File]::Open($OutputVpk, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $stream.Write($wholeHash, 0, $wholeHash.Length)
} finally {
    $stream.Dispose()
}

Write-Output "Built $OutputVpk with softened tone mapping and $([Math]::Round($Reduction * 100))% lower LUT luma contrast."
