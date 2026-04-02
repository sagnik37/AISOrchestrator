# ============================================================
# AIS Durable Orchestrator Local Test (Prod-ready)
# - Seeds Mock FSA with WO payload (normalized to _request -> "wo list")
# - Triggers Durable Orchestrator
# - Polls instance until completion
# ============================================================

$ErrorActionPreference = "Stop"

# -----------------------------
# 0) Endpoints
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

    # If WOList exists but 'wo list' doesn't, map it
    $woListProp  = $request.PSObject.Properties["wo list"]
    $woList2Prop = $request.PSObject.Properties["WOList"]

    if ($null -eq $woListProp -and $null -ne $woList2Prop) {
        # Add 'wo list' with same value
        $request | Add-Member -NotePropertyName "wo list" -NotePropertyValue $woList2Prop.Value -Force
        # Remove WOList
        $request.PSObject.Properties.Remove("WOList") | Out-Null
    }

    # Validate 'wo list' exists now
    $woListProp = $request.PSObject.Properties["wo list"]
    if ($null -eq $woListProp) {
        throw "Payload must contain '_request' -> 'wo list' (or 'WOList')."
    }

    # IMPORTANT:
    # Return the original JSON string if already correct, otherwise do a safe text replace.
    # If you used WOList, rewrite only that key name in the raw JSON.
    if ($Json -match '"WOList"\s*:') {
        # Replace only the property name token "WOList": with "wo list":
        $Json = [regex]::Replace($Json, '"WOList"\s*:', '"wo list":')
    }

    return $Json
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
# IMPORTANT:
#   - If your payload uses "_request" -> "WOList", it will be auto-mapped to "wo list"
#   - If it already uses "_request" -> "wo list", it will pass as-is
# -----------------------------
$WoPayloadJson = @'
{
  "_request": {
    "WOList": [
      {
        "Company": "425",
        "Work order GUID": "00000000-0000-0000-0000-000000000001",
        "Work order ID": "INV001",
        "WO Item Lines": {
          "LineType": "Item",
          "JournalLines": [
            {
              "Company": "425",
              "Sub-project Id": "PROJ-001",
              "Journal Id": "425-000001",
              "Line num": 1,
              "Item Id": "ITEM-100",
              "Quantity": 2,
              "Unit Id": "ea",
              "Project category": "CAT-ITEM",
              "Unit cost": 50,
              "Currency": "USD",
              "Unit amount": 120,
              "Line property": "LP-STD",
              "Site": "1",
              "Warehouse": "11",
              "Location": "LOC-01",
              "Transaction date": "2025-01-01",
              "Journal description": "Item journal header",
              "Journal line description": "Item journal line",
              "Journal type": "Item",
              "Accrual line version number": 1,
              "Accrual line GUID": "11111111-1111-1111-1111-111111111111",
              "RPC customer product reference": "CUST-PROD-1",
              "FSA unit price": 150,
              "RPC discount amount": 10,
              "RPC surcharge amount": 5,
              "RPC overall discount amount": 0,
              "RPC discount percent": 5,
              "RPC surcharge percent": 2,
              "Dimension department": "D001",
              "Dimension product": "P001"
            }
          ]
        },
        "WO Exp Lines": {
          "LineType": "Expense",
          "JournalLines": [
            {
              "Company": "425",
              "Sub-project Id": "PROJ-001",
              "Journal Id": "425-JNUM-00000014",
              "Line num": 1,
              "Project category": "CAT-EXP",
              "Currency": "USD",
              "Unit amount": 75,
              "Transaction date": "2025-01-01",
              "Journal description": "Expense journal header",
              "Journal line description": "Expense journal line",
              "Journal type": "Expense",
              "Dimension department": "D002",
              "Dimension product": "P002"
            }
          ]
        },
        "WO Hour Lines": {
          "LineType": "Hour",
          "JournalLines": [
            {
              "Company": "425",
              "Sub-project Id": "PROJ-001",
              "Journal Id": "425-PJ-000000001",
              "Line num": 1,
              "Duration": 8,
              "Unit Id": "hour",
              "Project category": "CAT-HOUR",
              "Currency": "USD",
              "Transaction date": "2025-01-01",
              "Resource Id": "WRK-0001",
              "Resource company": "425",
              "Journal description": "Hour journal header",
              "Journal line description": "Hour journal line",
              "Journal type": "Hour",
              "Dimension department": "D003",
              "Dimension product": "P003"
            }
          ]
        }
      },
      {
        "Company": "425",
        "Work order GUID": "00000000-0000-0000-0000-000000000002",
        "Work order ID": "INV002",
        "WO Item Lines": {
          "LineType": "Item",
          "JournalLines": [
            {
              "Company": "425",
              "Sub-project Id": "PROJ-002",
              "Journal Id": "425-000001",
              "Line num": 1,
              "Item Id": "ITEM-200",
              "Quantity": 1,
              "Unit Id": "ea",
              "Project category": "CAT-ITEM",
              "Unit cost": 100,
              "Currency": "USD",
              "Unit amount": 150,
              "Transaction date": "2025-01-02",
              "Journal description": "Item journal header 2",
              "Journal line description": "Item journal line 2",
              "Journal type": "Item",
              "Dimension department": "D010",
              "Dimension product": "P010"
            }
          ]
        },
        "WO Exp Lines": {
          "LineType": "Expense",
          "JournalLines": [
            {
              "Company": "425",
              "Sub-project Id": "PROJ-002",
              "Journal Id": "425-JNUM-00000014",
              "Line num": 1,
              "Project category": "CAT-EXP",
              "Currency": "USD",
              "Unit amount": 50,
              "Transaction date": "2025-01-02",
              "Journal description": "Expense journal header 2",
              "Journal line description": "Expense journal line 2",
              "Journal type": "Expense",
              "Dimension department": "D020",
              "Dimension product": "P020"
            }
          ]
        },
        "WO Hour Lines": {
          "LineType": "Hour",
          "JournalLines": [
            {
              "Company": "425",
              "Sub-project Id": "PROJ-002",
              "Journal Id": "425-PJ-000000001",
              "Line num": 1,
              "Duration": 6,
              "Unit Id": "hour",
              "Project category": "CAT-HOUR",
              "Currency": "USD",
              "Transaction date": "2025-01-02",
              "Resource Id": "WRK-0002",
              "Resource company": "425",
              "Journal description": "Hour journal header 2",
              "Journal line description": "Hour journal line 2",
              "Journal type": "Hour",
              "Dimension department": "D030",
              "Dimension product": "P030"
            }
          ]
        }
      }
    ]
  }
}
'@

# Validate JSON locally before calling anything (fail-fast)
try { $null = $WoPayloadJson | ConvertFrom-Json -ErrorAction Stop }
catch { throw "WO payload is not valid JSON. Fix formatting first. Error: $($_.Exception.Message)" }

# Normalize to what MockFsa_SeedWorkOrders expects: _request -> "wo list"
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

# Your durable HTTP starter typically takes no body (or small context). Keep "{}".
$startResp = Invoke-HttpJson -Method "POST" -Uri $OrchStartUri -Headers $Headers -Body "{}" -TimeoutSec 120

# Try multiple casing variants for instance id (depends on your function response contract)
$instanceId = $null
if ($startResp -ne $null) {
  if ($startResp.PSObject.Properties["instanceId"]) { $instanceId = $startResp.instanceId }
  elseif ($startResp.PSObject.Properties["InstanceId"]) { $instanceId = $startResp.InstanceId }
  elseif ($startResp.PSObject.Properties["id"]) { $instanceId = $startResp.id }
}

Write-Host "Orchestrator Start Response:"
$startResp | ConvertTo-Json -Depth 10 | Write-Host

if ([string]::IsNullOrWhiteSpace($instanceId)) {
  throw "InstanceId not returned. Check your StartAccrualOrchestration_Http function response contract."
}

$statusUri = Get-DurableStatusUrl $instanceId
Write-Host "InstanceId = $instanceId"
Write-Host "StatusUri  = $statusUri"

# -----------------------------
# 5) Poll Durable status until Completed/Failed/Terminated
# -----------------------------
Write-Host "`n[3/4] Polling orchestration status..." -ForegroundColor Cyan

$maxWaitSeconds = 600   # 10 minutes
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
    Write-Host "Timed out waiting for orchestration. InstanceId=$instanceId" -ForegroundColor Red
    Write-Host "You can terminate it manually if needed:"
    Write-Host (Get-DurableTerminateUrl $instanceId)
    break
  }
}

# -----------------------------
# 6) Print final output
# -----------------------------
Write-Host "`n[4/4] Final orchestration status payload:" -ForegroundColor Cyan
if ($null -ne $status) {
  $status | ConvertTo-Json -Depth 50 | Write-Host
} else {
  Write-Host "No status payload captured."
}
