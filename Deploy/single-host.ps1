<#
.SYNOPSIS
Builds and manages the full single-host Podman deployment for Research Engine.

.DESCRIPTION
This script manages the full local stack (app + crawl + llm + edge).
For `up` and `restart`, it builds the local application images first, then
deploys the manifests. You can also optionally install the local Caddy
certificate into the Windows certificate store.

If you need to exclude components, deploy manifests manually with `podman`.

.PARAMETER Action
Deployment action: up, down, restart, status, or build.

.PARAMETER SkipBuild
Skips the local image build step for `up` and `restart`.

.PARAMETER InstallCaddyCertificate
After a successful `up` or `restart`, exports the local Caddy CA certificate
from the edge container and imports it into the selected Windows trust store.

.PARAMETER CertificateStoreScope
Certificate store scope used when `-InstallCaddyCertificate` is specified.
Defaults to `CurrentUser`.

.EXAMPLE
.\Deploy\single-host.ps1 up

.EXAMPLE
.\Deploy\single-host.ps1 up -InstallCaddyCertificate

.EXAMPLE
.\Deploy\single-host.ps1 restart -SkipBuild

.NOTES
Managed manifests (in apply order):
  app   -> 20-app.yaml
  crawl -> 30-crawl.yaml
  llm   -> 40-llm.yaml
  edge  -> 10-edge.yaml
#>
[CmdletBinding()]
param(
    [ValidateSet("up", "down", "restart", "status", "build")]
    [string]$Action = "up",

    [switch]$SkipBuild,

    [switch]$InstallCaddyCertificate,

    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$CertificateStoreScope = "CurrentUser"
)

$ErrorActionPreference = "Stop"
$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Ensure-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available in PATH."
    }
}

function Invoke-ExternalCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$FailureMessage
    )

    & $FilePath @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Get-ImageDefinitions {
    return @(
        [pscustomobject]@{
            Name        = "research-api"
            Tag         = "localhost/research-api:latest"
            ContextPath = Join-Path $script:RepoRoot "ResearchEngine.API"
        },
        [pscustomobject]@{
            Name        = "research-webui"
            Tag         = "localhost/research-webui:latest"
            ContextPath = Join-Path $script:RepoRoot "ResearchEngine.WebUI"
        }
    )
}

function Get-ComponentDefinitions {
    $base = Join-Path $PSScriptRoot "single-host"

    return @(
        [pscustomobject]@{
            Name         = "app"
            PodName      = "research-app"
            ManifestPath = Join-Path $base "20-app.yaml"
        },
        [pscustomobject]@{
            Name         = "crawl"
            PodName      = "research-crawl"
            ManifestPath = Join-Path $base "30-crawl.yaml"
        },
        [pscustomobject]@{
            Name         = "llm"
            PodName      = "research-llm"
            ManifestPath = Join-Path $base "40-llm.yaml"
        },
        [pscustomobject]@{
            Name         = "edge"
            PodName      = "research-edge"
            ManifestPath = Join-Path $base "10-edge.yaml"
        }
    )
}

function Invoke-Build {
    param([object[]]$Images)

    foreach ($image in $Images) {
        if (-not (Test-Path -Path $image.ContextPath)) {
            throw "Image build context '$($image.ContextPath)' does not exist."
        }

        Write-Host "Building image '$($image.Tag)' from '$($image.ContextPath)'..."
        Invoke-ExternalCommand `
            -FilePath "podman" `
            -Arguments @("build", "-t", $image.Tag, $image.ContextPath) `
            -FailureMessage "Failed to build image '$($image.Tag)'."
    }
}

function Invoke-Up {
    param([object[]]$Definitions)

    foreach ($definition in $Definitions) {
        if (-not (Test-Path -Path $definition.ManifestPath)) {
            throw "Manifest '$($definition.ManifestPath)' does not exist."
        }

        Write-Host "Applying $(Split-Path $definition.ManifestPath -Leaf) [$($definition.Name)]..."
        Invoke-ExternalCommand `
            -FilePath "podman" `
            -Arguments @("kube", "play", "--replace", $definition.ManifestPath) `
            -FailureMessage "Failed to apply '$($definition.ManifestPath)'."
    }
}

function Invoke-Down {
    param([object[]]$Definitions)

    $definitions = @($Definitions)
    [array]::Reverse($definitions)

    foreach ($definition in $definitions) {
        Write-Host "Removing $(Split-Path $definition.ManifestPath -Leaf) [$($definition.Name)]..."
        & podman kube down $definition.ManifestPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Unable to fully remove '$($definition.ManifestPath)'. It may not be deployed yet."
        }
    }
}

function Invoke-Status {
    param([object[]]$Definitions)

    $selected = @($Definitions.PodName)

    $podRows = @(podman pod ps --format "{{.Name}}`t{{.Status}}")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query pod status."
    }

    $containerRows = @(podman ps --format "{{.Names}}`t{{.Status}}`t{{.Ports}}")
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query container status."
    }

    Write-Host "Pods:"
    if ($selected.Count -eq 0) {
        Write-Host "  (none selected)"
    }
    else {
        foreach ($podName in $selected) {
            $row = $podRows | Where-Object { $_ -like "$podName`t*" } | Select-Object -First 1
            if ($null -eq $row) {
                Write-Host "  $podName`t(not running)"
            }
            else {
                Write-Host "  $row"
            }
        }
    }

    Write-Host ""
    Write-Host "Containers:"
    foreach ($podName in $selected) {
        $matched = @($containerRows | Where-Object { $_ -match [regex]::Escape($podName) })
        if ($matched.Count -eq 0) {
            Write-Host "  [$podName] none"
            continue
        }

        foreach ($row in $matched) {
            Write-Host "  $row"
        }
    }
}

function Wait-ForContainer {
    param(
        [string]$ContainerName,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $runningContainers = @(podman ps --format "{{.Names}}")
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to query running containers."
        }

        if ($runningContainers -contains $ContainerName) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Container '$ContainerName' did not become ready within $TimeoutSeconds seconds."
}

function Install-CaddyLocalCertificate {
    param([string]$StoreScope)

    $trustScript = Join-Path $PSScriptRoot "trust-caddy-local-ca.ps1"
    if (-not (Test-Path -Path $trustScript)) {
        throw "Certificate helper script '$trustScript' was not found."
    }

    Write-Host "Waiting for the edge Caddy container to be ready..."
    Wait-ForContainer -ContainerName "research-edge-caddy" -TimeoutSeconds 30

    Write-Host "Installing the local Caddy certificate into '$StoreScope'..."
    & $trustScript -StoreScope $StoreScope
}

Ensure-Command -Name "podman"

if ($InstallCaddyCertificate -and $Action -notin @("up", "restart")) {
    throw "-InstallCaddyCertificate can only be used with the 'up' or 'restart' actions."
}

if ($SkipBuild -and $Action -eq "build") {
    throw "-SkipBuild cannot be used with the 'build' action."
}

$definitions = Get-ComponentDefinitions
$images = Get-ImageDefinitions

if ($Action -in @("up", "restart", "build") -and -not $SkipBuild) {
    Invoke-Build -Images $images
}

switch ($Action) {
    "up" {
        Invoke-Up -Definitions $definitions
    }
    "down" {
        Invoke-Down -Definitions $definitions
    }
    "restart" {
        Invoke-Down -Definitions $definitions
        Invoke-Up -Definitions $definitions
    }
    "status" {
        Invoke-Status -Definitions $definitions
    }
    "build" {
        Write-Host ""
        Write-Host "Local images built:"
        foreach ($image in $images) {
            Write-Host "  $($image.Tag)"
        }
        return
    }
}

if ($Action -eq "up" -or $Action -eq "restart") {
    if ($InstallCaddyCertificate) {
        Install-CaddyLocalCertificate -StoreScope $CertificateStoreScope
    }

    $started = @($definitions.Name)

    Write-Host ""
    Write-Host "Local stack started: $($started -join ', ')."
    Write-Host "HTTP:  http://localhost:8090/"

    if ($InstallCaddyCertificate) {
        Write-Host "UI:    https://research-webui.llm.local:8443/"
        Write-Host "API:   https://research-api.llm.local:8443/"
    }
    else {
        Write-Host "Optional UI:  https://research-webui.llm.local:8443/"
        Write-Host "Optional API: https://research-api.llm.local:8443/"
        Write-Host "  To use them without browser warnings, add these entries to your hosts file:"
        Write-Host "    127.0.0.1 research-webui.llm.local"
        Write-Host "    127.0.0.1 research-api.llm.local"
        Write-Host "  and rerun with -InstallCaddyCertificate, or run .\Deploy\trust-caddy-local-ca.ps1 later."
    }
}
