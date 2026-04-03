<#
.SYNOPSIS
Prepares and publishes a tagged GitHub release for Research Engine.

.DESCRIPTION
This script updates CITATION.cff to match the release version and date, runs
basic verification, shows the full release summary for review, and asks for
confirmation before it creates the release commit, tag, push, and GitHub
release.

.PARAMETER Version
Release version. Accepts either 1.2.3 or v1.2.3. The Git tag will always use
the v-prefix, while CITATION.cff stores the bare version.

.PARAMETER DateReleased
Release date written to CITATION.cff in yyyy-MM-dd format. Defaults to today.

.PARAMETER Title
GitHub release title. Defaults to "Research Engine v<version>".

.PARAMETER Notes
Optional maintainer notes prepended to the auto-generated GitHub release notes.

.PARAMETER Draft
Creates the GitHub release as a draft.

.PARAMETER PreRelease
Marks the GitHub release as a prerelease.

.PARAMETER SkipChecks
Skips dotnet build and dotnet test. Use sparingly.

.PARAMETER PreviewOnly
Shows the release summary and exits without changing files or running git/gh
release commands.

.EXAMPLE
.\Deploy\release.ps1 -Version 1.0.0

.EXAMPLE
.\Deploy\release.ps1 -Version 1.1.0 -Draft

.EXAMPLE
.\Deploy\release.ps1 -Version v1.2.0 -Notes "First public beta." -PreRelease
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string]$DateReleased = (Get-Date -Format 'yyyy-MM-dd'),

    [string]$Title,

    [string]$Notes,

    [switch]$Draft,

    [switch]$PreRelease,

    [switch]$SkipChecks,

    [switch]$PreviewOnly
)

$ErrorActionPreference = "Stop"
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:CitationPath = Join-Path $script:RepoRoot "CITATION.cff"
$script:NormalizedVersion = $Version.Trim()
$script:NormalizedVersion = $script:NormalizedVersion -replace '^v', ''
$script:TagName = "v$script:NormalizedVersion"

if ([string]::IsNullOrWhiteSpace($Title)) {
    $Title = "Research Engine $script:TagName"
}

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

function Invoke-GitCapture {
    param(
        [string[]]$Arguments,
        [string]$FailureMessage
    )

    $result = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }

    return ($result | Out-String).Trim()
}

function Get-CitationMetadata {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Citation file '$Path' does not exist."
    }

    $content = Get-Content -LiteralPath $Path -Raw
    $versionMatch = [regex]::Match($content, '(?m)^version:\s*"(?<value>[^"]+)"\s*$')
    $dateMatch = [regex]::Match($content, '(?m)^date-released:\s*(?<value>\d{4}-\d{2}-\d{2})\s*$')

    if (-not $versionMatch.Success) {
        throw "Could not find a top-level 'version' field in '$Path'."
    }

    if (-not $dateMatch.Success) {
        throw "Could not find a top-level 'date-released' field in '$Path'."
    }

    return [pscustomobject]@{
        Content      = $content
        Version      = $versionMatch.Groups['value'].Value
        DateReleased = $dateMatch.Groups['value'].Value
    }
}

function Get-UpdatedCitationContent {
    param(
        [string]$Content,
        [string]$VersionValue,
        [string]$DateValue
    )

    $updated = [regex]::Replace($Content, '(?m)^version:\s*"[^"]+"\s*$', "version: `"$VersionValue`"")
    $updated = [regex]::Replace($updated, '(?m)^date-released:\s*\d{4}-\d{2}-\d{2}\s*$', "date-released: $DateValue")

    return $updated
}

function Set-Utf8NoBomFileContent {
    param(
        [string]$Path,
        [string]$Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Assert-CleanWorktree {
    $status = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to inspect git status."
    }

    if ($status.Count -gt 0) {
        throw "Working tree is not clean. Commit, stash, or remove local changes before running the release script."
    }
}

function Assert-OnMainBranch {
    $branch = Invoke-GitCapture -Arguments @("branch", "--show-current") -FailureMessage "Failed to determine the current branch."
    if ($branch -ne "main") {
        throw "Releases must be created from 'main'. Current branch: '$branch'."
    }
}

function Assert-RemoteConfigured {
    $remote = Invoke-GitCapture -Arguments @("remote", "get-url", "origin") -FailureMessage "Git remote 'origin' is not configured."
    if ([string]::IsNullOrWhiteSpace($remote)) {
        throw "Git remote 'origin' is not configured."
    }
}

function Assert-GitHubAuth {
    Invoke-ExternalCommand `
        -FilePath "gh" `
        -Arguments @("auth", "status") `
        -FailureMessage "GitHub CLI is not authenticated. Run 'gh auth login' first."
}

function Assert-TagDoesNotExist {
    param([string]$TagName)

    & git rev-parse --verify --quiet "refs/tags/$TagName" *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$TagName' already exists locally."
    }
}

function Assert-MainTracksOrigin {
    Invoke-ExternalCommand `
        -FilePath "git" `
        -Arguments @("fetch", "origin", "main", "--tags") `
        -FailureMessage "Failed to fetch origin/main and tags."

    $headSha = Invoke-GitCapture -Arguments @("rev-parse", "HEAD") -FailureMessage "Failed to resolve HEAD."
    $originSha = Invoke-GitCapture -Arguments @("rev-parse", "origin/main") -FailureMessage "Failed to resolve origin/main."

    if ($headSha -ne $originSha) {
        throw "Local 'main' does not match 'origin/main'. Pull and review the branch before releasing."
    }
}

function Show-ReleaseSummary {
    param(
        [pscustomobject]$CurrentCitation,
        [string]$VersionValue,
        [string]$DateValue,
        [string]$TagName,
        [string]$Title,
        [bool]$Draft,
        [bool]$PreRelease,
        [bool]$SkipChecks
    )

    Write-Host ""
    Write-Host "Release summary"
    Write-Host "---------------"
    Write-Host "Repository   : $script:RepoRoot"
    Write-Host "Branch       : main"
    Write-Host "CITATION old : version=$($CurrentCitation.Version), date-released=$($CurrentCitation.DateReleased)"
    Write-Host "CITATION new : version=$VersionValue, date-released=$DateValue"
    Write-Host "Git tag      : $TagName"
    Write-Host "Release title: $Title"
    Write-Host "Draft        : $Draft"
    Write-Host "Prerelease   : $PreRelease"
    Write-Host "Run checks   : $(-not $SkipChecks)"
    if (-not [string]::IsNullOrWhiteSpace($Notes)) {
        Write-Host "Notes        : $Notes"
    }

    Write-Host ""
    Write-Host "Planned actions"
    Write-Host "---------------"
    if (-not $SkipChecks) {
        Write-Host "1. dotnet build ResearchEngine.slnx"
        Write-Host "2. dotnet test ResearchEngine.IntegrationTests/ResearchEngine.IntegrationTests.csproj"
        Write-Host "3. Update CITATION.cff"
        Write-Host "4. Commit CITATION.cff"
        Write-Host "5. Create annotated tag $TagName"
        Write-Host "6. Push main"
        Write-Host "7. Push tag $TagName"
        Write-Host "8. Create GitHub release with gh"
    }
    else {
        Write-Host "1. Update CITATION.cff"
        Write-Host "2. Commit CITATION.cff"
        Write-Host "3. Create annotated tag $TagName"
        Write-Host "4. Push main"
        Write-Host "5. Push tag $TagName"
        Write-Host "6. Create GitHub release with gh"
    }
}

Ensure-Command -Name "git"
Ensure-Command -Name "gh"
Ensure-Command -Name "dotnet"

Set-Location -LiteralPath $script:RepoRoot

Assert-CleanWorktree
Assert-OnMainBranch
Assert-RemoteConfigured
Assert-GitHubAuth
Assert-TagDoesNotExist -TagName $script:TagName
Assert-MainTracksOrigin

$currentCitation = Get-CitationMetadata -Path $script:CitationPath
$updatedCitationContent = Get-UpdatedCitationContent `
    -Content $currentCitation.Content `
    -VersionValue $script:NormalizedVersion `
    -DateValue $DateReleased

if (-not $SkipChecks) {
    Write-Host "Running release checks..."
    Invoke-ExternalCommand `
        -FilePath "dotnet" `
        -Arguments @("build", "ResearchEngine.slnx") `
        -FailureMessage "dotnet build failed."

    Invoke-ExternalCommand `
        -FilePath "dotnet" `
        -Arguments @("test", "ResearchEngine.IntegrationTests/ResearchEngine.IntegrationTests.csproj") `
        -FailureMessage "dotnet test failed."
}

Assert-CleanWorktree

Show-ReleaseSummary `
    -CurrentCitation $currentCitation `
    -VersionValue $script:NormalizedVersion `
    -DateValue $DateReleased `
    -TagName $script:TagName `
    -Title $Title `
    -Draft:$Draft `
    -PreRelease:$PreRelease `
    -SkipChecks:$SkipChecks

if ($PreviewOnly) {
    Write-Host ""
    Write-Host "Preview only. No files were changed and nothing was pushed."
    exit 0
}

Write-Host ""
$confirmation = Read-Host "Proceed with this release? [y/N]"
if ($confirmation -notmatch '^(?i:y|yes)$') {
    Write-Host "Release cancelled."
    exit 0
}

Set-Utf8NoBomFileContent -Path $script:CitationPath -Content $updatedCitationContent

Invoke-ExternalCommand `
    -FilePath "git" `
    -Arguments @("add", "--", "CITATION.cff") `
    -FailureMessage "Failed to stage CITATION.cff."

Invoke-ExternalCommand `
    -FilePath "git" `
    -Arguments @("commit", "-m", "Prepare release $script:TagName") `
    -FailureMessage "Failed to create the release commit."

Invoke-ExternalCommand `
    -FilePath "git" `
    -Arguments @("tag", "-a", $script:TagName, "-m", "Release $script:TagName") `
    -FailureMessage "Failed to create tag '$script:TagName'."

Invoke-ExternalCommand `
    -FilePath "git" `
    -Arguments @("push", "origin", "main") `
    -FailureMessage "Failed to push 'main' to origin."

Invoke-ExternalCommand `
    -FilePath "git" `
    -Arguments @("push", "origin", $script:TagName) `
    -FailureMessage "Failed to push tag '$script:TagName' to origin."

$releaseArguments = @(
    "release", "create", $script:TagName,
    "--verify-tag",
    "--generate-notes",
    "--title", $Title
)

if ($Draft) {
    $releaseArguments += "--draft"
}

if ($PreRelease) {
    $releaseArguments += "--prerelease"
}

if (-not [string]::IsNullOrWhiteSpace($Notes)) {
    $releaseArguments += @("--notes", $Notes)
}

Invoke-ExternalCommand `
    -FilePath "gh" `
    -Arguments $releaseArguments `
    -FailureMessage "Failed to create the GitHub release."

Write-Host ""
Write-Host "Release complete."
Write-Host "Tag: $script:TagName"
Write-Host "Title: $Title"
