param([string]$SptRoot = 'C:\Games\SPT-4.1', [string]$FikaPath)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!$FikaPath) {
        $FikaPath = dotnet msbuild fika/HORIZON.Fika.csproj -nologo -getProperty:FikaPath "-p:SptRoot=$SptRoot"
        if ($LASTEXITCODE -ne 0) { throw 'Could not resolve Fika build reference' }
    }
    $FikaPath = (Resolve-Path -LiteralPath $FikaPath).Path
    dotnet build HORIZON.csproj -c Release --nologo "-p:SptRoot=$SptRoot" "-p:FikaPath=$FikaPath"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/Regression -c Release -- $SptRoot $FikaPath
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
    dotnet run --project tests/CameraHarness -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Camera bridge checks failed' }
    dotnet run --project tests/ConfigHarness -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Configuration checks failed' }
    dotnet run --project tests/FikaFeed -c Release "-p:FikaPath=$FikaPath"
    if ($LASTEXITCODE -ne 0) { throw 'Fika action sync checks failed' }
    $version = dotnet msbuild HORIZON.csproj -nologo -getProperty:Version -p:Configuration=Release
    if ($LASTEXITCODE -ne 0) { throw 'Could not read HORIZON package version' }
    $stage = Join-Path $PSScriptRoot ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    $pluginDir = Join-Path $stage 'BepInEx/plugins/HORIZON'
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    Copy-Item -LiteralPath 'bin/Release/netstandard2.1/HORIZON.dll' -Destination $pluginDir
    Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.txt' -Destination $pluginDir
    $zipName = "HORIZON-$version.zip"
    $candidate = Join-Path $stage $zipName
    Compress-Archive -LiteralPath (Join-Path $stage 'BepInEx') -DestinationPath $candidate
    dotnet run --project tests/Regression -c Release --no-build -- --package $candidate $SptRoot $FikaPath
    if ($LASTEXITCODE -ne 0) { throw 'Packaged plugin load checks failed' }
    $zip = Join-Path $PSScriptRoot "artifacts/$zipName"
    Move-Item -LiteralPath $candidate -Destination $zip -Force
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
} finally { Pop-Location }
