// The package build worker as an ACA Job scaled by build-queue depth (KEDA).
// Deployed separately so the build path scales independently of web traffic —
// idles at zero, wakes on a queued message, dies when the queue drains.
targetScope = 'resourceGroup'

param environmentName string = 'packforge'
param location string = resourceGroup().location
param acaEnvId string
param identityId string
param storageAccountName string
@secure()
param postgresConnectionString string

resource buildJob 'Microsoft.App/jobs@2024-03-01' = {
  name: '${environmentName}-build'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identityId}': {} } }
  properties: {
    environmentId: acaEnvId
    configuration: {
      triggerType: 'Event'
      replicaTimeout: 1800
      secrets: [ { name: 'postgres', value: postgresConnectionString } ]
      eventTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
        scale: {
          minExecutions: 0
          maxExecutions: 5
          pollingInterval: 15
          rules: [
            {
              name: 'queue-depth'
              type: 'azure-queue'
              metadata: { queueName: 'builds', queueLength: '1', accountName: storageAccountName }
              identity: identityId
            }
          ]
        }
      }
    }
    template: {
      containers: [
        {
          name: 'build'
          image: 'mcr.microsoft.com/k8se/quickstart-jobs:latest' // replaced by CI with the worker image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ConnectionStrings__Postgres', secretRef: 'postgres' }
            { name: 'PackForge__Mode', value: 'BuildWorker' }
          ]
        }
      ]
    }
  }
}
