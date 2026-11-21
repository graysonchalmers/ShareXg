# Sync-Upstream.ps1 - pull new ShareX (upstream) commits into a sync/<date> branch, build, report.
# Never deploys and never touches main. Review the branch, then merge it to main yourself.
# Usage (Legion PowerShell): powershell -NoProfile -File C:\Projects-local\Tool-ShareX-Grayson\scripts\Sync-Upstream.ps1
param([string]$UpstreamBranch = 'develop')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

if (-not (git remote | Select-String -Quiet '^upstream$')) {
    git remote add upstream https://github.com/ShareX/ShareX.git
}
if (git status --porcelain) { Write-Host "Working tree is dirty; commit or stash first."; exit 2 }

git fetch upstream --tags --quiet
$up = "upstream/$UpstreamBranch"
$base = git merge-base HEAD $up
$counts = (git rev-list --left-right --count "$up...HEAD") -split '\s+'
Write-Host "Behind upstream: $($counts[0]) commits.  Ahead (ShareXg changes): $($counts[1]) commits."
if ([int]$counts[0] -eq 0) { Write-Host "Already in parity. Nothing to do."; exit 0 }

Write-Host "`nNew upstream commits:"
git log --oneline --no-merges "$base..$up" | Select-Object -First 40
$tags = git tag --contains $base --merged $up | Where-Object { $_ -match '^v\d' }
if ($tags) { Write-Host "`nUpstream release tags since our base: $($tags -join ', ')" }

$branch = "sync/$(Get-Date -Format yyyy-MM-dd)"
$start = git rev-parse --abbrev-ref HEAD
git checkout -b $branch --quiet
git merge --no-ff --no-edit $up
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nCONFLICTS in:"; git diff --name-only --diff-filter=U
    git merge --abort; git checkout $start --quiet; git branch -D $branch --quiet
    Write-Host "Merge aborted; nothing changed. Resolve by hand or ask Claude."
    exit 1
}

dotnet build ShareX/ShareX.csproj -c Release -r win-x64 -nologo -v q
$ok = $LASTEXITCODE -eq 0
git checkout $start --quiet
if ($ok) { Write-Host "`nBuild OK on $branch. Review, then: git merge $branch (on main), push, redeploy." }
else { Write-Host "`nBuild FAILED on $branch. Branch kept for fixing." ; exit 3 }
