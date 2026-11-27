param([string] $OutputPath = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.x compiler is required.'
}

$output = New-Item -ItemType Directory -Path $OutputPath -Force
$executable = Join-Path $output.FullName 'PomoNoBro.exe'
$source = Join-Path $PSScriptRoot 'src'
$references = @(
    'System.Xaml.dll'
    'System.Runtime.Serialization.dll'
    'WPF\WindowsBase.dll'
    'WPF\PresentationCore.dll'
    'WPF\PresentationFramework.dll'
)
$compilerArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warnaserror+'
    '/codepage:65001'
    "/out:$executable"
    "/resource:$source\MainWindow.xaml,PomoNoBro.MainWindow.xaml"
)
$icon = Join-Path $source 'PomoNoBro.ico'
if (Test-Path -LiteralPath $icon) { $compilerArgs += "/win32icon:$icon" }
$compilerArgs += $references | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
$compilerArgs += Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName

& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "PomoNoBro build failed (exit code $LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Showcase') -Destination $output.FullName -Recurse -Force
Write-Output $executable
