// Future Azure resource blueprint — NOT referenced by the mock release pipeline.
// Review network/private endpoints, policy and subscriptions before any real deployment.
targetScope = 'resourceGroup'

@allowed(['dev', 'uat', 'prod'])
param environment string
param location string = resourceGroup().location
param workloadPrefix string = 'clearcase'
param vaultName string
param identityName string

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: {
    application: workloadPrefix
    environment: environment
    dataClassification: 'confidential'
  }
}

// Azure RBAC + default-deny network posture. A private endpoint/DNS and networking
// module must be added before connecting any real runtime to this vault.
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    accessPolicies: []
  }
  tags: {
    application: workloadPrefix
    environment: environment
  }
}

// Key Vault Secrets User built-in role; scope only this vault.
resource vaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, runtimeIdentity.id, 'KeyVaultSecretsUser')
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: runtimeIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output managedIdentityResourceId string = runtimeIdentity.id
output vaultResourceId string = vault.id
