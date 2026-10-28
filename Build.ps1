param
(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler))
{
    throw '.NET Framework 4.8 compiler is required on a 64-bit Windows system.'
}

$artifactDir = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$app = Join-Path $artifactDir 'FrameTools.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\FrameTools') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$manifest = Join-Path $projectRoot 'src\FrameTools\app.manifest'
& $compiler /nologo /codepage:65001 /langversion:5 /warn:4 /warnaserror /optimize+ /target:winexe /platform:anycpu "/out:$app" "/win32manifest:$manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0)
{
    throw 'Application compilation failed.'
}
Write-Output "Built: $app"

if ($Test)
{
    $testApp = Join-Path $artifactDir 'FrameTools.Tests.exe'
    $testSource = Join-Path $projectRoot 'tests\ProtocolTests.cs'
    & $compiler /nologo /codepage:65001 /langversion:5 /warn:4 /warnaserror /target:exe "/out:$testApp" "/reference:$app" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $testSource
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Test compilation failed.'
    }
    & $testApp
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Protocol tests failed.'
    }
}
