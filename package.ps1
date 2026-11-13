param([string]$SptRoot = 'C:\Games\SPT-4.1')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build HORIZON.csproj -c Release --nologo "-p:SptRoot=$SptRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/Regression -c Release -- $SptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
    dotnet run --project tests/CameraHarness -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Camera bridge checks failed' }
    dotnet run --project tests/ConfigHarness -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Configuration checks failed' }
    $version = ([xml](Get-Content -Raw HORIZON.csproj)).Project.PropertyGroup.Version
    $stage = Join-Path $PSScriptRoot ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    $pluginDir = Join-Path $stage 'BepInEx/plugins/HORIZON'
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    Copy-Item -LiteralPath 'bin/Release/netstandard2.1/HORIZON.dll' -Destination $pluginDir
    $zip = Join-Path $PSScriptRoot "artifacts/HORIZON-$version.zip"
    Compress-Archive -LiteralPath (Join-Path $stage 'BepInEx') -DestinationPath $zip -Force
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
} finally { Pop-Location }
