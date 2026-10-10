param(
    [string]$AmongUsPath = 'C:\Users\mitsu\Downloads\among-us-windows\AmongUsItch\Itch'
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $AmongUsPath).Path
$gameExe = Join-Path $gameRoot 'Among Us.exe'
if (-not (Test-Path -LiteralPath $gameExe)) { throw 'Among Us.exe was not found.' }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\interop\Assembly-CSharp.dll'))) {
    throw 'BepInEx IL2CPP and complete generated interop assemblies are required.'
}
$running = Get-CimInstance Win32_Process -Filter "Name = 'Among Us.exe'"
if ($running | Where-Object { -not $_.ExecutablePath }) { throw 'Unable to verify a running Among Us path.' }
$isGameRunning = [bool]($running | Where-Object { [string]::Equals($_.ExecutablePath, $gameExe, [StringComparison]::OrdinalIgnoreCase) })
& dotnet build (Join-Path $PSScriptRoot 'RandomLanguage.csproj') -c Release "-p:AmongUsPath=$gameRoot"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$source = Join-Path $PSScriptRoot 'bin\Release\net6.0\RandomLanguage.dll'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($source).FileVersion
$version = ([version]$version).ToString(3)
$destination = Join-Path $gameRoot "BepInEx\plugins\RandomLanguage-v$version.dll"
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
$builtHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if (Test-Path -LiteralPath $destination) {
    if ($builtHash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
        throw "A different DLL already exists at $destination. No files were overwritten."
    }
} else {
    # CreateNew also refuses an overwrite if another process creates the file after the check.
    $destinationStream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $sourceStream = [IO.File]::OpenRead($source)
        try { $sourceStream.CopyTo($destinationStream) } finally { $sourceStream.Dispose() }
    } finally { $destinationStream.Dispose() }
}
if ($builtHash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
    throw 'Installed DLL hash does not match the build.'
}
Write-Output "Installed: $destination"
Write-Output "SHA256: $builtHash"
Write-Output 'Existing DLLs were preserved. BepInEx selects the newest version of this plugin at startup.'
if ($isGameRunning) { Write-Output 'The running game still uses the previous version. Restart it when convenient to apply this update.' }
