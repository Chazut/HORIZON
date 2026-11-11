param([string]$SptRoot = 'C:\Games\SPT-4.1')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build RaidSpectator.csproj -c Release --nologo "-p:SptRoot=$SptRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/Regression -c Release -- $SptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
    dotnet run --project tests/CameraHarness -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Camera bridge checks failed' }
    $version = ([xml](Get-Content -Raw RaidSpectator.csproj)).Project.PropertyGroup.Version
    $stage = Join-Path $PSScriptRoot ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    $pluginDir = Join-Path $stage 'BepInEx/plugins/RaidSpectator'
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    Copy-Item -LiteralPath 'bin/Release/netstandard2.1/RaidSpectator.dll' -Destination $pluginDir
    $zip = Join-Path $PSScriptRoot "artifacts/RaidSpectator-$version-test.zip"
    Compress-Archive -LiteralPath (Join-Path $stage 'BepInEx') -DestinationPath $zip -Force
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
} finally { Pop-Location }
