<#
.SYNOPSIS
  Bulk-create/update Azure Function App application settings (prod-ready, Az.Websites compatible).

.DESCRIPTION
  - Uses Az PowerShell.
  - Uses Update-AzFunctionAppSetting (widely available) instead of Set-AzFunctionAppSetting.
  - Excludes these keys (as requested):
      FUNCTIONS_WORKER_RUNTIME
      FUNCTIONS_EXTENSION_VERSION
      AzureWebJobsStorage
      WEBSITE_RUN_FROM_PACKAGE
  - Excludes KV-only secret names:
      TenantId, FscmClientId, FscmClientSecret
  - Optionally sets Smtp:Password if provided.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string] $SubscriptionId,

  [Parameter(Mandatory = $true)]
  [string] $ResourceGroupName,

  [Parameter(Mandatory = $true)]
  [string] $FunctionAppName,

  # Optional: literal password or Key Vault reference string
  [Parameter(Mandatory = $false)]
  [string] $SmtpPassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Cmdlet {
  param([string]$Name)
  if (-not (Get-Command -Name $Name -ErrorAction SilentlyContinue)) {
    throw "Required cmdlet '$Name' not found. Ensure Az modules are installed and imported."
  }
}

# Required cmdlets for this script
Assert-Cmdlet "Get-AzContext"
Assert-Cmdlet "Select-AzSubscription"
Assert-Cmdlet "Update-AzFunctionAppSetting"
Assert-Cmdlet "Get-AzFunctionApp"

# Ensure you are in the right subscription
$ctx = Get-AzContext -ErrorAction SilentlyContinue
if (-not $ctx) {
  throw "Not logged in. Run Connect-AzAccount first."
}
if ($ctx.Subscription.Id -ne $SubscriptionId) {
  Select-AzSubscription -SubscriptionId $SubscriptionId | Out-Null
}

# Verify Function App exists
$fa = Get-AzFunctionApp -ResourceGroupName $ResourceGroupName -Name $FunctionAppName -ErrorAction SilentlyContinue
if (-not $fa) {
  throw "Function App '$FunctionAppName' not found in resource group '$ResourceGroupName'."
}

$Excluded = @(
  "FUNCTIONS_WORKER_RUNTIME",
  "FUNCTIONS_EXTENSION_VERSION",
  "AzureWebJobsStorage",
  "WEBSITE_RUN_FROM_PACKAGE",
  "TenantId",
  "FscmClientId",
  "FscmClientSecret"
)

# Desired app settings (from your local appsettings.json)
$Desired = [ordered]@{
  "KeyVault:VaultUri" = "https://kv-d365ais-dev-eastus2.vault.azure.net/"

  "Fscm:Auth:DefaultScope" = ""
  "Fscm:Auth:ScopesByHost:rg-dev-pwc-0348924b69cefd8760devaos.axcloud.dynamics.com" = "https://rg-dev-pwc-0348924b69cefd8760devaos.axcloud.dynamics.com/.default"
  "Fscm:Auth:ScopesByHost:rg-dev-pwc-185667f196b2554cf6devaos.axcloud.dynamics.com" = "https://rg-dev-pwc-185667f196b2554cf6devaos.axcloud.dynamics.com/.default"

  "Endpoints:FsaBaseUrl" = "http://localhost:7071"
  "Endpoints:FsaPath"    = "/api/mock/fsa/workorders"

  "Endpoints:FscmValidationBaseUrl" = "https://rg-dev-pwc-0348924b69cefd8760devaos.axcloud.dynamics.com"
  "Endpoints:FscmValidationPath"    = "/api/services/RPCFSWOJournalValidationServiceGroup/RPCFSWOJournalValidationService/validate"

  "Endpoints:FscmPostingBaseUrl" = "https://rg-dev-pwc-185667f196b2554cf6devaos.axcloud.dynamics.com"
  "Endpoints:FscmPostingPath"    = "/api/services/RPCJournalOperationWOIntV2ServiceGroup/RPCJournalOperationWOIntV2Service/JournalAsync"

  "Endpoints:FscmSubProjectBaseUrl" = "https://rg-dev-pwc-185667f196b2554cf6devaos.axcloud.dynamics.com"
  "Endpoints:FscmSubProjectPath"    = "/api/services/RPCFieldServiceSubProjectServiceGroup/RPCFieldServiceSubProjectService/createSubProject"

  "Notifications:ErrorDistributionList" = "sagnik37@gmail.com"

  "Smtp:Host" = "smtp.gmail.com"
  "Smtp:Port" = "587"
  "Smtp:EnableSsl" = "true"
  "Smtp:Username" = "sagnik37@gmail.com"
  "Smtp:FromAddress" = "sagnik37@gmail.com"
  "Smtp:FromDisplayName" = "AIS Orchestrator (Local)"

  "Processing:Mode" = "InMemoryDurable"
  "Processing:MaxRecordsPerBatch" = "50"
  "Processing:TargetBatchBytes" = "1000000"
  "Processing:MaxParallelBatches" = "3"
  "Processing:DurablePostRetryAttempts" = "5"
  "Processing:DurablePostRetryFirstIntervalSeconds" = "5"
  "Processing:DurablePostRetryBackoffCoefficient" = "2.0"
  "Processing:DurablePostRetryMaxIntervalSeconds" = "300"

  "AccrualSchedule" = "0 */50 * * * *"
}

if (-not [string]::IsNullOrWhiteSpace($SmtpPassword)) {
  $Desired["Smtp:Password"] = $SmtpPassword
}

# Remove excluded keys defensively
foreach ($k in $Excluded) {
  if ($Desired.Contains($k)) { $Desired.Remove($k) }
}

Write-Host "Applying $($Desired.Count) application settings to '$FunctionAppName'..."
Write-Host "Excluded keys: $($Excluded -join ', ')"

# Apply settings
Update-AzFunctionAppSetting `
  -ResourceGroupName $ResourceGroupName `
  -Name $FunctionAppName `
  -AppSetting $Desired | Out-Null

Write-Host "Settings applied successfully."
Write-Host "Recommended next step: restart the Function App."
