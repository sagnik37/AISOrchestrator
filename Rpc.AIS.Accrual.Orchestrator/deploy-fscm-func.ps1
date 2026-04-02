$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ===========================
# CONFIG
# ===========================
$resourceGroup = "rg-d365-fs-fscm-intg-wo-projects-eastus"
$functionApp   = "app-fs-fscm-intg-woprojects-sit2"

$repoRoot        = "C:\RPC\ais\Rpc.AIS.Accrual.Orchestrator_Updated"
$functionsCsproj = Join-Path $repoRoot "src\Rpc.AIS.Accrual.Orchestrator.Functions\Rpc.AIS.Accrual.Orchestrator.Functions.csproj"

$publishFolder = Join-Path $repoRoot "publish"
$zipPath       = Join-Path $repoRoot "Rpc.AIS.Accrual.Orchestrator.zip"

function Write-Info($msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Fail($msg) { Write-Host $msg -ForegroundColor Red }

function Assert-CommandExists([string]$commandName, [string]$installHint) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        Write-Fail "Required command '$commandName' was not found in PATH."
        Write-Warn $installHint
        throw "Missing dependency: $commandName"
    }
}

# Robust external process invocation:
# - Avoids $args collisions by using $toolArgs
# - Uses Start-Process to ensure arguments are passed reliably
# - Fails fast on non-zero exit codes with clear command line
function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $false)][string[]]$ToolArgs = @()
    )

    $cmdLine = if ($ToolArgs -and $ToolArgs.Count -gt 0) {
        "{0} {1}" -f $File, ($ToolArgs -join " ")
    }
    else {
        "{0}" -f $File
    }

    Write-Host ("Running: {0}" -f $cmdLine) -ForegroundColor DarkGray

    # Defensive: prevent accidental "dotnet" (no args) calls unless explicitly intended
    if ($File -ieq "dotnet" -and (-not $ToolArgs -or $ToolArgs.Count -eq 0)) {
        throw "Internal script error: attempted to run 'dotnet' with no arguments. Fix the caller argument array."
    }

    $p = Start-Process -FilePath $File -ArgumentList $ToolArgs -NoNewWindow -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "Command failed ($($p.ExitCode)): $cmdLine"
    }
}

# ===========================
# 0) Prechecks
# ===========================
Write-Info "Precheck: validating dependencies..."
Assert-CommandExists "dotnet" "Install .NET SDK 8.x and reopen your terminal."
Assert-CommandExists "az"     "Install Azure CLI and reopen your terminal. Verify with: az --version"

if (-not (Test-Path $functionsCsproj)) {
    Write-Fail "Functions project file not found:"
    Write-Host "  $functionsCsproj"
    throw "Invalid path for Functions .csproj"
}

Write-Ok "Dependencies OK."
Write-Host "dotnet: $(dotnet --version)"
Write-Host "az:     $((az --version | Select-Object -First 1) -join ' ')"

# ===========================
# 1) Azure login
# ===========================
Write-Info "Checking Azure login..."
try {
    az account show --only-show-errors *> $null
    Write-Ok "Already logged into Azure CLI."
}
catch {
    Write-Warn "Not logged in. Launching 'az login'..."
    Invoke-External -File "az" -ToolArgs @("login","--only-show-errors")
    Write-Ok "Azure login completed."
}

# Validate Function App exists
Write-Info "Validating target Function App exists..."
$faJson = az functionapp show --resource-group $resourceGroup --name $functionApp --only-show-errors --output json
$fa = $faJson | ConvertFrom-Json
Write-Ok "Target confirmed: $($fa.name) (State: $($fa.state))"

# ===========================
# 2) Clean publish folder
# ===========================
Write-Info "Cleaning publish output folder..."
if (Test-Path $publishFolder) { Remove-Item -Recurse -Force $publishFolder }
New-Item -ItemType Directory -Path $publishFolder | Out-Null
Write-Ok "Publish folder ready: $publishFolder"

# ===========================
# 3) Publish (Release)
# ===========================
Write-Info "Publishing Functions project (Release)..."
Invoke-External -File "dotnet" -ToolArgs @(
    "publish", $functionsCsproj,
    "-c", "Release",
    "-o", $publishFolder
)

# Sanity check
$hostJson = Join-Path $publishFolder "host.json"
if (-not (Test-Path $hostJson)) {
    throw "Publish succeeded but host.json not found at $hostJson. Deployment package is invalid."
}
Write-Ok "Publish output validated (host.json found)."

# ===========================
# 4) Create ZIP
# ===========================
Write-Info "Creating deployment ZIP..."
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $publishFolder "*") -DestinationPath $zipPath -Force

$zipInfo = Get-Item $zipPath
Write-Ok "ZIP created: $zipPath"
Write-Host ("ZIP size: {0:N2} MB" -f ($zipInfo.Length / 1MB))

# ===========================
# 5) Deploy ZIP
# ===========================
Write-Info "Deploying ZIP to Function App via config-zip..."
Invoke-External -File "az" -ToolArgs @(
  "functionapp","deployment","source","config-zip",
  "--resource-group",$resourceGroup,
  "--name",$functionApp,
  "--src",$zipPath,
  "--only-show-errors"
)

Write-Ok "Deployment completed successfully."

Write-Host ""
Write-Ok "Diagnostics:"
Write-Host "az webapp log tail --name $functionApp --resource-group $resourceGroup"
