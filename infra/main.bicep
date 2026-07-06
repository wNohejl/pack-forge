// PackForge cloud footprint — scale-to-zero Container Apps, blob storage with a
// lifecycle policy, a build queue, and a KEDA-scaled build job. Cheap by design:
// the app and the build job both idle at zero replicas.
targetScope = 'resourceGroup'

@description('Deployment environment name, used as a resource-name prefix.')
param environmentName string = 'packforge'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('PostgreSQL connection string (from Key Vault or azd env). Empty uses the in-cluster fallback only for demos.')
@secure()
param postgresConnectionString string

var tags = { 'azd-env-name': environmentName, app: 'packforge' }
var storageName = toLower('${environmentName}${uniqueString(resourceGroup().id)}')

// ---- Storage: blobs (models, packages, migrated) + build queue ----
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
}

resource blob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for name in ['models', 'packages', 'migrated']: {
    parent: blob
    name: name
  }
]

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource buildQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'builds'
}

// Lifecycle: packages move to Cool after 30 days (cost control, per PHASES Phase 3).
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'packages-to-cool'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: [ 'blockBlob' ], prefixMatch: [ 'packages/' ] }
            actions: { baseBlob: { tierToCool: { daysAfterModificationGreaterThan: 30 } } }
          }
        }
      ]
    }
  }
}

// ---- Observability ----
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${environmentName}-logs'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${environmentName}-ai'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

// ---- Container Apps environment ----
resource acaEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${environmentName}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${environmentName}-id'
  location: location
  tags: tags
}

// Managed identity gets data-plane access to storage — no connection-string secrets for blobs.
var blobContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var queueContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, blobContributor)
  properties: { roleDefinitionId: blobContributor, principalId: identity.properties.principalId, principalType: 'ServicePrincipal' }
}

resource queueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, queueContributor)
  properties: { roleDefinitionId: queueContributor, principalId: identity.properties.principalId, principalType: 'ServicePrincipal' }
}

// ---- The web app: scale 0..3 on HTTP concurrency ----
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${environmentName}-web'
  location: location
  tags: union(tags, { 'azd-service-name': 'web' })
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    managedEnvironmentId: acaEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto' }
      secrets: [ { name: 'postgres', value: postgresConnectionString } ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: 'mcr.microsoft.com/k8se/quickstart:latest' // replaced by azd/CI with the built image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ConnectionStrings__Postgres', secretRef: 'postgres' }
            { name: 'ConnectionStrings__Blob', value: 'https://${storage.name}.blob.${environment().suffixes.storage}' }
            { name: 'Storage__QueueUri', value: 'https://${storage.name}.queue.${environment().suffixes.storage}' }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [ { name: 'http', http: { metadata: { concurrentRequests: '50' } } } ]
      }
    }
  }
}

output WEB_URI string = 'https://${web.properties.configuration.ingress.fqdn}'
output STORAGE_ACCOUNT string = storage.name
output APPLICATIONINSIGHTS_CONNECTION_STRING string = appInsights.properties.ConnectionString
