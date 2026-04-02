# ==========================================
# Invoke FSCM Sub-Project Function (Local)
# ==========================================

$functionUrl = "http://localhost:7071/api/fscm/subproject"

# -------- Headers --------
$headers = @{
    "Content-Type"      = "application/json"
    "Accept"            = "application/json"
    "x-correlation-id"  = [guid]::NewGuid().ToString()
    "x-source-system"   = "LOCAL-TEST"
    "x-requested-by"    = "PowerShell"
}

# -------- Request Body --------
$payload = @{
    _request = @{
        DataAreaId         = "425"
        ParentProjectId    = "425-P0000068"
        ProjectName        = "WO-SubProject-001"
        CustomerReference  = "10005"
        InvoiceNotes       = "TestInvoiceNotes"
        ActualStartDate    = "2026-01-01"
        ActualEndDate      = "2026-01-31"
        AddressName        = "Well Site Test service address 3"
        Street             = "100 Main St"
        City               = "Houston"
        State              = "TX"
        County             = "Harris"
        CountryRegionId    = "USA"
        WellLocale         = "2"
        WellName           = "Well name for sub 0002"
        WellNumber         = "WN-0002"
    }
}

$jsonBody = $payload | ConvertTo-Json -Depth 5

# -------- Invoke Function --------
try {
    Write-Host "Calling Sub-Project Function..." -ForegroundColor Cyan
    Write-Host "CorrelationId: $($headers['x-correlation-id'])" -ForegroundColor DarkGray

    $response = Invoke-RestMethod `
        -Uri $functionUrl `
        -Method POST `
        -Headers $headers `
        -Body $jsonBody `
        -TimeoutSec 60

    Write-Host "Response received:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 10
}
catch {
    Write-Host "Error calling function:" -ForegroundColor Red
    Write-Host $_.Exception.Message

    if ($_.Exception.Response -ne $null) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Response body:" -ForegroundColor Yellow
        Write-Host $reader.ReadToEnd()
    }
}
