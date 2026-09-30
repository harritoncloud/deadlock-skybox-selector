param(
    [ValidateSet('PlayerMarkers', 'HealthLines', 'ClassicAbilityFill')]
    [string]$StyleKind = 'PlayerMarkers',
    [string]$BaseStyle = '',
    [string]$OutputVpk = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
if ($StyleKind -eq 'ClassicAbilityFill') {
    $styleName = 'citadel_hud_ability_upgrade_pips'
    $vpkName = 'classic-ability-fill_dir.vpk'
    $expectedBaseHash = 'BC4AF182AAE5A8E4F78E1B56D8ED8AF2A65E0FFD16E4EFCE466F17247F7E858D'
    $oldFill = 'background-color: deadlockGreenDarker;'
    $newFill = 'background-color: #362147;'
    $oldCheck = 'wash-color: deadlockGreenDark;wash-color: offBlack;opacity: 0.5;'
    $newCheck = 'wash-color: spiritLightColor;opacity: 1;'
    $replacements = @(
        @($oldFill, ($newFill + (' ' * ($oldFill.Length - $newFill.Length)))),
        @($oldCheck, ($newCheck + (' ' * ($oldCheck.Length - $newCheck.Length))))
    )
    $headerStyle = Join-Path $repositoryRoot 'source\overrides\hud_abilities.vcss_c'
    $expectedHeaderHash = 'D846371B9F5E992FEF38E9885BE0A322F67CD839D8B017341F9054BDB60B4C99'
    $oldHeader = 'border-top: 6px solid deadlockGreen;'
    $newHeader = 'border-top: 6px solid #CE90FF;'
    $headerReplacements = ,@($oldHeader, ($newHeader + (' ' * ($oldHeader.Length - $newHeader.Length))))
    $apHudStyle = Join-Path $repositoryRoot 'source\overrides\hud.vcss_c'
    $expectedApHudHash = '4CBF7AD312EC9EB81377122185296102668DBB94D233FA7FEC0EC07081AF27B1'
    $apRules = @(
        @('wash-color: deadlockGreen;}.APCurrencyAmount{', 'wash-color: #d8b8ff;}.APCurrencyAmount{'),
        @('color: deadlockGreen;}.infiniteMoney .APCurrencyAmount{', 'color: #d8b8ff;}.infiniteMoney .APCurrencyAmount{'),
        @('#hudAPInfinite{visibility: collapse;wash-color: deadlockGreen&80;}', '#hudAPInfinite{visibility: collapse;wash-color: #d8b8ff&80;}')
    )
    foreach ($rule in $apRules) {
        $rule[1] += ' ' * ($rule[0].Length - $rule[1].Length)
    }
} elseif ($StyleKind -eq 'HealthLines') {
    $styleName = 'hud_health'
    $vpkName = 'hide-health-lines_dir.vpk'
    $expectedBaseHash = 'DB6613A280BDBB8EB37011407D8E75F40AD6B8230AF259FECC828BF998A72674'
    $replacements = @(
        @('#healthLines .line_large{width: 100%;height: 3px;background-color: offBlack;opacity: 0.7;}',
          '#healthLines .line_large{width: 100%;height: 3px;background-color: offBlack;opacity: 0.0;}'),
        @('#healthLines .line_small{width: 30%;height: 3px;background-color: offBlack;opacity: 0.7;}',
          '#healthLines .line_small{width: 30%;height: 3px;background-color: offBlack;opacity: 0.0;}')
    )
} else {
    $styleName = 'hud_unit_indicators_v2'
    $vpkName = 'hide-player-markers_dir.vpk'
    $expectedBaseHash = '0AEEBCEF780BB10B29B2F256E042555BB1A2C5B44B6AAD6A40162BBB602B000A'
    $replacements = ,@('.PlayerEntry.visible{visibility: visible;}',
                       '.PlayerEntry.visible{visibility:collapse;}')
}
if ([String]::IsNullOrWhiteSpace($BaseStyle)) {
    $BaseStyle = Join-Path $repositoryRoot "source\overrides\$styleName.vcss_c"
}
if ([String]::IsNullOrWhiteSpace($OutputVpk)) {
    $OutputVpk = Join-Path $repositoryRoot "unpacked\assets\overrides\$vpkName"
}
# The compiled VCSS stores its rules as plain text inside the DATA block.
# Same-length substitutions preserve every resource offset.
function Get-PatchedStyle([string]$Path, [string]$ExpectedHash, [object[]]$Rules) {
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedHash) {
        throw "The captured Deadlock stylesheet changed: $actualHash"
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $sourceText = [Text.Encoding]::ASCII.GetString($bytes)
    foreach ($replacement in $Rules) {
        $oldRule = [string]$replacement[0]
        $newRule = [string]$replacement[1]
        if ($oldRule.Length -ne $newRule.Length) {
            throw 'A replacement rule changed the byte length.'
        }
        $ruleIndex = $sourceText.IndexOf($oldRule, [StringComparison]::Ordinal)
        if ($ruleIndex -lt 0 -or $sourceText.LastIndexOf($oldRule, [StringComparison]::Ordinal) -ne $ruleIndex) {
            throw "Expected exactly one occurrence of $oldRule"
        }
        [Array]::Copy([Text.Encoding]::ASCII.GetBytes($newRule), 0, $bytes, $ruleIndex, $newRule.Length)
    }
    return ,$bytes
}

$entries = @([pscustomobject]@{
    Name = $styleName
    Payload = (Get-PatchedStyle $BaseStyle $expectedBaseHash $replacements)
})
if ($StyleKind -eq 'ClassicAbilityFill') {
    $entries += [pscustomobject]@{
        Name = 'hud_abilities'
        Payload = (Get-PatchedStyle $headerStyle $expectedHeaderHash $headerReplacements)
    }
    $entries += [pscustomobject]@{
        Name = 'hud'
        Payload = (Get-PatchedStyle $apHudStyle $expectedApHudHash $apRules)
    }
}

Add-Type -TypeDefinition @'
using System;
public static class PlayerMarkerVpkCrc32
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
Write-ZString $treeWriter 'vcss_c'
Write-ZString $treeWriter 'panorama/styles'
$offset = 0
foreach ($entry in $entries) {
    $payload = [byte[]]$entry.Payload
    Write-ZString $treeWriter $entry.Name
    $treeWriter.Write([uint32][PlayerMarkerVpkCrc32]::Compute($payload))
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
$fileStream = [IO.File]::Open($OutputVpk, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $writer = [IO.BinaryWriter]::new($fileStream, [Text.Encoding]::ASCII, $true)
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
    $fileStream.Dispose()
}
$wholeHash = $md5.ComputeHash([IO.File]::ReadAllBytes($OutputVpk))
$md5.Dispose()
$appendStream = [IO.File]::Open($OutputVpk, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $appendStream.Write($wholeHash, 0, $wholeHash.Length)
} finally {
    $appendStream.Dispose()
}

Write-Output "Built $OutputVpk"
