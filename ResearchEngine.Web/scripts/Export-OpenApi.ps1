param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile,

    [string]$DocumentName = "v1",
    [string]$ServerUrl = "http://localhost:8090/",
    [int]$StartupTimeoutSeconds = 30,
    [string]$BuildConfiguration = "Debug",
    [switch]$UseLocalDotnetCliHome
)

$ErrorActionPreference = "Stop"

$projectDirPath = (Resolve-Path $ProjectDir).Path
if ([System.IO.Path]::IsPathRooted($OutputFile)) {
    $outputFilePath = [System.IO.Path]::GetFullPath($OutputFile)
}
else {
    $outputFilePath = [System.IO.Path]::GetFullPath((Join-Path $projectDirPath $OutputFile))
}

$outputDirectory = Split-Path -Path $outputFilePath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path $outputDirectory)) {
    New-Item -Path $outputDirectory -ItemType Directory -Force | Out-Null
}

if ($UseLocalDotnetCliHome) {
    $dotnetCliHome = Join-Path $projectDirPath ".dotnet"
    if (-not (Test-Path $dotnetCliHome)) {
        New-Item -Path $dotnetCliHome -ItemType Directory -Force | Out-Null
    }

    $env:DOTNET_CLI_HOME = $dotnetCliHome
}
$env:ASPNETCORE_ENVIRONMENT = "Testing"
$env:IpRateLimiting__Enabled = "false"

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()

$baseUrl = "http://127.0.0.1:$port"
$env:ASPNETCORE_URLS = $baseUrl

$projectFilePath = Join-Path $projectDirPath "ResearchEngine.Web.csproj"
$stdoutLog = [System.IO.Path]::GetTempFileName()
$stderrLog = [System.IO.Path]::GetTempFileName()

$process = $null
try {
    $process = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList @("run", "--project", $projectFilePath, "--configuration", $BuildConfiguration, "--no-build", "--no-restore", "--no-launch-profile") `
        -WorkingDirectory $projectDirPath `
        -PassThru `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog

    $openApiUri = "$baseUrl/openapi/$DocumentName.yaml"
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $downloaded = $false

    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            break
        }

        try {
            Invoke-WebRequest -Uri $openApiUri -TimeoutSec 2 -OutFile $outputFilePath
            $downloaded = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $downloaded) {
        $stdoutText = if (Test-Path $stdoutLog) { Get-Content -Raw $stdoutLog } else { "" }
        $stderrText = if (Test-Path $stderrLog) { Get-Content -Raw $stderrLog } else { "" }
        throw "OpenAPI export failed for '$openApiUri' within $StartupTimeoutSeconds seconds.`nSTDOUT:`n$stdoutText`nSTDERR:`n$stderrText"
    }

    if (-not [string]::IsNullOrWhiteSpace($ServerUrl)) {
        $normalizedServerUrl = $ServerUrl.Trim()
        if (-not $normalizedServerUrl.EndsWith("/")) {
            $normalizedServerUrl += "/"
        }

        $openApiText = Get-Content -Raw -Path $outputFilePath
        $openApiText = [System.Text.RegularExpressions.Regex]::Replace(
            $openApiText,
            "(?m)^\s*-\surl:\s*http://127\.0\.0\.1:\d+/\s*$",
            "  - url: $normalizedServerUrl",
            1)

        [System.IO.File]::WriteAllText(
            $outputFilePath,
            $openApiText,
            [System.Text.UTF8Encoding]::new($false))
    }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }

    if (Test-Path $stdoutLog) {
        Remove-Item -Path $stdoutLog -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path $stderrLog) {
        Remove-Item -Path $stderrLog -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Exported OpenAPI document to '$outputFilePath'."
