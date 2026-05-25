# Requires: GitHub CLI (gh) and admin access to roger18gm/vpm
# Usage:
#   gh auth login
#   .\scripts\configure-main-branch-protection.ps1

$ErrorActionPreference = "Stop"
$Repo = "roger18gm/vpm"
$Branch = "main"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProtectionFile = Join-Path $ScriptDir "main-branch-protection.json"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  Write-Error "GitHub CLI (gh) is not installed. Run: winget install GitHub.cli"
}

$auth = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-Host "Not logged in to GitHub. Run this first:"
  Write-Host "  gh auth login"
  Write-Host ""
  Write-Host "Choose: GitHub.com, HTTPS, and authenticate in the browser."
  exit 1
}

Write-Host "Recent status checks on ${Branch} (use these names in branch protection):"
gh api "repos/$Repo/commits/$Branch/check-runs" --paginate -q ".check_runs[].name" 2>$null |
  Sort-Object -Unique |
  ForEach-Object { Write-Host "  - $_" }
Write-Host ""

Write-Host "Applying branch protection to $Branch..."
Get-Content $ProtectionFile -Raw | gh api "repos/$Repo/branches/$Branch/protection" -X PUT --input -

Write-Host ""
Write-Host "Done. Required checks:"
Write-Host "  - Backend CI / backend-test"
Write-Host "  - UI E2E / playwright"
Write-Host ""
Write-Host "Verify: https://github.com/$Repo/settings/branches"
