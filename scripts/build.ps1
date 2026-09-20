param([switch]$Bootstrap)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Bootstrap) {
    $toolsDirectory = Join-Path $projectRoot '.tools'
    New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
    $installScript = Join-Path $toolsDirectory 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
    & $installScript -Version '10.0.401' -InstallDir (Join-Path $toolsDirectory 'dotnet') -NoPath
    if ($LASTEXITCODE) { throw 'SDK installation failed.' }
    & $sdk workload install maui-windows --skip-manifest-update --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE) { throw 'MAUI workload installation failed.' }
}
if (!(Test-Path -LiteralPath $sdk)) { throw 'Run scripts\build.ps1 -Bootstrap to install the local .NET SDK.' }
Push-Location $projectRoot
try {
    & $sdk run --project tests/SysWlan.Tests -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    $runningApp = Get-Process -Name 'SysWlan.App' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase) }
    if ($runningApp) { throw 'Windows publish cannot replace locked files. Close SysWlan.App and run scripts\\build.ps1 again.' }
    & $sdk publish src/SysWlan.App/SysWlan.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o artifacts/windows
    if ($LASTEXITCODE) { throw 'Windows publish failed.' }
    Write-Output (Join-Path $projectRoot 'artifacts\windows\SysWlan.App.exe')
} finally { Pop-Location }
