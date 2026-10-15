param([string]$Dotnet = 'dotnet', [switch]$Publish, [switch]$SkipUi)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$buildRoot = Join-Path $env:LOCALAPPDATA 'VaultSyncBuild'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Dotnet -eq 'dotnet' -and -not ((& $Dotnet --list-sdks) -match '^8\.')) {
    $localSdk = Join-Path $env:LOCALAPPDATA 'MegaBrainSyncBuild/dotnet/dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { $Dotnet = $localSdk }
}
$props = "-p:VaultSyncBuildRoot=$($buildRoot.Replace('\','/'))"
& $Dotnet build (Join-Path $projectRoot 'src/App/VaultSync.App.csproj') $props -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação' }
& $Dotnet build (Join-Path $projectRoot 'tests/VaultSync.Tests.csproj') $props -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação dos testes' }
& $Dotnet (Join-Path $buildRoot 'bin/VaultSync.Tests/Release/net8.0-windows/VaultSync.Tests.dll') (Join-Path $buildRoot 'test-results.json')
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes' }
if (-not $SkipUi) {
    $appDll = Join-Path $buildRoot 'bin/VaultSync.App/Release/net8.0-windows/VaultSync.dll'
    & $Dotnet $appDll --self-test (Join-Path $buildRoot 'ui-smoke')
    if ($LASTEXITCODE -ne 0) { throw 'Falha no smoke test da interface' }
}
if ($Publish) {
    $publishRoot = Join-Path $buildRoot 'publish/win-x64'
    & $Dotnet publish (Join-Path $projectRoot 'src/App/VaultSync.App.csproj') $props -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -o $publishRoot --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar' }
    $dist = Join-Path $projectRoot 'dist'; New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $output = Join-Path $dist 'VaultSync-Obsidian-0.1.0-win-x64-Setup.exe'
    Copy-Item -LiteralPath (Join-Path $publishRoot 'VaultSync.exe') -Destination $output
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($output))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Instalador: $output"
}
