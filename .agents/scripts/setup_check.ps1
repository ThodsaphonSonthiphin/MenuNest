# setup_check.ps1 — verify the prerequisites for the ado-backlog toolkit.
# Run: powershell -ExecutionPolicy Bypass -File setup_check.ps1
# Prints PASS / WARN / FAIL with a fix for anything missing. Read-only; changes nothing.

$ok = $true
function Line($status, $what, $detail) {
    $color = switch ($status) { "PASS" { "Green" } "WARN" { "Yellow" } default { "Red" } }
    Write-Host ("{0,-5} {1,-22} {2}" -f $status, $what, $detail) -ForegroundColor $color
}

# --- Azure CLI present + logged in ---
$az = (Get-Command az -ErrorAction SilentlyContinue)
if (-not $az) {
    Line "FAIL" "Azure CLI (az)" "not found. Install: https://aka.ms/installazurecli"; $ok = $false
} else {
    try {
        $acct = az account show --query "user.name" -o tsv 2>$null
        if ($acct) { Line "PASS" "az login" "signed in as $acct" }
        else { Line "FAIL" "az login" "not logged in. Run: az login"; $ok = $false }
    } catch { Line "FAIL" "az login" "not logged in. Run: az login"; $ok = $false }
}

# --- .NET SDK >= 10 ---
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $dotnet) {
    Line "FAIL" ".NET SDK" "not found. Need >= 10. Install: https://dotnet.microsoft.com/download"; $ok = $false
} else {
    $sdks = dotnet --list-sdks
    $has10 = $sdks | Where-Object { $_ -match "^(1[0-9]|[2-9][0-9])\." }
    if ($has10) { Line "PASS" ".NET SDK" "found $($has10 | Select-Object -First 1)" }
    else { Line "FAIL" ".NET SDK" "need >= 10 (create-backlog.cs is a .NET 10 file-based app). Have: $($sdks -join ', ')"; $ok = $false }
}

# --- Python + openpyxl ---
$py = (Get-Command python -ErrorAction SilentlyContinue)
if (-not $py) {
    Line "FAIL" "Python" "not found. Install Python 3.x and 'pip install openpyxl pyyaml'"; $ok = $false
} else {
    $v = python --version
    try {
        python -c "import openpyxl" 2>$null
        if ($?) { Line "PASS" "Python + openpyxl" "$v, openpyxl ok" }
        else { Line "WARN" "openpyxl" "missing. Run: python -m pip install openpyxl" }
    } catch { Line "WARN" "openpyxl" "missing. Run: python -m pip install openpyxl" }
    # --- PyYAML (daily-state.py frontmatter parse/emit) ---
    try {
        python -c "import yaml" 2>$null
        if ($?) { Line "PASS" "PyYAML" "installed (daily-state.py)" }
        else { Line "WARN" "PyYAML" "missing. Run: python -m pip install pyyaml" }
    } catch { Line "WARN" "PyYAML" "missing. Run: python -m pip install pyyaml" }
}

# --- Claude Code CLI ---
$claude = (Get-Command claude -ErrorAction SilentlyContinue)
if ($claude) { Line "PASS" "Claude Code" "found" }
else { Line "WARN" "Claude Code" "'claude' not on PATH (fine if you only use the IDE extension)" }

# --- target board env (optional but recommended) ---
if ($env:AZDO_ORG) { Line "PASS" "AZDO_ORG" $env:AZDO_ORG }
else { Line "WARN" "AZDO_ORG" "unset. Set it or let the skill ask: `$env:AZDO_ORG='YourOrg'" }
if ($env:AZDO_PROJECT) { Line "PASS" "AZDO_PROJECT" $env:AZDO_PROJECT }
else { Line "WARN" "AZDO_PROJECT" "unset. Set it or let the skill ask: `$env:AZDO_PROJECT='YourProject'" }

Write-Host ""
if ($ok) { Write-Host "Ready. Run /ado-backlog:run <your-file> to start." -ForegroundColor Green }
else { Write-Host "Fix the FAIL items above, then re-run setup_check.ps1." -ForegroundColor Red; exit 1 }
