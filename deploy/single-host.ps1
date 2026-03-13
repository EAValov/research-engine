param(
    [ValidateSet("up", "down", "restart", "status")]
    [string]$Action = "up"
)

$ErrorActionPreference = "Stop"

function Ensure-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available in PATH."
    }
}

function Get-ManifestPaths {
    $base = Join-Path $PSScriptRoot "single-host"

    return @(
        (Join-Path $base "00-common.yaml"),
        (Join-Path $base "20-app.yaml"),
        (Join-Path $base "30-crawl.yaml"),
        (Join-Path $base "40-model-vllm.yaml"),
        (Join-Path $base "10-edge.yaml")
    )
}

function Invoke-Up {
    param([string[]]$ManifestPaths)

    foreach ($path in $ManifestPaths) {
        Write-Host "Applying $(Split-Path $path -Leaf)..."
        podman kube play --replace $path | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to apply '$path'."
        }
    }
}

function Invoke-Down {
    param([string[]]$ManifestPaths)

    $paths = @($ManifestPaths)
    [array]::Reverse($paths)

    foreach ($path in $paths) {
        Write-Host "Removing $(Split-Path $path -Leaf)..."
        podman kube down $path | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Unable to fully remove '$path'. It may not be deployed yet."
        }
    }
}

Ensure-Command -Name "podman"

$manifestPaths = Get-ManifestPaths

switch ($Action) {
    "up" {
        Invoke-Up -ManifestPaths $manifestPaths
    }
    "down" {
        Invoke-Down -ManifestPaths $manifestPaths
    }
    "restart" {
        Invoke-Down -ManifestPaths $manifestPaths
        Invoke-Up -ManifestPaths $manifestPaths
    }
    "status" {
        podman pod ps | Out-Host
        Write-Host ""
        podman ps --format "table {{.Names}}`t{{.Status}}`t{{.Ports}}" | Out-Host
    }
}

if ($Action -eq "up" -or $Action -eq "restart") {
    Write-Host ""
    Write-Host "Local stack started."
    Write-Host "Open: http://localhost:8080/"
}
