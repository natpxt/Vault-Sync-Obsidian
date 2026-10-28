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
    $installationCheck = Start-Process -FilePath (Join-Path $publishRoot 'VaultSync.exe') -ArgumentList @('--test-installation', ('"' + (Join-Path $buildRoot 'installation-test.json') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($installationCheck.ExitCode -ne 0) { throw 'Falha na verificação isolada do instalador' }
    if (-not $SkipUi) {
        [xml]$updaterProject = Get-Content -LiteralPath (Join-Path $projectRoot 'src/App/VaultSync.App.csproj') -Raw
        $versionText = $updaterProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        $baseVersion = [version]($versionText.Split('-')[0])
        $fixtureVersion = "$($baseVersion.Major).$($baseVersion.Minor).$($baseVersion.Build + 1)"
        $fixtureRoot = Join-Path $buildRoot 'update-fixture-candidate'
        & $Dotnet publish (Join-Path $projectRoot 'src/App/VaultSync.App.csproj') "-p:VaultSyncBuildRoot=$($buildRoot.Replace('\','/'))/update-fixture-build" "-p:Version=$fixtureVersion" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -o $fixtureRoot --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar pacote isolado de teste' }
        $updateCheck = Start-Process -FilePath (Join-Path $publishRoot 'VaultSync.exe') -ArgumentList @('--test-updates', ('"' + (Join-Path $fixtureRoot 'VaultSync.exe') + '"'), ('"' + (Join-Path $buildRoot 'update-runtime-results.json') + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($updateCheck.ExitCode -ne 0) { throw 'Falha no teste isolado de atualização e retomada' }
    }
    $dist = Join-Path $projectRoot 'dist'; New-Item -ItemType Directory -Force -Path $dist | Out-Null
    [xml]$projectInfo = Get-Content -LiteralPath (Join-Path $projectRoot 'src/App/VaultSync.App.csproj') -Raw
    $version = $projectInfo.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $output = Join-Path $dist "VaultSync-Obsidian-$version-win-x64-Setup.exe"
    Copy-Item -LiteralPath (Join-Path $publishRoot 'VaultSync.exe') -Destination $output
    $deliverables = @($output)
    if ($version -like '*-preview.*') {
        $evaluation = Join-Path $dist "VaultSync-Obsidian-$version-win-x64-Avaliacao.exe"
        Copy-Item -LiteralPath (Join-Path $publishRoot 'VaultSync.exe') -Destination $evaluation
        $deliverables += $evaluation
    }
    $checksums = foreach ($file in $deliverables) { "$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))" }
    [IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Instalador: $output"
}
