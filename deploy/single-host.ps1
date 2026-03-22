<#
.SYNOPSIS
Manages the full single-host Podman deployment for Research Engine.

.DESCRIPTION
This script always manages the full local stack (app + crawl + model + edge).
If you need to exclude components, deploy manifests manually with podman commands.

.PARAMETER Action
Deployment action: up, down, restart, or status.

.EXAMPLE
.\Deploy\single-host.ps1 up

.EXAMPLE
.\Deploy\single-host.ps1 restart

.NOTES
Managed manifests (in apply order):
  app   -> 20-app.yaml
  crawl -> 30-crawl.yaml
  model -> 40-model-vllm.yaml
  edge  -> 10-edge.yaml
#>
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
            Name         = "LLM"
            PodName      = "research-LLM"
            ManifestPath = Join-Path $base "40-LLM.yaml"
        },
        [pscustomobject]@{
            Name         = "edge"
            PodName      = "research-edge"
            ManifestPath = Join-Path $base "10-edge.yaml"
        }
    )
}

function Invoke-Up {
    param([object[]]$Definitions)

    foreach ($definition in $Definitions) {
        Write-Host "Applying $(Split-Path $definition.ManifestPath -Leaf) [$($definition.Name)]..."
        podman kube play --replace $definition.ManifestPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to apply '$($definition.ManifestPath)'."
        }
    }
}

function Invoke-Down {
    param([object[]]$Definitions)

    $definitions = @($Definitions)
    [array]::Reverse($definitions)

    foreach ($definition in $definitions) {
        Write-Host "Removing $(Split-Path $definition.ManifestPath -Leaf) [$($definition.Name)]..."
        podman kube down $definition.ManifestPath | Out-Host
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

Ensure-Command -Name "podman"

$definitions = Get-ComponentDefinitions

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
}

if ($Action -eq "up" -or $Action -eq "restart") {
    $started = @($definitions.Name)

    Write-Host ""
    Write-Host "Local stack started: $($started -join ', ')."
    Write-Host "Open: https://research-webui.llm.local:8443/"
}
