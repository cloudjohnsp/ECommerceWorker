[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipImage
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Invoke-GateStep {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    Invoke-GateStep "Restore Worker" { dotnet restore ECommerceWorker.slnx }
    Invoke-GateStep "Verify Worker formatting" {
        dotnet format ECommerceWorker.slnx --verify-no-changes --no-restore
    }
    Invoke-GateStep "Build Worker" {
        dotnet build ECommerceWorker.slnx --configuration $Configuration --no-restore
    }
    $previousIntegrationSetting = $env:RUN_POSTGRES_INTEGRATION_TESTS
    try {
        $env:RUN_POSTGRES_INTEGRATION_TESTS = "true"
        Invoke-GateStep "Test Worker with PostgreSQL" {
            dotnet test ECommerceWorker.slnx --configuration $Configuration --no-build --no-restore
        }
    }
    finally {
        $env:RUN_POSTGRES_INTEGRATION_TESTS = $previousIntegrationSetting
    }

    if (-not $SkipImage) {
        $revision = (& git rev-parse HEAD).Trim()
        Invoke-GateStep "Build Worker image" {
            docker build --label "org.opencontainers.image.revision=$revision" `
                --tag "ecommerce-worker:sha-$revision" .
        }
    }
}
finally {
    Pop-Location
}

Write-Host "Worker local gate passed."
