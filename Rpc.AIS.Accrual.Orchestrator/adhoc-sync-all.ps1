# ----------------------------------------
# Configuration
# ----------------------------------------
$uri = "http://localhost:7071/api/fscm/workorder/adhoc-post"

$headers = @{
    "Content-Type"     = "application/json"
    "x-run-id"         = [guid]::NewGuid().ToString()
    "x-correlation-id" = [guid]::NewGuid().ToString()
}

# ----------------------------------------
# Request Payload
# NOTE: Use single quotes to preserve JSON
# ----------------------------------------
$body = @'
{
    "BusinessEventId": "",
    "BusinessEventLegalEntity": "",
    "Company": "",
    "ContextRecordSubject": "",
    "ControlNumber": 0,
    "EventId": "",
    "EventTime": "/Date(-2208988800000)/",
    "EventTimeIso8601": "1900-01-01T00:00:00Z",
    "InitiatingUserAADObjectId": "{00000000-0000-0000-0000-000000000000}",
    "MajorVersion": 0,
    "MinorVersion": 0,
    "ParentContextRecordSubjects": null,
    "ProjectId": "",
    "Work order GUID": "{00000000-0000-0000-0000-000000000000}"
}
'@

# ----------------------------------------
# Invoke REST Call
# ----------------------------------------
try {
    Write-Host "Calling Adhoc Work Order endpoint..." -ForegroundColor Cyan
    Write-Host "Run-Id        : $($headers['x-run-id'])"
    Write-Host "Correlation-Id: $($headers['x-correlation-id'])"
    Write-Host ""

    $response = Invoke-RestMethod `
        -Method Post `
        -Uri $uri `
        -Headers $headers `
        -Body $body `
        -TimeoutSec 300

    Write-Host "Response received successfully." -ForegroundColor Green
    Write-Host ""
    Write-Host "Response Body:" -ForegroundColor Yellow
    $response | ConvertTo-Json -Depth 20
}
catch {
    Write-Host "Request failed." -ForegroundColor Red
    Write-Host "Status Code:" $_.Exception.Response.StatusCode.value__
    Write-Host "Status Description:" $_.Exception.Response.StatusDescription

    if ($_.Exception.Response -and $_.Exception.Response.GetResponseStream()) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $errorBody = $reader.ReadToEnd()
        Write-Host ""
        Write-Host "Error Response Body:" -ForegroundColor Yellow
        Write-Host $errorBody
    }
}
