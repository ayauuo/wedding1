# Test Google Apps Script URL: GET and POST
$url = "https://script.google.com/macros/s/AKfycbzDTbQ0LQ2SebAAMv8mwPXCPW2JgnGEXkp1hw1GkTBCEdVnPMwD0AY7NE8_Ym2SVgBtaQ/exec"

Write-Host "=== GET ==="
try {
    $get = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15
    Write-Host "Status:" $get.StatusCode
    Write-Host "Body:" $get.Content
} catch {
    Write-Host "Error:" $_.Exception.Message
}

Write-Host ""
Write-Host "=== POST (JSON) ==="
try {
    $body = '{"source":"photobooth","test":true,"time":"2025-02-05T12:00:00Z","machineName":"test-pc","sample":{"date":"2025-02-05","projectName":"test","unitPrice":100,"dailySalesCount":1,"dailyRevenue":100}}'
    $post = Invoke-WebRequest -Uri $url -Method POST -Body $body -ContentType "application/json; charset=utf-8" -UseBasicParsing -TimeoutSec 15
    Write-Host "Status:" $post.StatusCode
    Write-Host "Body:" $post.Content
} catch {
    Write-Host "Error:" $_.Exception.Message
}
