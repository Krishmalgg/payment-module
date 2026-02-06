param (
    [string]$ApiKey = "pm_live_a1b2c3d4e5f6g7h8",
    [string]$Secret = "d41d8cd98f00b204e9800998ecf8427e3b435b6c7d8e9f0a1b2c3d4e5f6g7h8i",
    [string]$Method = "POST",
    [string]$Path = "/api/payments/card",
    [string]$Body = '{"amount":1500,"currency":"LKR"}'
)

$Timestamp = [int][double]::Parse((Get-Date -UFormat %s))
$Nonce = [Guid]::NewGuid().ToString()
$Method = $Method.ToUpper()

# Canonical String: Method + Path + Timestamp + Nonce + Body
$Payload = "$Method$Path$Timestamp$Nonce$Body"

$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($Secret)
$hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Payload))
$Signature = [BitConverter]::ToString($hash).Replace("-", "").ToLower()

Write-Host "--- S2S Headers ---" -ForegroundColor Cyan
Write-Host "x-api-key: $ApiKey"
Write-Host "x-timestamp: $Timestamp"
Write-Host "x-nonce: $Nonce"
Write-Host "x-signature: $Signature"
Write-Host ""
Write-Host "--- Payload Signed ---" -ForegroundColor Gray
Write-Host $Payload
Write-Host ""
Write-Host "--- Curl Command ---" -ForegroundColor Green
$EscapedBody = $Body.Replace('"', '\"')
Write-Host "curl -X $Method ""http://localhost:5201$Path"" -H ""x-api-key: $ApiKey"" -H ""x-timestamp: $Timestamp"" -H ""x-nonce: $Nonce"" -H ""x-signature: $Signature"" -H ""Content-Type: application/json"" -d ""$EscapedBody"""
