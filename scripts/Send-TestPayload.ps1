#Requires -Version 7.0
<#
.SYNOPSIS
    Posts a sample Azure DevOps service-hook payload to the Lightkeeper function and reports the response.
.DESCRIPTION
    Local runs need no key. For a deployed Function App, supply the function key through -FunctionKey (prompted securely
    when the URI is not localhost and no key is given). The key is sent in the x-functions-key header so it stays out of the URI.
.EXAMPLE
    ./scripts/Send-TestPayload.ps1
.EXAMPLE
    ./scripts/Send-TestPayload.ps1 -Uri https://<app>.azurewebsites.net/api/service-hooks/azure-devops
#>
[CmdletBinding()]
param(
    [Uri] $Uri = 'http://localhost:7071/api/service-hooks/azure-devops',
    [string] $PayloadPath = (Join-Path $PSScriptRoot '..' 'samples' 'build.complete.json'),
    [SecureString] $FunctionKey,
    [string] $CorrelationId = "lightkeeper-test-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
)

$ErrorActionPreference = 'Stop'

$headers = @{ 'x-correlation-id' = $CorrelationId }
if (-not $Uri.IsLoopback -and -not $FunctionKey) {
    $FunctionKey = Read-Host -Prompt 'Function key' -AsSecureString
}
if ($FunctionKey) {
    $headers['x-functions-key'] = ConvertFrom-SecureString -SecureString $FunctionKey -AsPlainText
}

$response = Invoke-WebRequest -Uri $Uri -Method Post -ContentType 'application/json' -Headers $headers `
    -Body (Get-Content -Path $PayloadPath -Raw) -SkipHttpErrorCheck

[pscustomobject]@{
    StatusCode    = $response.StatusCode
    CorrelationId = "$($response.Headers['x-correlation-id'])"
    Body          = $response.Content
} | Format-List
