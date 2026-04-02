# ============================================================
# AIS Durable Orchestrator Local Test (Prod-ready)
# For payload shape:
#   _request -> WOList (PascalCase) -> [ { WOExpLines/WOItemLines/WOHourLines ... } ]
#
# Script:
# - Normalizes WOList => "wo list" (if your Mock FSA expects that)
# - Leaves the inner structure as-is (WOExpLines, JournalLines, etc.)
# - Seeds Mock FSA
# - Triggers Durable Orchestrator
# - Polls Durable status until completion
# ============================================================

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# -----------------------------
# 0) Endpoints (local)
# -----------------------------
$BaseUrl      = "http://localhost:7071"
$MockFsaUri   = "$BaseUrl/api/mock/fsa/workorders/seed"
$OrchStartUri = "$BaseUrl/api/accrual/orchestrate/durable"

function Get-DurableStatusUrl([string]$instanceId) {
  return "$BaseUrl/runtime/webhooks/durabletask/instances/$instanceId"
}

function Get-DurableTerminateUrl([string]$instanceId) {
  return "$BaseUrl/runtime/webhooks/durabletask/instances/$instanceId/terminate?reason=TerminatedByTestScript"
}

# -----------------------------
# Helpers
# -----------------------------
function Invoke-HttpJson {
  param(
    [Parameter(Mandatory=$true)][ValidateSet("GET","POST","PUT","PATCH","DELETE")][string]$Method,
    [Parameter(Mandatory=$true)][string]$Uri,
    [hashtable]$Headers = @{},
    [string]$Body = $null,
    [int]$TimeoutSec = 120
  )

  try {
    if ($null -ne $Body) {
      return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $Headers -Body $Body -TimeoutSec $TimeoutSec
    } else {
      return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $Headers -TimeoutSec $TimeoutSec
    }
  }
  catch {
    $resp = $_.Exception.Response
    $status = $null
    $respBody = $null

    if ($resp -ne $null) {
      try { $status = $resp.StatusCode } catch { }
      try {
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $respBody = $sr.ReadToEnd()
        $sr.Close()
      } catch { }
    }

    $msg = "HTTP call failed. Method=$Method Uri=$Uri"
    if ($status)   { $msg += "`nStatus=$status" }
    if ($respBody) { $msg += "`nBody=$respBody" }
    throw $msg
  }
}

function Normalize-WoPayloadForSeed {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Json
  )

  # Validate JSON parses
  $obj = $Json | ConvertFrom-Json -ErrorAction Stop
  if ($null -eq $obj) { throw "Payload is empty or invalid JSON." }

  # Must have _request
  $reqProp = $obj.PSObject.Properties["_request"]
  if ($null -eq $reqProp) { throw "Payload missing required '_request' object." }

  $request = $reqProp.Value

  # Map WOList => "wo list" (what your mock seed usually expects)
  $woListProp  = $request.PSObject.Properties["wo list"]
  $woList2Prop = $request.PSObject.Properties["WOList"]

  if ($null -eq $woListProp -and $null -ne $woList2Prop) {
    $request | Add-Member -NotePropertyName "wo list" -NotePropertyValue $woList2Prop.Value -Force
    $request.PSObject.Properties.Remove("WOList") | Out-Null
  }

  # Validate "wo list" exists now
  $woListProp = $request.PSObject.Properties["wo list"]
  if ($null -eq $woListProp) {
    throw "Payload must contain '_request' -> 'wo list' (or 'WOList')."
  }

  # Re-emit normalized JSON
  return ($obj | ConvertTo-Json -Depth 80)
}

# -----------------------------
# 1) Headers
# -----------------------------
$CorrelationId = [guid]::NewGuid().ToString()
$Headers = @{
  "Content-Type"     = "application/json"
  "x-correlation-id" = $CorrelationId
}
Write-Host "CorrelationId = $CorrelationId"

# -----------------------------
# 2) WO Payload (paste full JSON)
# -----------------------------
$WoPayloadJson = @'
{
 "_request": {
  "WOList": [
   {
    "Company": "425",
    "SubProjectId": "425-P0000001-00001",
    "WorkOrderGUID": "{6BC7FF8C-6E35-4028-98A8-446C0F359836}",
    "WorkOrderID": "Work order 1",
    "WOExpLines": {
     "JournalDescription": "Test journal",
     "LineType": "Expense",
     "JournalLines": [
      {
       "AccrualLineGUID": "{F08EDBA4-A9D2-4C1B-87D0-B5FC212AAF73}",
       "AccrualLineVersionNumber": 1,
       "Company": "",
       "Currency": "USD",
       "DimensionDepartment": "0344",
       "DimensionProduct": "003",
       "Duration": 0.0,
       "FSAUnitPrice": 10,
       "ItemId": "",
       "JournalDescription": "",
       "JournalId": "",
       "JournalLineDescription": "",
       "JournalType": "Expense",
       "LineNum": 1,
       "LineProperty": "NonBill",
       "Location": "",
       "ProductColourId": "",
       "ProductConfigurationId": "",
       "ProductSizeId": "",
       "ProductStyleId": "",
       "ProjectCategory": "30201(EXPENSE)SERVICES",
       "Quantity": 2,
       "ResourceCompany": "425",
       "ResourceId": "12045",
       "RPCCustomerProductReference": "Testing",
       "RPCDiscountAmount": 0.0,
       "RPCDiscountPercent": 0.0,
       "RPCMarkupPercent": 0.0,
       "RPCOverallDiscountAmount": 0.0,
       "RPCOverallDiscountPercent": 0.0,
       "RPCSurchargeAmount": 0.0,
       "RPCSurchargePercent": 0.0,
       "RPMarkUpAmount": 0.0,
       "Site": "",
       "TransactionDate": "/Date(1767052800000)/",
       "UnitAmount": 30,
       "UnitCost": 20,
       "UnitId": "ea",
       "Warehouse": ""
      }
     ]
    },
    "WOHourLines": {
     "JournalDescription": "Test journal",
     "LineType": "Hour",
     "JournalLines": [
      {
       "AccrualLineGUID": "{521DAA6D-2292-4B27-BB14-36356A0443D7}",
       "AccrualLineVersionNumber": 1,
       "Company": "",
       "Currency": "USD",
       "DimensionDepartment": "0344",
       "DimensionProduct": "119",
       "Duration": 2,
       "FSAUnitPrice": 10,
       "ItemId": "",
       "JournalDescription": "",
       "JournalId": "",
       "JournalLineDescription": "WO Hour Jour Line",
       "JournalType": "Expense",
       "LineNum": 1,
       "LineProperty": "Bill",
       "Location": "",
       "ProductColourId": "",
       "ProductConfigurationId": "",
       "ProductSizeId": "",
       "ProductStyleId": "",
       "ProjectCategory": "30201(SERVICE)SERVICES",
       "Quantity": 1.0,
       "ResourceCompany": "425",
       "ResourceId": "12045",
       "RPCCustomerProductReference": "Testing",
       "RPCDiscountAmount": 0.0,
       "RPCDiscountPercent": 0.0,
       "RPCMarkupPercent": 0.0,
       "RPCOverallDiscountAmount": 0.0,
       "RPCOverallDiscountPercent": 0.0,
       "RPCSurchargeAmount": 0.0,
       "RPCSurchargePercent": 0.0,
       "RPMarkUpAmount": 0.0,
       "Site": "",
       "TransactionDate": "/Date(1767052800000)/",
       "UnitAmount": 30,
       "UnitCost": 20,
       "UnitId": "",
       "Warehouse": ""
      }
     ]
    },
    "WOItemLines": {
     "JournalDescription": "Test journal",
     "LineType": "Item",
     "JournalLines": [
      {
       "AccrualLineGUID": "{670C159A-259B-42CC-B8A1-E1B79A0A1E09}",
       "AccrualLineVersionNumber": 1,
       "Company": "",
       "Currency": "USD",
       "DimensionDepartment": "0344",
       "DimensionProduct": "119",
       "Duration": 0.0,
       "FSAUnitPrice": 10,
       "ItemId": "10002",
       "JournalDescription": "",
       "JournalId": "",
       "JournalLineDescription": "WO Item Jour Line",
       "JournalType": "Item",
       "LineNum": 0.0,
       "LineProperty": "Bill",
       "Location": "",
       "ProductColourId": "",
       "ProductConfigurationId": "",
       "ProductSizeId": "",
       "ProductStyleId": "",
       "ProjectCategory": "GENERIC M&R ITEM",
       "Quantity": 2.0,
       "ResourceCompany": "",
       "ResourceId": "",
       "RPCCustomerProductReference": "Testing",
       "RPCDiscountAmount": 0.0,
       "RPCDiscountPercent": 0.0,
       "RPCMarkupPercent": 0.0,
       "RPCOverallDiscountAmount": 0.0,
       "RPCOverallDiscountPercent": 0.0,
       "RPCSurchargeAmount": 0.0,
       "RPCSurchargePercent": 0.0,
       "RPMarkUpAmount": 0.0,
       "Site": "344",
       "TransactionDate": "/Date(1767744000000)/",
       "UnitAmount": 30,
       "UnitCost": 30,
       "UnitId": "ea",
       "Warehouse": "344-01"
      }
     ]
    }
   }
  ]
 }
}
'@

# Validate JSON locally
try { $null = $WoPayloadJson | ConvertFrom-Json -ErrorAction Stop }
catch { throw "WO payload is not valid JSON. Fix formatting first. Error: $($_.Exception.Message)" }

# Normalize payload for the mock seed endpoint
$SeedJson = Normalize-WoPayloadForSeed $WoPayloadJson

# Optional sanity print
try {
  $seedObj = $SeedJson | ConvertFrom-Json -ErrorAction Stop
  $req = $seedObj.PSObject.Properties["_request"].Value
  $wo  = $req.PSObject.Properties["wo list"].Value
  Write-Host ("Normalized 'wo list' count = {0}" -f $wo.Count)
} catch {
  throw "Normalization sanity-check failed: $($_.Exception.Message)"
}

# -----------------------------
# 3) Seed Mock FSA with payload
# -----------------------------
Write-Host "`n[1/4] Seeding Mock FSA payload..." -ForegroundColor Cyan
$null = Invoke-HttpJson -Method "POST" -Uri $MockFsaUri -Headers $Headers -Body $SeedJson -TimeoutSec 120
Write-Host "Mock FSA seeded successfully."

# -----------------------------
# 4) Trigger Durable Orchestrator
# -----------------------------
Write-Host "`n[2/4] Triggering Durable Orchestrator..." -ForegroundColor Cyan
$startResp = Invoke-HttpJson -Method "POST" -Uri $OrchStartUri -Headers $Headers -Body "{}" -TimeoutSec 120

$instanceId = $null
if ($startResp -ne $null) {
  if ($startResp.PSObject.Properties["instanceId"]) { $instanceId = $startResp.instanceId }
  elseif ($startResp.PSObject.Properties["InstanceId"]) { $instanceId = $startResp.InstanceId }
  elseif ($startResp.PSObject.Properties["id"]) { $instanceId = $startResp.id }
  elseif ($startResp.PSObject.Properties["statusQueryGetUri"]) {
    # If your starter returns durable management payload, prefer that:
    $statusQueryGetUri = $startResp.statusQueryGetUri
  }
}

Write-Host "Orchestrator Start Response:"
$startResp | ConvertTo-Json -Depth 20 | Write-Host

if (-not [string]::IsNullOrWhiteSpace($statusQueryGetUri)) {
  $statusUri = $statusQueryGetUri
  Write-Host "Using statusQueryGetUri from starter response."
}
else {
  if ([string]::IsNullOrWhiteSpace($instanceId)) {
    throw "InstanceId not returned. Check your HTTP starter response contract."
  }
  $statusUri = Get-DurableStatusUrl $instanceId
  Write-Host "InstanceId = $instanceId"
}

Write-Host "StatusUri  = $statusUri"

# -----------------------------
# 5) Poll Durable status until Completed/Failed/Terminated/Canceled
# -----------------------------
Write-Host "`n[3/4] Polling orchestration status..." -ForegroundColor Cyan

$maxWaitSeconds = 600
$pollEverySec   = 5
$elapsed        = 0
$status         = $null

while ($true) {
  $status = Invoke-HttpJson -Method "GET" -Uri $statusUri -Headers @{} -TimeoutSec 60

  $runtimeStatus = $status.runtimeStatus
  $createdTime   = $status.createdTime
  $lastUpdated   = $status.lastUpdatedTime

  Write-Host ("[{0,4}s] Status={1} Created={2} Updated={3}" -f $elapsed, $runtimeStatus, $createdTime, $lastUpdated)

  if ($runtimeStatus -in @("Completed","Failed","Terminated","Canceled")) {
    Write-Host "Orchestration finished with status: $runtimeStatus" -ForegroundColor Yellow
    break
  }

  Start-Sleep -Seconds $pollEverySec
  $elapsed += $pollEverySec

  if ($elapsed -ge $maxWaitSeconds) {
    Write-Host "Timed out waiting for orchestration." -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($instanceId)) {
      Write-Host "Terminate URL:"
      Write-Host (Get-DurableTerminateUrl $instanceId)
    }
    break
  }
}

# -----------------------------
# 6) Print final output
# -----------------------------
Write-Host "`n[4/4] Final orchestration status payload:" -ForegroundColor Cyan
if ($null -ne $status) {
  $status | ConvertTo-Json -Depth 120 | Write-Host
} else {
  Write-Host "No status payload captured."
}
