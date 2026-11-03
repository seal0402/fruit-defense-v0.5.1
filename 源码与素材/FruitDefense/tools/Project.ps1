[CmdletBinding()]
param(
    [ValidateSet('Setup','Doctor','Build','Play','Editor','Test','Import')]
    [string]$Action = 'Doctor',
    [switch]$RulesOnly,
    [switch]$Download
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = Join-Path $projectRoot 'game'
$localRoot = Join-Path $projectRoot '.local'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'platform-manifest.json') -Raw | ConvertFrom-Json

function Setup-Windows {
    if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitOperatingSystem) {
        throw 'Use Python setup.py for Linux/macOS. The bundled offline tools are Windows x64.'
    }
    $profile = $manifest.platforms.'win-x64'
    foreach ($name in @('dotnet','godot')) {
        $component = $profile.$name
        $destination = Join-Path $PSScriptRoot $name
        $marker = Join-Path $destination '.fruitdefense-ready'
        if (Test-Path -LiteralPath $marker) { continue }
        if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force).Count -gt 0) {
            throw "Tool directory is not empty and has no setup marker: $destination. Inspect it before retrying."
        }
        $offline = Join-Path (Split-Path -Parent $projectRoot) ('offline-tools\' + $component.name)
        $downloaded = Join-Path $PSScriptRoot ('downloads\' + $component.name)
        $archive = if (Test-Path -LiteralPath $offline) { $offline } elseif (Test-Path -LiteralPath $downloaded) { $downloaded } else { $null }
        if (!$archive) {
            if (!$Download) { throw "Archive missing: $($component.name). Use the Windows offline bundle, or run Setup -Download." }
            New-Item -ItemType Directory -Path (Split-Path -Parent $downloaded) -Force | Out-Null
            Write-Host "Downloading official $name archive..."
            Invoke-WebRequest -Uri $component.url -OutFile $downloaded -UseBasicParsing
            $archive = $downloaded
        }
        Write-Host "Verifying SHA512: $($component.name)"
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $component.sha512) {
            throw "Archive SHA512 mismatch: $archive"
        }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Expand-Archive -LiteralPath $archive -DestinationPath $destination
        Set-Content -LiteralPath $marker -Value $manifest.metadataDate -Encoding ASCII
    }
}
function Resolve-Dotnet {
    $local = Join-Path $PSScriptRoot 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    if ($env:FRUITDEFENSE_DOTNET -and (Test-Path -LiteralPath $env:FRUITDEFENSE_DOTNET)) { return $env:FRUITDEFENSE_DOTNET }
    if ($env:FRUITDEFENSE_PORTABLE_ONLY -ne '1' -and (Test-Path -LiteralPath 'D:\GodotDev\dotnet\dotnet.exe')) {
        return 'D:\GodotDev\dotnet\dotnet.exe'
    }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw '.NET SDK not found. Run Setup first. A runtime alone cannot build this project.'
}
function Resolve-Godot {
    $folder = Join-Path $PSScriptRoot 'godot'
    if (Test-Path -LiteralPath $folder) {
        $binary = Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*mono_win64.exe' | Select-Object -First 1
        if ($binary) { return $binary.FullName }
    }
    if ($env:FRUITDEFENSE_GODOT -and (Test-Path -LiteralPath $env:FRUITDEFENSE_GODOT)) { return $env:FRUITDEFENSE_GODOT }
    $legacy = 'D:\GodotDev\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
    if ($env:FRUITDEFENSE_PORTABLE_ONLY -ne '1' -and (Test-Path -LiteralPath $legacy)) { return $legacy }
    foreach ($name in @('godot-mono','godot4','godot')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    throw 'Godot .NET not found. Run Setup or set FRUITDEFENSE_GODOT to the executable path.'
}
function Run-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE : $Program" }
}
function Run-Godot([string]$Program, [string[]]$Arguments, [switch]$ShowWindow) {
    # Windows PowerShell can return early for GUI executables. Wait for the process tree explicitly.
    $quoted = @($Arguments | ForEach-Object { '"' + $_.Replace('"','\"') + '"' })
    $stdout = Join-Path $localRoot ('godot-' + $Action + '.log')
    $stderr = Join-Path $localRoot ('godot-' + $Action + '-error.log')
    $window = if ($ShowWindow) { 'Normal' } else { 'Hidden' }
    $process = Start-Process -FilePath $Program -ArgumentList $quoted -WindowStyle $window -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Get-Content -LiteralPath $stdout,$stderr -Encoding UTF8
    if ($process.ExitCode -ne 0) { throw "Godot failed with exit code $($process.ExitCode). See $stderr" }
}
try {
    if ($Action -eq 'Setup') { Setup-Windows }
    $dotnet = Resolve-Dotnet
    New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:DOTNET_CLI_HOME = Join-Path $localRoot 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $localRoot 'nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    Push-Location $gameRoot
    try {
        $version = (& $dotnet --version | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $version -notmatch '^10\.0\.4\d\d$') {
            throw "Expected .NET SDK 10.0.402 or a compatible 10.0.4xx patch; detected $version"
        }
        Write-Host "Project: $projectRoot"
        Write-Host "Dotnet: $dotnet ($version)"
        Write-Host "NuGet: $env:NUGET_PACKAGES"
        $needsGodot = $Action -in @('Setup','Doctor','Play','Editor','Import') -or ($Action -eq 'Test' -and !$RulesOnly)
        if ($needsGodot) {
            $godot = Resolve-Godot
            $console = [IO.Path]::ChangeExtension($godot, $null) + '_console.exe'
            if (!(Test-Path -LiteralPath $console)) { $console = $godot }
            $engineVersion = (& $console --version | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $engineVersion -notmatch '^4\.7\.2.*mono') {
                throw "Expected Godot .NET 4.7.2; detected $engineVersion"
            }
            Write-Host "Godot: $godot ($engineVersion)"
        }
        if ($Action -in @('Build','Play','Test')) {
            Run-Checked $dotnet @('build',(Join-Path $gameRoot 'FruitDefense.csproj'),'--configuration','Debug','--nologo','--verbosity','minimal')
        }
        if ($Action -eq 'Play') { Run-Godot $godot @('--path',$gameRoot) -ShowWindow }
        if ($Action -eq 'Editor') { Run-Godot $godot @('--editor','--path',$gameRoot) -ShowWindow }
        if ($Action -eq 'Import') { Run-Godot $console @('--headless','--editor','--path',$gameRoot,'--import') }
        if ($Action -eq 'Test') {
            $reports = Join-Path $projectRoot '06_预览与测试\试玩记录'
            Run-Checked $dotnet @('run','--project',(Join-Path $projectRoot 'tests\DomainChecks\DomainChecks.csproj'),'--',
                (Join-Path $gameRoot 'config\prototype.json'),(Join-Path $reports 'domain-checks.json'))
            if (!$RulesOnly) {
                Run-Godot $console @('--path',$gameRoot,'--resolution','450x800','--',
                    ('--integration=' + (Join-Path $reports 'ui-checks.json')))
            }
        }
        Write-Host "Completed: $Action"
    }
    finally { Pop-Location }
}
catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
