param(
    [Parameter(Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.+].*)?$')]
    [string]$Version = '0.1.0',

    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Quick.csproj'
$publishDir = Join-Path $root 'release\Quick-installer-publish'
$outputDir = Join-Path $root 'release\Quick-installer'
$iconPath = Join-Path $root 'Assets\Quick.ico'

foreach ($directory in @($publishDir, $outputDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
}

dotnet publish $project --configuration Release -p:PublishProfile=Installer "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

dnx vpk@1.2.0 pack `
    --packId Quick `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe Quick.exe `
    --outputDir $outputDir `
    --packTitle Quick `
    --icon $iconPath
if ($LASTEXITCODE -ne 0) {
    throw 'Velopack packaging failed.'
}

$requiredFiles = @(
    'Quick-win-Setup.exe',
    'RELEASES',
    'releases.win.json'
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputDir $file))) {
        throw "Velopack output is incomplete. Missing $file."
    }
}

if ($SmokeTest) {
    $smokeDir = Join-Path ([IO.Path]::GetTempPath()) "Quick-installer-smoke-$Version-$([Guid]::NewGuid())"
    $setupPath = Join-Path $outputDir 'Quick-win-Setup.exe'
    $installedExe = Join-Path $smokeDir 'current\Quick.exe'
    $installedRuntime = Join-Path $smokeDir 'current\Quick.runtimeconfig.json'
    $updatePath = Join-Path $smokeDir 'Update.exe'

    function Wait-ForPath {
        param(
            [Parameter(Mandatory)]
            [string]$Path,

            [int]$TimeoutSeconds = 30
        )

        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not (Test-Path -LiteralPath $Path)) {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for $Path."
            }

            Start-Sleep -Milliseconds 250
        }
    }

    function Invoke-CheckedProcess {
        param(
            [Parameter(Mandatory)]
            [string]$FilePath,

            [Parameter(Mandatory)]
            [string[]]$ArgumentList
        )

        $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            throw "$FilePath failed with exit code $($process.ExitCode)."
        }
    }

    try {
        Invoke-CheckedProcess $setupPath @('--silent', '--installto', $smokeDir)
        Wait-ForPath $installedExe
        Wait-ForPath $installedRuntime
        Wait-ForPath $updatePath

        Invoke-CheckedProcess $installedExe @('--self-test')
        Invoke-CheckedProcess $updatePath @('uninstall', '--silent')

        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (Test-Path -LiteralPath $smokeDir) {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for the test installation to uninstall."
            }

            Start-Sleep -Milliseconds 250
        }

        Write-Host 'Installer install, self-check, and uninstall smoke test passed.'
    }
    finally {
        if (Test-Path -LiteralPath $smokeDir) {
            Remove-Item -LiteralPath $smokeDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "Installer output: $outputDir"
