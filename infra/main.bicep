targetScope = 'subscription'

// Storage account and Key Vault names are globally unique and capped at 24 characters: AppBase (11) + Environment (3) + Region (8) + type (2).
@description('Application base name; first part of every resource name. Must be unique to your deployment, such as lkcontoso.')
@minLength(2)
@maxLength(11)
param p_AppBase string

@description('Environment code used in resource names and tags.')
@allowed(['PRD', 'DMO', 'UAT', 'QAT', 'DEV'])
param p_Environment string

@description('Datacenter reference used in resource names, such as useast2.')
@minLength(2)
@maxLength(8)
param p_Region string

@description('Azure region for the resource group and its resources, such as eastus2.')
param p_Location string

@description('Resource tags applied to all resources in this deployment.')
param p_Tags object

@secure()
@description('Teams Workflow webhook URL. Contains its signature secret.')
param p_TeamsWorkflowFallbackWebhookUrl string

@description('Entra ID object IDs of users granted Key Vault Secrets Officer on the Key Vault.')
param p_KeyVaultSecretsOfficerIds array = []

var v_BaseName = toLower('${p_AppBase}${p_Environment}${p_Region}')

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${v_BaseName}rg'
  location: p_Location
  tags: p_Tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: resourceGroup
  params: {
    p_BaseName: v_BaseName
    p_Location: p_Location
    p_Tags: p_Tags
    p_TeamsWorkflowFallbackWebhookUrl: p_TeamsWorkflowFallbackWebhookUrl
    p_KeyVaultSecretsOfficerIds: p_KeyVaultSecretsOfficerIds
  }
}

output o_ResourceGroupName string = resourceGroup.name
output o_FunctionAppName string = resources.outputs.o_FunctionAppName
output o_FunctionAppHostName string = resources.outputs.o_FunctionAppHostName
output o_KeyVaultName string = resources.outputs.o_KeyVaultName
