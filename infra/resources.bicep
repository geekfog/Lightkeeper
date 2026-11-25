targetScope = 'resourceGroup'

@description('Shared name prefix: <AppBase><env><Region>, built in main.bicep. At most 22 characters so the storage account name fits 24.')
@minLength(1)
@maxLength(22)
param p_BaseName string

@description('Azure region for all resources.')
param p_Location string = resourceGroup().location

@description('Resource tags applied to all resources in this deployment.')
param p_Tags object

@secure()
@description('Teams Workflow webhook URL. Contains its signature secret.')
param p_TeamsWorkflowFallbackWebhookUrl string

@description('Entra ID object IDs of users granted Key Vault Secrets Officer, so they can set the secrets the app loads at startup. Additive: removing an ID does not revoke it.')
param p_KeyVaultSecretsOfficerIds array = []

@description('Memory per Flex Consumption instance, in MB.')
@allowed([512, 2048, 4096])
param p_InstanceMemoryMB int = 2048

@description('Maximum Flex Consumption instances.')
@minValue(40)
@maxValue(1000)
param p_MaximumInstanceCount int = 40

var v_DeploymentContainerName = 'app-package'
var v_Runtime = { name: 'dotnet-isolated', version: '10.0' }

// Storage Blob Data Owner, Storage Queue Data Contributor, Storage Table Data Contributor: identity-based host storage and deployment package.
var v_StorageRoleIds = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
]

var v_KeyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var v_KeyVaultSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: '${p_BaseName}st'
  location: p_Location
  tags: p_Tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }

  resource blobService 'blobServices' = {
    name: 'default'

    resource deploymentContainer 'containers' = {
      name: v_DeploymentContainerName
    }
  }
}

// Access is by Azure role (RBAC), not access policies, so role assignments made outside this template survive each deployment.
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${p_BaseName}kv'
  location: p_Location
  tags: p_Tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    softDeleteRetentionInDays: 90
  }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${p_BaseName}oiw'
  location: p_Location
  tags: p_Tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${p_BaseName}oiw'
  location: p_Location
  tags: p_Tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${p_BaseName}fcnasp'
  location: p_Location
  tags: p_Tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: '${p_BaseName}fcn'
  location: p_Location
  tags: p_Tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: { minTlsVersion: '1.2' }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${v_DeploymentContainerName}'
          authentication: { type: 'SystemAssignedIdentity' }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: p_MaximumInstanceCount
        instanceMemoryMB: p_InstanceMemoryMB
      }
      runtime: v_Runtime
    }
  }
}

resource storageRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for roleId in v_StorageRoleIds: {
  name: guid(storage.id, functionApp.id, roleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource keyVaultAppRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionApp.id, v_KeyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', v_KeyVaultSecretsUserRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultOfficerRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for objectId in p_KeyVaultSecretsOfficerIds: {
  name: guid(keyVault.id, objectId, v_KeyVaultSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', v_KeyVaultSecretsOfficerRoleId)
    principalId: objectId
    principalType: 'User'
  }
}]

// Replaces all app settings on every deployment, so every setting the app needs is listed here.
resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    AzureWebJobsStorage__credential: 'managedidentity'
    AzureWebJobsStorage__blobServiceUri: storage.properties.primaryEndpoints.blob
    AzureWebJobsStorage__queueServiceUri: storage.properties.primaryEndpoints.queue
    AzureWebJobsStorage__tableServiceUri: storage.properties.primaryEndpoints.table
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    TeamsWorkflow__FallbackWebhookUrl: p_TeamsWorkflowFallbackWebhookUrl
    KeyVault__Uri: keyVault.properties.vaultUri
  }
  dependsOn: [storageRoles, keyVaultAppRole]
}

output o_FunctionAppName string = functionApp.name
output o_FunctionAppHostName string = functionApp.properties.defaultHostName
output o_KeyVaultName string = keyVault.name
