<#
.SYNOPSIS
    Builds a Pixel8 release: tests, the self-contained game and the installer artifacts\Pixel8-Setup-<version>.exe.

.DESCRIPTION
    Run from anywhere (paths are relative to the repo root), or double-click publish.bat at the root:
        powershell -ExecutionPolicy Bypass -File build\publish.ps1
    Steps (stops at the first failure):
      1. Reads the version from Directory.Build.props (<Version>).
      2. Runs the tests in Pixel8.sln.
      3. Publishes Pixel8.Desktop self-contained for win-x64 (a folder, not single-file, no trimming) to artifacts\app\
         and checks the game data and licence files came with it.
      4. Optionally signs Pixel8.exe (-Sign; off for now).
      5. Compiles installer\Pixel8.iss with Inno Setup 6 into artifacts\, then signs the setup (if -Sign) and writes
         its SHA-256 next to it.
    Needs: the .NET 8 SDK and Inno Setup 6 (https://jrsoftware.org/isinfo.php; a build tool, not shipped).
    Mirrors Throng's release routine.

.PARAMETER SkipTests
    Skip the tests (not for real releases).

.PARAMETER Sign
    Sign Pixel8.exe and the setup with signtool (needs -CertThumbprint and signtool on PATH).

.PARAMETER InnoCompiler
    Full path to ISCC.exe if Inno Setup 6 isn't in one of its usual folders.
#>
param(
    [switch] $SkipTests,
    [switch] $Sign,
    [string] $CertThumbprint = '',
    [string] $InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'

function Step([string] $text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check-Exit([string] $what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." } }

# ---- 1. Version
[xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "The version '$version' isn't major.minor.patch." }
Step "Pixel8 $version"

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

# ---- 2. Tests
if (-not $SkipTests) {
    Step 'Tests'
    dotnet test (Join-Path $root 'Pixel8.sln') -c Release --nologo
    Check-Exit 'The tests'
}

# ---- 3. The game (self-contained, so players need no .NET runtime)
Step 'Pixel8.Desktop (self-contained win-x64)'
$appOut = Join-Path $artifacts 'app'
dotnet publish (Join-Path $root 'src\Pixel8.Desktop\Pixel8.Desktop.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishTrimmed=false -p:PublishSingleFile=false -o $appOut --nologo
Check-Exit 'Publishing Pixel8.Desktop'
Get-ChildItem $appOut -Filter *.pdb -Recurse | Remove-Item -Force
foreach ($needed in 'Pixel8.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'data\cards.json', 'data\heroes.json',
                    'data\enemies.json', 'data\items.json', 'data\events.json', 'data\balance.json') {
    if (-not (Test-Path (Join-Path $appOut $needed))) { throw "The game publish lacks $needed." }
}

# ---- 4. Signing (off for now)
function Sign-Files([string[]] $files) {
    if (-not $Sign) { return }
    if (-not $CertThumbprint) { throw '-Sign needs -CertThumbprint.' }
    & signtool sign /sha1 $CertThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $files
    Check-Exit 'signtool'
}
Sign-Files @(Join-Path $appOut 'Pixel8.exe')

# ---- 5. The installer
Step 'Installer'
if (-not $InnoCompiler) {
    $InnoCompiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                      "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $InnoCompiler) {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $InnoCompiler = $onPath.Source }
}
if (-not $InnoCompiler) { throw 'Inno Setup 6 (ISCC.exe) was not found: install it or pass -InnoCompiler.' }
& $InnoCompiler "/DAppVersion=$version" "/O$artifacts" (Join-Path $root 'installer\Pixel8.iss')
Check-Exit 'Inno Setup'

$setup = Join-Path $artifacts "Pixel8-Setup-$version.exe"
Sign-Files @($setup)
Step "Done: $setup"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host "SHA-256 $hash"
"$hash  Pixel8-Setup-$version.exe" | Set-Content (Join-Path $artifacts "Pixel8-Setup-$version.sha256.txt")
