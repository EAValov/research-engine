param(
    [string]$ContainerName = "",
    [string]$CertOutputPath = "$PSScriptRoot\.generated\caddy-local-root.crt",
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

Ensure-Command -Name "podman"
Ensure-Command -Name "certutil"

if ([string]::IsNullOrWhiteSpace($ContainerName)) {
    foreach ($candidate in @("research-edge-caddy", "llm-stack-caddy")) {
        $isCandidateRunning = podman ps --format "{{.Names}}" | Select-String -Pattern "^$candidate$" -Quiet
        if ($isCandidateRunning) {
            $ContainerName = $candidate
            break
        }
    }
}

Write-Host "Checking container '$ContainerName'..."
$isRunning = podman ps --format "{{.Names}}" | Select-String -Pattern "^$ContainerName$" -Quiet
if (-not $isRunning) {
    throw "No supported Caddy container is running. Start either the modular stack or llm-stack first."
}

$outputDirectory = Split-Path -Path $CertOutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Write-Host "Exporting Caddy local root certificate..."
podman cp "$ContainerName`:/data/caddy/pki/authorities/local/root.crt" $CertOutputPath

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
