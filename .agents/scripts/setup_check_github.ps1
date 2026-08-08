# setup_check_github.ps1 — verify prerequisites for the github-backlog pipeline.
# Run: powershell -ExecutionPolicy Bypass -File setup_check_github.ps1
# Prints PASS / WARN / FAIL with a fix for anything missing. Read-only; changes nothing.

$ok = $true
function Line($status, $what, $detail) {
    $color = switch ($status) { "PASS" { "Green" } "WARN" { "Yellow" } default { "Red" } }
    Write-Host ("{0,-5} {1,-22} {2}" -f $status, $what, $detail) -ForegroundColor $color
}

# --- gh CLI present + logged in ---
$gh = (Get-Command gh -ErrorAction SilentlyContinue)
if (-not $gh) {
    Line "FAIL" "gh CLI" "not found. Install: https://cli.github.com"; $ok = $false
} else {
    $ver = (gh --version | Select-Object -First 1)
    Line "PASS" "gh CLI" $ver
    $authOut = gh auth status 2>&1
    if ($LASTEXITCODE -eq 0) {
        Line "PASS" "gh auth" "logged in"
    } else {
        Line "FAIL" "gh auth" "not logged in. Run: gh auth login"; $ok = $false
    }
}

# --- Python ---
$py = (Get-Command python -ErrorAction SilentlyContinue)
if (-not $py) {
    Line "FAIL" "Python" "not found. Install Python 3.x"; $ok = $false
} else {
    $v = python --version 2>&1
    Line "PASS" "Python" "$v"

    # requests
    $req = python -c "import requests; print(requests.__version__)" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Line "PASS" "requests" "$req"
    } else {
        Line "FAIL" "requests" "not installed. Run: pip install requests"; $ok = $false
    }

    # openpyxl (for write-back)
    $xl = python -c "import openpyxl; print(openpyxl.__version__)" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Line "PASS" "openpyxl" "$xl"
    } else {
        Line "FAIL" "openpyxl" "not installed. Run: pip install openpyxl"; $ok = $false
    }
}

# --- Env vars ---
if ($env:GH_OWNER) {
    Line "PASS" "GH_OWNER" $env:GH_OWNER
} else {
    Line "WARN" "GH_OWNER" "not set — set before running the pipeline"
}
if ($env:GH_REPO) {
    Line "PASS" "GH_REPO" $env:GH_REPO
} else {
    Line "WARN" "GH_REPO" "not set — set before running the pipeline"
}

# --- Repo reachable (only if both env vars set) ---
if ($env:GH_OWNER -and $env:GH_REPO) {
    $full = gh api "repos/$env:GH_OWNER/$env:GH_REPO" --jq '.full_name' 2>&1
    if ($LASTEXITCODE -eq 0) {
        Line "PASS" "repo reachable" $full
    } else {
        Line "FAIL" "repo reachable" "could not reach $env:GH_OWNER/$env:GH_REPO — check spelling and permissions"
        $ok = $false
    }
}

Write-Host ""
if ($ok) { Write-Host "All checks passed." -ForegroundColor Green }
else      { Write-Host "Some checks failed — fix them before running the pipeline." -ForegroundColor Red }
