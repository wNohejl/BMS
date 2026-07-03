@description('Environment name — used as suffix on all resource names')
param environment string = 'dev'

@description('Email address for the cost alert notifications')
param budgetContactEmail string = 'your@email.com'

@description('GitHub username — images are pulled from ghcr.io/<username> (free)')
param githubUsername string = 'YOUR_GITHUB_USERNAME'

var location = resourceGroup().location
var suffix = environment

// IoT Hub — F1 free tier
// Limit: 8,000 msgs/day at 0.5KB chunks. Sufficient at 30s intervals with batching.
// One F1 hub allowed per Azure subscription.
// UPGRADE: change 'F1' to 'S1' when first paying customer onboards.
resource iotHub 'Microsoft.Devices/IotHubs@2023-06-30' = {
  name: 'edgemonitor-iothub-${suffix}'
  location: location
  sku: {
    name: 'F1'
    capacity: 1
  }
  properties: {}
}

// Container Apps Environment
resource caEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: 'edgemonitor-env-${suffix}'
  location: location
  properties: {}
}

// Container App (API)
// Free tier: 180,000 vCPU-seconds + 2M requests/month per subscription.
// min-replicas=0 scales to zero — no charge when idle (~5–15s cold start).
// Images pulled from ghcr.io (free) — no ACR needed.
// UPGRADE: set minReplicas to 1 when first paying customer onboards.
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'edgemonitor-api-${suffix}'
  location: location
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
    }
    template: {
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
      containers: [
        {
          name: 'api'
          image: 'ghcr.io/${githubUsername}/edgemonitor-api:latest'
          resources: {
            cpu: json('0.5')
            memory: '1.0Gi'
          }
        }
      ]
    }
  }
}

// SignalR — Free_F1
// Limit: 20 concurrent connections. Fine for solo dev and simple demos.
// UPGRADE: change 'Free_F1' to 'Standard_S1' the day before any multi-viewer demo.
resource signalR 'Microsoft.SignalRService/signalR@2023-08-01-preview' = {
  name: 'edgemonitor-signalr-${suffix}'
  location: location
  sku: {
    name: 'Free_F1'
    capacity: 1
  }
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
    ]
  }
}

// Static Web App — free tier (permanently free, no upgrade needed)
resource staticWebApp 'Microsoft.Web/staticSites@2022-09-01' = {
  name: 'edgemonitor-dashboard-${suffix}'
  location: 'eastus2' // Static Web Apps not available in all regions
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: 'https://github.com/${githubUsername}/EdgeMonitor'
    branch: 'main'
    buildProperties: {
      appLocation: 'dashboard/EdgeMonitor.Dashboard'
      outputLocation: 'wwwroot'
    }
  }
}

// Cost Alert — triggers at $8 (80% of a $10 budget).
// Any charge on a free-tier stack is unexpected and worth investigating.
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'edgemonitor-budget-${suffix}'
  properties: {
    category: 'Cost'
    amount: 10
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-08-01'
    }
    notifications: {
      overBudget: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: [
          budgetContactEmail
        ]
      }
    }
  }
}

output iotHubName string = iotHub.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output dashboardUrl string = staticWebApp.properties.defaultHostname
output signalRName string = signalR.name
