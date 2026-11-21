# Makes the native debug symbols zip Google Play takes for an app bundle, from the MapLibre release the app uses.
# The bundle's other native libraries (.NET runtime, SQLite, ML Kit) come stripped, with no symbols published.
#
# MaplibreNativeMAUI attaches <package>-<version>-android-symbols.zip (<abi>/libmln-cabi.so, unstripped) to each
# GitHub release from 5.0.0-pre.5. This picks the release from the project's MapLibreNative.Maui* package reference,
# keeps the ABIs the bundle ships, and checks each library's build ID against the bundle's copy, since Play matches
# symbols by build ID.
#
# Writes $OutFile and returns its path, or warns and returns nothing when there is nothing usable to upload, so a
# release never fails over symbols. -SymbolsZip uses a local zip instead of downloading one.
param(
    [Parameter(Mandatory)][string]$Aab,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Project = 'VistumblerMAUI/VistumblerMAUI.csproj',
    [string]$SymbolsZip,
    [string]$Repository = 'TechIdiots-LLC/MaplibreNativeMAUI'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# The GNU build ID (NT_GNU_BUILD_ID note) of a little-endian ELF image, as hex, or $null
function Get-BuildId([byte[]]$Elf) {
    if ($Elf.Length -lt 64 -or $Elf[0] -ne 0x7F -or $Elf[1] -ne 0x45 -or $Elf[2] -ne 0x4C -or $Elf[3] -ne 0x46) { return $null }
    $is64 = $Elf[4] -eq 2
    if ($is64) {
        $phoff = [BitConverter]::ToInt64($Elf, 0x20); $phentsize = [BitConverter]::ToUInt16($Elf, 0x36); $phnum = [BitConverter]::ToUInt16($Elf, 0x38)
    } else {
        $phoff = [BitConverter]::ToUInt32($Elf, 0x1C); $phentsize = [BitConverter]::ToUInt16($Elf, 0x2A); $phnum = [BitConverter]::ToUInt16($Elf, 0x2C)
    }
    for ($i = 0; $i -lt $phnum; $i++) {
        $ph = $phoff + $i * $phentsize
        if ([BitConverter]::ToUInt32($Elf, $ph) -ne 4) { continue }   # PT_NOTE
        if ($is64) { $off = [BitConverter]::ToInt64($Elf, $ph + 8); $size = [BitConverter]::ToInt64($Elf, $ph + 0x20) }
        else { $off = [BitConverter]::ToUInt32($Elf, $ph + 4); $size = [BitConverter]::ToUInt32($Elf, $ph + 0x10) }
        $pos = $off; $end = $off + $size
        while ($pos + 12 -le $end) {
            $namesz = [BitConverter]::ToUInt32($Elf, $pos)
            $descsz = [BitConverter]::ToUInt32($Elf, $pos + 4)
            $type = [BitConverter]::ToUInt32($Elf, $pos + 8)
            $name = [Text.Encoding]::ASCII.GetString($Elf, $pos + 12, [Math]::Max(0, $namesz - 1))
            $desc = $pos + 12 + (($namesz + 3) -band -bnot 3)
            if ($type -eq 3 -and $name -eq 'GNU') {
                return (($Elf[$desc..($desc + $descsz - 1)] | ForEach-Object { $_.ToString('x2') }) -join '')
            }
            $pos = $desc + (($descsz + 3) -band -bnot 3)
        }
    }
    $null
}

function Read-Entry([IO.Compression.ZipArchiveEntry]$Entry) {
    $ms = New-Object IO.MemoryStream
    $s = $Entry.Open()
    try { $s.CopyTo($ms) } finally { $s.Dispose() }
    , $ms.ToArray()
}

$m = [regex]::Match((Get-Content $Project -Raw), '<PackageReference\s+Include="(MapLibreNative\.Maui(?:\.Handlers|\.Vulkan)?)"\s+Version="([^"]+)"')
if (-not $m.Success) { Write-Warning "No MapLibreNative.Maui package in $Project; no native symbols."; return }
$package = if ($m.Groups[1].Value -eq 'MapLibreNative.Maui.Vulkan') { 'MapLibreNative.Maui.Vulkan' } else { 'MapLibreNative.Maui' }
$version = $m.Groups[2].Value

$downloaded = $null
if (-not $SymbolsZip) {
    $url = "https://github.com/$Repository/releases/download/v$version/$package-$version-android-symbols.zip"
    $downloaded = Join-Path ([IO.Path]::GetTempPath()) "$package-$version-android-symbols.zip"
    try {
        Invoke-WebRequest -Uri $url -OutFile $downloaded -UseBasicParsing
    } catch {
        Write-Warning "No MapLibre symbols at $url ($($_.Exception.Message)); none uploaded."
        return
    }
    $SymbolsZip = $downloaded
}

$bundle = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Aab))
$symbols = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $SymbolsZip))
$out = $null
$added = 0
try {
    # .NET Framework writes zips with backslashes in entry names
    $symbolEntries = @{}
    foreach ($e in $symbols.Entries) { $symbolEntries[$e.FullName.Replace('\', '/')] = $e }
    foreach ($entry in $bundle.Entries) {
        $abiMatch = [regex]::Match($entry.FullName, '^base/lib/([^/]+)/libmln-cabi\.so$')
        if (-not $abiMatch.Success) { continue }
        $abi = $abiMatch.Groups[1].Value
        $symbolEntry = $symbolEntries["$abi/libmln-cabi.so"]
        if (-not $symbolEntry) { Write-Warning "$SymbolsZip has no $abi/libmln-cabi.so"; continue }
        $shipped = Get-BuildId (Read-Entry $entry)
        $bytes = Read-Entry $symbolEntry
        $unstripped = Get-BuildId $bytes
        if (-not $shipped -or $shipped -ne $unstripped) {
            Write-Warning "$abi/libmln-cabi.so: build ID $unstripped in the symbols, $shipped in the bundle; skipped."
            continue
        }
        if (-not $out) {
            if (Test-Path $OutFile) { Remove-Item $OutFile }
            $out = [IO.Compression.ZipFile]::Open($OutFile, [IO.Compression.ZipArchiveMode]::Create)
        }
        $s = $out.CreateEntry("$abi/libmln-cabi.so", [IO.Compression.CompressionLevel]::Optimal).Open()
        try { $s.Write($bytes, 0, $bytes.Length) } finally { $s.Dispose() }
        Write-Host "MapLibre $version symbols for $abi (build ID $shipped)"
        $added++
    }
} finally {
    $bundle.Dispose(); $symbols.Dispose()
    if ($out) { $out.Dispose() }
    if ($downloaded) { Remove-Item $downloaded -ErrorAction SilentlyContinue }
}

if ($added -eq 0) { Write-Warning "No MapLibre symbols match $Aab; none uploaded."; return }
(Resolve-Path $OutFile).Path
