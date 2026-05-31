param(
    [string]$ContainerName = "",
    [string]$CertOutputPath = "$PSScriptRoot\.generated\caddy-local-root.crt",
    [ValidateSet("podman", "docker")]
    [string]$ContainerRuntime = "podman",
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$StoreScope = "CurrentUser"
)

$ErrorActionPreference = "Stop"

function Ensure-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available in PATH."
    }
}

Ensure-Command -Name $ContainerRuntime
Ensure-Command -Name "certutil"

if ([string]::IsNullOrWhiteSpace($ContainerName)) {
    foreach ($candidate in & $ContainerRuntime ps --format "{{.Names}}") {
        $labelsJson = & $ContainerRuntime inspect $candidate --format "{{json .Config.Labels}}" 2>$null
        $mountsJson = & $ContainerRuntime inspect $candidate --format "{{json .Mounts}}" 2>$null
        if ([string]::IsNullOrWhiteSpace($labelsJson)) {
            continue
        }

        $labels = $labelsJson | ConvertFrom-Json
        $mounts = if ([string]::IsNullOrWhiteSpace($mountsJson)) { @() } else { $mountsJson | ConvertFrom-Json }
        $imageTitle = $labels."org.opencontainers.image.title"
        $envLabel = $labels."com.microsoft.developer.usvc-dev.env"
        $mountsLabel = $labels."com.microsoft.developer.usvc-dev.mountsLabel"
        $composeService = $labels."com.docker.compose.service"
        $podmanComposeService = $labels."io.podman.compose.service"
        $hasCaddyDataMount = @($mounts | Where-Object {
            $_.Destination -eq "/data" -and $_.Name -match "(^|_)research-edge-caddy-data$"
        }).Count -gt 0

        if ($imageTitle -eq "Caddy" -and
            (($envLabel -match "(^|\n)EDGE_HTTPS_PORT(\n|$)" -and
              $mountsLabel -match "research-edge-caddy-data") -or
             $composeService -eq "research-edge" -or
             $podmanComposeService -eq "research-edge" -or
             $hasCaddyDataMount)) {
            $ContainerName = $candidate
            break
        }
    }
}

Write-Host "Checking container '$ContainerName'..."
$isRunning = & $ContainerRuntime ps --format "{{.Names}}" | Select-String -Pattern "^$ContainerName$" -Quiet
if (-not $isRunning) {
    throw "No supported Caddy container is running. Start the Aspire or compose edge first."
}

$outputDirectory = Split-Path -Path $CertOutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Write-Host "Exporting Caddy local root certificate..."
& $ContainerRuntime cp "$ContainerName`:/data/caddy/pki/authorities/local/root.crt" $CertOutputPath

if (-not (Test-Path -Path $CertOutputPath)) {
    throw "Failed to export certificate to '$CertOutputPath'."
}

if ($StoreScope -eq "CurrentUser") {
    Write-Host "Importing certificate into CurrentUser\\Root..."
    certutil -user -addstore Root $CertOutputPath | Out-Host
} else {
    Write-Host "Importing certificate into LocalMachine\\Root (requires elevated shell)..."
    certutil -addstore Root $CertOutputPath | Out-Host
}

Write-Host ""
Write-Host "Done. Restart your browser and open:"
Write-Host "  https://research-webui.llm.local:8443/"
Write-Host "API endpoint:"
Write-Host "  https://research-api.llm.local:8443/"
