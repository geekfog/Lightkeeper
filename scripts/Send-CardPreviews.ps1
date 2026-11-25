#Requires -Version 7.0
<#
.SYNOPSIS
    Sends one sample of every card type to the locally running Lightkeeper, so their appearance can be checked in Teams.
.DESCRIPTION
    Local only: refuses any address other than localhost, so neither Azure DevOps nor the hosted tables are involved.
    Adds the rule card-preview to the local (Azurite) NotificationRule table: active, priority 0, every organization and event,
    Include with no workflow, so every card goes to the fallback workflow in local.settings.json. Point that setting at a workflow
    that posts to you alone before running. Each run uses a new pull request number, so the announce-once check never skips it.
    Starts Azurite and the Functions host in their own windows when they are not already running. Azurite is left running; a Functions host the script
    started is stopped once the cards are sent, so the next run builds the latest code. A host that was already running is used as is.
    Needs Azurite, Azure Functions Core Tools, and the Azure CLI installed.

    With -ActiveListOrganization and -ActiveListProject, also previews the active pull request list for that real project. It reads
    AzDo__PersonalAccessToken from local.settings.json, announces the project's newest active non-draft pull request (so it is starred and shows Now),
    and adds the rule card-preview-active-list (priority -1, that organization and project, git.pullrequest.*, ActiveList) to the local table.
    Nothing is written to Azure DevOps.
.EXAMPLE
    ./scripts/Send-CardPreviews.ps1
.EXAMPLE
    ./scripts/Send-CardPreviews.ps1 -ActiveListOrganization fabrikam -ActiveListProject 'Fabrikam Fiber'
#>
[CmdletBinding()]
param(
    [Uri] $Uri = 'http://localhost:7071/api/service-hooks/azure-devops',
    [string] $ActiveListOrganization,
    [string] $ActiveListProject
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (-not $Uri.IsLoopback) {
    throw "Card previews run against the local host only, not $Uri."
}

$previewActiveList = [bool]$ActiveListOrganization -or [bool]$ActiveListProject
if ($previewActiveList -and -not ($ActiveListOrganization -and $ActiveListProject)) {
    throw 'Give both -ActiveListOrganization and -ActiveListProject to preview the active pull request list.'
}

# Azurite listens on IPv4 only, and localhost can resolve to IPv6 first.
$loopback = '127.0.0.1'
$azuriteTablePort = 10002
$startupTimeout = [TimeSpan]::FromMinutes(3)
$repositoryRoot = Join-Path $PSScriptRoot '..'
$functionsProject = Join-Path $repositoryRoot 'src' 'Lightkeeper.Functions'
$settingsPath = Join-Path $functionsProject 'local.settings.json'

# Without it the host exits at once, closing its window, and this script would wait out the startup timeout.
if (-not (Test-Path $settingsPath)) {
    throw "$settingsPath is missing. Copy local.settings.template.json beside it to local.settings.json and fill it in (see Create local settings in the README)."
}

function Test-Port([int] $port) { Test-Connection -TargetName $loopback -TcpPort $port -Quiet }

# Starts a missing service in its own window and waits until its port answers; returns the started process, or nothing when it was already running.
function Start-IfMissing([string] $name, [int] $port, [string] $command, [string[]] $arguments, [string] $workingDirectory, [string] $installHint) {
    if (Test-Port $port) { return }
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$name is not running and '$command' is not installed. $installHint"
    }

    Write-Host "Starting $name in a new window..."
    $process = Start-Process -FilePath (Get-Command $command).Source -ArgumentList $arguments -WorkingDirectory $workingDirectory -PassThru
    $deadline = [DateTime]::UtcNow + $startupTimeout
    while (-not (Test-Port $port)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "$name did not start within $($startupTimeout.TotalMinutes) minutes; check its window." }
        Start-Sleep -Seconds 2
    }

    $process
}

$null = Start-IfMissing 'Azurite' $azuriteTablePort 'azurite' @('--silent', '--skipApiVersionCheck', '--location', (Join-Path $repositoryRoot '.azurite')) `
    $repositoryRoot 'Install it with: npm install -g azurite'
$functionsHost = Start-IfMissing 'The Functions host' $Uri.Port 'func' @('start', '--port', $Uri.Port) $functionsProject `
    'Install it with: winget install Microsoft.Azure.FunctionsCoreTools'
if (-not $functionsHost) { Write-Warning 'Using the Functions host that was already running; stop it first if the code has changed since it started.' }

$samples = Join-Path $repositoryRoot 'samples'
# Azurite's published development account; the Azure CLI does not accept the UseDevelopmentStorage=true shortcut.
$azurite = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://${loopback}:$azuriteTablePort/devstoreaccount1;"
$ruleTable = 'NotificationRule'
$previewRuleId = 'card-preview'

# The host creates its tables on first use; the rule must exist before the first preview arrives.
az storage table create --name $ruleTable --connection-string $azurite --output none
az storage entity insert --table-name $ruleTable --connection-string $azurite --if-exists replace --output none --entity `
    PartitionKey='*' RowKey=$previewRuleId Active=true Active@odata.type=Edm.Boolean Priority=0 Priority@odata.type=Edm.Int32 `
    Action=Include 'Note=Local card previews: every event to the fallback workflow'

function Read-Sample([string] $name) { Get-Content -Path (Join-Path $samples $name) -Raw | ConvertFrom-Json -AsHashtable }

function New-BuildPreview([string] $result) {
    $payload = Read-Sample 'build.complete.json'
    $payload.resource.result = $result
    $payload
}

$pullRequest = Read-Sample 'git.pullrequest.created.json'
$pullRequest.resource.pullRequestId = Get-Random -Minimum 100000 -Maximum 999999
$pullRequest.resource.isDraft = $false
$pullRequest.resource.creationDate = [DateTime]::UtcNow.ToString('o')
$pullRequest.resource.description = @(
    '## Issue', '', 'Uploading a stylesheet fails with **two** "Network Error" toasts.', '',
    '## Fix', '', '- Send the file base64-encoded', '- Decode it in the API', '', 'See [the work item](https://dev.azure.com/) for details.'
) -join "`n"

$workItem = @{
    id              = [guid]::NewGuid().ToString()
    eventType       = 'workitem.updated'
    message         = @{ text = 'Bug #5 (Stylesheet upload fails) updated by Jamal Hartnett.' }
    detailedMessage = @{ markdown = "Bug #5 updated`n`n- **State**: New → Active`n- **Assigned to**: Jamal Hartnett" }
    resource        = @{ revision = @{ fields = @{ 'System.TeamProject' = 'Fabrikam' } }; _links = @{ html = @{ href = 'https://dev.azure.com/' } } }
}

$codePush = @{
    id              = [guid]::NewGuid().ToString()
    eventType       = 'git.push'
    message         = @{ text = 'Jamal Hartnett pushed updates to fabrikam-frontend:develop' }
    detailedMessage = @{ markdown = "Jamal Hartnett pushed a commit to **fabrikam-frontend**:develop`n`n- Send the stylesheet upload base64-encoded" }
    resource        = @{ repository = @{ name = 'fabrikam-frontend'; project = @{ name = 'Fabrikam' } } }
}

# A real pull request from the project, so the list has a starred pull request; a new repository ID each run gets past the announce-once check.
function New-ActiveListPreview {
    $token = (Get-Content -Path $settingsPath -Raw | ConvertFrom-Json).Values.AzDo__PersonalAccessToken
    if (-not $token) { throw "Set AzDo__PersonalAccessToken in $settingsPath to preview the active pull request list." }

    $projectUrl = "https://dev.azure.com/$([uri]::EscapeDataString($ActiveListOrganization))/$([uri]::EscapeDataString($ActiveListProject))"
    $authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$token"))
    $listed = Invoke-RestMethod -Headers @{ Authorization = $authorization } `
        -Uri "$projectUrl/_apis/git/pullrequests?searchCriteria.status=active&`$top=100&api-version=7.1"
    $newest = $listed.value | Where-Object { -not $_.isDraft } | Sort-Object { [DateTimeOffset]$_.creationDate } -Descending | Select-Object -First 1
    if (-not $newest) { throw "$ActiveListOrganization/$ActiveListProject has no active pull request that is not a draft." }

    az storage entity insert --table-name $ruleTable --connection-string $azurite --if-exists replace --output none --entity `
        "PartitionKey=$ActiveListOrganization" RowKey=card-preview-active-list Active=true Active@odata.type=Edm.Boolean `
        Priority=-1 Priority@odata.type=Edm.Int32 "Project=$ActiveListProject" 'Trigger=git.pullrequest.*' Action=Include NotificationType=ActiveList `
        'Note=Local card previews: the active pull request list to the fallback workflow'

    $payload = Read-Sample 'git.pullrequest.created.json'
    $payload.resourceContainers.account.baseUrl = $payload.resourceContainers.collection.baseUrl = "https://dev.azure.com/$ActiveListOrganization/"
    $resource = $payload.resource
    $resource.pullRequestId = $newest.pullRequestId
    $resource.title = $newest.title
    $resource.isDraft = $false
    $resource.sourceRefName = $newest.sourceRefName
    $resource.targetRefName = $newest.targetRefName
    $resource.creationDate = $newest.creationDate
    $resource.createdBy = @{ displayName = $newest.createdBy.displayName }
    $resource.reviewers = @()
    $resource.repository.id = [guid]::NewGuid().ToString()
    $resource.repository.name = $newest.repository.name
    $resource.repository.project.name = $ActiveListProject
    $payload
}

$previews = [ordered]@{
    'Build succeeded'           = New-BuildPreview 'succeeded'
    'Build partially succeeded' = New-BuildPreview 'partiallySucceeded'
    'Build failed'              = New-BuildPreview 'failed'
    'Build stopped'             = New-BuildPreview 'canceled'
    'Build, minimal sample'     = Read-Sample 'build.complete.minimal.json'
    'Pull request ready'        = $pullRequest
    'Work item updated'         = $workItem
    'Code pushed (other events)' = $codePush
}
if ($previewActiveList) { $previews['Active pull request list'] = New-ActiveListPreview }

# What the card is about: the pull request, the build, or the event's own summary.
function Get-PreviewTitle([hashtable] $payload) {
    $resource = $payload.resource
    if ($resource.title) { return "!$($resource.pullRequestId) $($resource.title)" }
    if ($resource.buildNumber) { return (@($resource.definition.name, $resource.buildNumber, $resource.result) | Where-Object { $_ }) -join ' · ' }
    $payload.message.text
}

try {
    $previews.GetEnumerator() | ForEach-Object {
        $response = Invoke-WebRequest -Uri $Uri -Method Post -ContentType 'application/json' -SkipHttpErrorCheck `
            -Headers @{ 'x-correlation-id' = "lightkeeper-preview-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))" } `
            -Body ($_.Value | ConvertTo-Json -Depth 20)
        [pscustomobject]@{ Card = $_.Key; Title = Get-PreviewTitle $_.Value; StatusCode = $response.StatusCode; Outcome = ($response.Content | ConvertFrom-Json).outcome }
    } | Format-Table -AutoSize
}
finally {
    # Ends the host together with its worker process and window.
    if ($functionsHost) { $functionsHost.Kill($true) }
}
