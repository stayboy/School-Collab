#!/usr/bin/env pwsh
# preflight-pr.ps1 — Single-command pre-flight PR workflow (PowerShell)
# Usage: .\scripts\preflight-pr.ps1 [-SkipTests] [-SkipReview] [-SkipMerge] [-MergeStrategy <squash|merge|rebase>] [-BaseBranch <branch>]
#
# Workflow:
#   1. Create feature branch (if not already on one)
#   2. Commit uncommitted changes
#   3. Push branch to origin
#   4. Run build & unit tests locally
#   5. Create PR (or update existing)
#   6. Request Copilot code review
#   7. Wait for CI to pass
#   8. Merge PR
#
# Requires: git, gh (GitHub CLI), dotnet

[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipReview,
    [switch]$SkipMerge,
    [ValidateSet("squash","merge","rebase")]
    [string]$MergeStrategy = "squash",
    [string]$BaseBranch = "main"
)

$ErrorActionPreference = "Stop"

function Step($msg)   { Write-Host "`n▶ $msg" -ForegroundColor Blue }
function Ok($msg)     { Write-Host "  ✔ $msg" -ForegroundColor Green }
function Warn($msg)   { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Fail($msg)   { Write-Host "  ✖ $msg" -ForegroundColor Red; exit 1 }

# --- Step 1: Check for uncommitted changes ---
Step "Checking working tree"
$hasChanges = $false
if (git diff --quiet 2>$null -and git diff --cached --quiet 2>$null) {
    Ok "Working tree clean"
} else {
    $hasChanges = $true
    Warn "Uncommitted changes detected"
    $branchName = "preflight/$(git branch --show-current)-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Write-Host "  Creating branch: $branchName" -ForegroundColor Yellow
    git checkout -b $branchName
    git add -A
    git commit -m "preflight: auto-commit before PR workflow"
    Ok "Changes committed to $branchName"
}

# --- Step 2: Current branch ---
$currentBranch = git branch --show-current
Step "Current branch: $currentBranch"
if ($currentBranch -eq $BaseBranch) {
    $newBranch = "feature/preflight-$(Get-Date -Format 'yyyyMMddHHmmss')"
    Warn "On $BaseBranch — creating feature branch: $newBranch"
    git checkout -b $newBranch
    $currentBranch = $newBranch
}

# --- Step 3: Push ---
Step "Pushing to origin"
try { git push -u origin $currentBranch 2>$null } catch { git push origin $currentBranch }
Ok "Pushed $currentBranch"

# --- Step 4: Local build & test ---
if (-not $SkipTests) {
    Step "Running local build & tests"
    dotnet restore --quiet 2>$null
    if (dotnet build --no-restore --configuration Release 2>$null) {
        Ok "Build succeeded"
    } else {
        Fail "Build failed — fix errors before continuing"
    }

    $testResult = dotnet test --no-build --configuration Release `
        --filter "FullyQualifiedName~Tests.Unit" `
        --logger "trx;LogFileName=preflight-unit.trx" `
        --results-directory ./test-results 2>$null
    if ($LASTEXITCODE -eq 0) {
        Ok "Unit tests passed"
    } else {
        Fail "Unit tests failed — fix before continuing"
    }
} else {
    Step "Skipping local build & tests (-SkipTests)"
}

# --- Step 5: Create or update PR ---
Step "Creating / updating PR"
$prNumber = (gh pr list --head $currentBranch --json number --jq ".[0].number" 2>$null) ?? ""

if ($prNumber -and $prNumber -ne "") {
    Ok "PR #$prNumber already exists"
} else {
    $title = $currentBranch -replace "^feature/", "" -replace "^preflight/", "" -replace "-", " "
    $title = (Get-Culture).TextInfo.ToTitleCase($title.ToLower())
    $prNumber = gh pr create `
        --base $BaseBranch `
        --title $title `
        --body "Auto-generated PR from preflight workflow for branch ``$currentBranch``." `
        --json number --jq ".number"
    Ok "Created PR #$prNumber"
}

# --- Step 6: Copilot code review ---
if (-not $SkipReview) {
    Step "Requesting Copilot code review"
    try {
        gh api repos/{owner}/{repo}/pulls/$prNumber/requested_reviewers `
            --method POST `
            --field reviewers='["copilot"]' 2>$null | Out-Null
        Ok "Copilot review requested"
    } catch {
        Warn "Copilot review may not be available — continuing anyway"
    }
} else {
    Step "Skipping Copilot review (-SkipReview)"
}

# --- Step 7: Wait for CI ---
Step "Waiting for CI checks on PR #$prNumber"
Write-Host "  Polling every 30s for CI completion..."

$maxWait = 20
$attempts = 0
$ciPassed = $false
while ($attempts -lt $maxWait) {
    $checksOutput = gh pr checks $prNumber 2>$null
    if ($checksOutput -match "fail|cancel") {
        Fail "CI checks failed — review and fix before merging"
    }
    if ($checksOutput -match "pass") {
        Ok "CI checks passed"
        $ciPassed = $true
        break
    }
    $attempts++
    Write-Host "  ...waiting (attempt $attempts/$maxWait)"
    Start-Sleep -Seconds 30
}

if (-not $ciPassed -and $attempts -ge $maxWait) {
    Warn "Timed out waiting for CI — check manually with: gh pr checks $prNumber"
}

# --- Step 8: Merge ---
if (-not $SkipMerge) {
    Step "Merging PR #$prNumber"
    gh pr merge $prNumber --$MergeStrategy --delete-branch
    Ok "PR #$prNumber merged ($MergeStrategy)"

    git checkout $BaseBranch
    git pull origin $BaseBranch
    Ok "Pulled $BaseBranch"
} else {
    Step "Skipping merge (-SkipMerge)"
    Write-Host "  PR #$prNumber is ready for manual merge: gh pr merge $prNumber --$MergeStrategy"
}

Write-Host "`n✅ Preflight PR workflow complete!" -ForegroundColor Green