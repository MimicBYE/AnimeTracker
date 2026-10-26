[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'AnimeTracker.exe'),
    [ValidateSet('anycpu','x64','x86')][string]$Platform = 'anycpu'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The 64-bit .NET Framework compiler was not found. Build on 64-bit Windows with .NET Framework 4.8 or newer.' }
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputPath)) -Force | Out-Null
$sourceNames = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'windows-sources.txt') | Where-Object { $_.Trim().Length -gt 0 })
$sourceFiles = foreach ($name in $sourceNames) {
    if ($name -notmatch '^src/(AnimeTracker.Windows|AnimeTracker.Core)/[A-Za-z0-9]+\.cs$') { throw "Invalid source filename: $name" }
    $path = Join-Path $root $name
    if (!(Test-Path -LiteralPath $path)) { throw "Missing source: $name" }
    $path
}
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Net.Http.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$arguments = @('/nologo','/target:winexe',('/platform:' + $Platform),'/optimize+',
    ('/resource:' + (Join-Path $root 'src/AnimeTracker.Core/ArcCatalog.xml') + ',AnimeTracker.ArcCatalog'),
    ('/resource:' + (Join-Path $root 'src/AnimeTracker.Windows/Assets/DropdownTheme.xaml') + ',AnimeTracker.DropdownTheme'),
    ('/resource:' + (Join-Path $root 'src/AnimeTracker.Windows/Assets/app.ico') + ',AnimeTracker.Icon'),
    ('/win32icon:' + (Join-Path $root 'src/AnimeTracker.Windows/Assets/app.ico')),
    ('/win32manifest:' + (Join-Path $root 'src/AnimeTracker.Windows/Assets/app.manifest')),
    ('/out:' + $OutputPath))
& $compiler $arguments $refs $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$configSource = Join-Path $root 'AnimeTracker.exe.config'
$configTarget = $OutputPath + '.config'
if ([IO.Path]::GetFullPath($configSource) -ne $configTarget) { Copy-Item -LiteralPath $configSource -Destination $configTarget -Force }
Write-Output ("Built {0} ({1})" -f $OutputPath, $Platform)
