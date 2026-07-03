#EdgeMonitor — Azure BMS Edge Control Platform

> A lightweight Building Management System edge monitoring platform built with C++ (IoT Edge), C#/.NET (API + SignalR), Blazor (dashboard), and PostgreSQL — deployed entirely on Azure.

---

## Table of Contents

1. [Project Goals](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#project-goals)
2. [Architecture Overview](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#architecture-overview)
3. [Cost Model](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#cost-model)
4. [Risk Register](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#risk-register)
5. [Azure Services Setup](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#azure-services-setup)
6. [Infrastructure as Code (Bicep)](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#infrastructure-as-code-bicep)
7. [Repository Structure](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#repository-structure)
8. [Module 1 — C++ IoT Edge Agent](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#module-1--cpp-iot-edge-agent)
9. [Module 2 — C# .NET API (Container App)](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#module-2--c-net-api-container-app)
10. [Module 3 — Azure SignalR Real-Time Layer](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#module-3--azure-signalr-real-time-layer)
11. [Module 4 — Blazor Dashboard (Static Web App)](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#module-4--blazor-dashboard-static-web-app)
12. [Module 5 — Azure Database for PostgreSQL](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#module-5--azure-database-for-postgresql)
13. [CI/CD — Azure DevOps Pipeline](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#cicd--azure-devops-pipeline)
14. [Local Development Setup (Zero Azure Cost)](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#local-development-setup-zero-azure-cost)
15. [Build Order & Milestones](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#build-order--milestones)
16. [Business Packaging Notes](https://claude.ai/chat/6941890b-70ee-4e29-a5dd-9eb9065d8ab9#business-packaging-notes)

---

## Project Goals

**Career:** Demonstrate C++ ↔ C#/.NET integration, real-time messaging (SignalR/WebSockets), Azure IoT Edge, and full-stack cloud deployment — directly targeting the Schneider Electric Senior Software Engineer role. The Azure stack mirrors Schneider's own EcoStruxure platform, which runs on Azure IoT infrastructure.

**Portfolio:** A working, documented, GitHub-hosted project demoed live via a public Azure Static Web App URL. The ISensorReader abstraction layer (see Module 1) shows production architectural thinking, not just prototype code.

**Business:** A modular, low-cost edge monitoring platform sold through HVAC contractors and property management firms — not direct to SMBs. Channel sales removes cold-call burden and leverages existing customer relationships. Target: $49–99/month per building, sold as a white-label add-on to contractor service contracts.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                          EDGE LAYER                              │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │            C++ IoT Edge Module (Docker container)          │  │
│  │                                                            │  │
│  │   ISensorReader (interface)                                │  │
│  │   ├── SimulatedSensorReader   ← v1 (portfolio)            │  │
│  │   └── BACnetSensorReader      ← v1.5 (real buildings)     │  │
│  │                                                            │  │
│  │   Batches readings → TelemetryPublisher (with quota       │  │
│  │   guard + local queue fallback)                            │  │
│  └──────────────────────────┬─────────────────────────────────┘  │
└─────────────────────────────│────────────────────────────────────┘
                              │ MQTT (batched payloads — F1 free tier in portfolio phase)
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                         AZURE CLOUD                              │
│                                                                  │
│  ┌──────────────┐  event   ┌───────────────────────────────┐    │
│  │ Azure IoT    │──trigger─▶  C# .NET Container App        │    │
│  │ Hub          │          │  min-replicas=0 (free)        │    │
│  │ (F1 free →  │          │  scales to zero when idle     │    │
│  │  S1 at prod) │          │  • REST API (external)        │    │
│  └──────────────┘          │  • SignalR hub                │    │
│                            │  • IoTHubListenerService      │    │
│                            │  • FileSystemListenerService  │    │
│                            └──────────┬──────────┬─────────┘    │
│                                       │          │              │
│                              ┌────────▼──┐  ┌────▼───────────┐  │
│                              │ Azure DB  │  │ Azure SignalR  │  │
│                              │ Postgres  │  │ (Free_F1 —    │  │
│                              │ (TenantId │  │  20 conn cap,  │  │
│                              │  from day │  │  upgrade when  │  │
│                              │  one)     │  │  demoing)      │  │
│                              └───────────┘  └────────┬───────┘  │
└──────────────────────────────────────────────────────│──────────┘
                                                       │ WebSocket
                                                       ▼
                                      ┌────────────────────────────┐
                                      │  Blazor WASM Dashboard     │
                                      │  (Azure Static Web App)    │
                                      │  • Live sensor tiles       │
                                      │  • Energy cost calculator  │
                                      │  • Plain-English alerts    │
                                      │  • Monthly PDF report      │
                                      └────────────────────────────┘
```

**Data flow summary:**

1. C++ edge module reads sensors via `ISensorReader` → batches readings → publishes to Azure IoT Hub S1 via MQTT (with quota guard and local queue fallback)
2. IoT Hub triggers `IoTHubListenerService` in the C# Container App via Event Hub-compatible endpoint
3. C# API persists readings to PostgreSQL (with `TenantId` on all entities), evaluates alert thresholds, and broadcasts via Azure SignalR
4. Blazor dashboard subscribes to SignalR for real-time updates; queries REST API for history and reports

**Local dev data flow (zero Azure cost):**

1. C++ agent runs with `--local` flag → writes JSON batches to `/tmp/edgemonitor/readings/`
2. `FileSystemListenerService` polls that directory and feeds the same pipeline
3. PostgreSQL runs in Docker locally; SignalR is replaced by an in-process `IHubContext` mock

---

## Cost Model

The strategy is free-first at every layer. Every service below has a free tier that covers portfolio and demo work. Upgrade triggers are marked clearly — you only pay when you have a reason to.

### Phase 1: Local development — $0/month

|Service|Approach|Cost|
|---|---|---|
|IoT Hub|Not used — `--local` flag writes JSON to disk|$0|
|Container App|Runs locally via `dotnet run`|$0|
|SignalR|In-process hub (no Azure service needed)|$0|
|PostgreSQL|Docker container on your machine|$0|
|Static Web App|Runs locally via `dotnet watch`|$0|
|Container Registry|Not needed locally|$0|
|**Total**||**$0**|

### Phase 2: Cloud portfolio / public demo — $0/month

Everything runs on free tiers. Throughput is limited but sufficient for a portfolio demo with simulated data.

|Service|Free tier|Limit to know|
|---|---|---|
|IoT Hub F1|1 hub per subscription|8,000 msgs/day at 0.5KB chunks|
|Container Apps|180K vCPU-sec + 2M requests/month free|Scales to zero — ~5–15s cold start|
|SignalR Free_F1|20 concurrent connections|Enough for solo demo; not for multi-viewer|
|PostgreSQL Flexible|No free tier — use Neon.tech free instead|0.5 GB storage free|
|Container Registry|No free tier — use GitHub Container Registry (ghcr.io) instead|Free for public repos|
|Static Web App|Free tier|$0 always|
|**Total**||**$0**|

> **IoT Hub F1 throughput strategy:** The F1 free tier supports 8,000 messages at 0.5KB chunks per day. At a 30-second read interval batching 5 sensor readings into one payload (~0.3KB), you generate ~2,880 messages per day — well within the limit. Do not read more frequently than every 20–30 seconds while on F1. If `client.send_message()` hangs, the `403002 IoTHubQuotaExceeded` error has triggered — the `TelemetryPublisher` quota guard handles this by queuing locally until midnight UTC resets the counter.

> **SignalR 20-connection cap:** Free_F1 allows 20 concurrent open connections. For solo development and a simple portfolio demo this is fine. If you're showing the demo to multiple people simultaneously (e.g. a Schneider interview), upgrade to Standard_S1 (~$40/month) the day before and downgrade after.

> **PostgreSQL alternative — Neon.tech:** Azure PostgreSQL Flexible Server has no free tier (~$15/month minimum). Use [Neon.tech](https://neon.tech/) instead — their free tier gives 0.5GB storage and a standard PostgreSQL 15 connection string that works identically with Npgsql/EF Core. Switch to Azure PostgreSQL Flexible Server only when you have a paying customer.

> **Container Registry alternative — GitHub Container Registry (ghcr.io):** Azure Container Registry Basic costs ~$5/month. GitHub Container Registry is free for public repos and works with Docker and Azure IoT Edge. Push images to `ghcr.io/your-username/edgemonitor-api` instead of Azure ACR during the portfolio phase.

### Upgrade triggers — flip these when the time comes

|Trigger|Upgrade|Cost added|
|---|---|---|
|Live demo with multiple viewers|SignalR Free → Standard_S1|+$40/month|
|First paying customer|IoT Hub F1 → S1|+$25/month|
|First paying customer|Neon.tech → Azure PostgreSQL B1ms|+$15/month|
|First paying customer|ghcr.io → Azure Container Registry Basic|+$5/month|
|10+ paying buildings|Container App min-replicas 0 → 1|+$10–13/month|

### Per-building production cost (at scale, after upgrades)

|Scenario|Azure cost|Revenue at $49/mo|Margin|
|---|---|---|---|
|1 building|~$45/month|$49|~9%|
|10 buildings|~$80/month (shared hub)|$490|~84%|
|50 buildings|~$200/month|$2,450|~92%|

### Azure Cost Alert — set this even on free tier

```bash
az consumption budget create \
  --budget-name edgemonitor-budget \
  --amount 10 \
  --time-grain Monthly \
  --resource-group rg-edgemonitor \
  --notifications "[{\"enabled\":true,\"operator\":\"GreaterThan\",\"threshold\":80,\"contactEmails\":[\"your@email.com\"]}]"
```

Set at $10 — any unexpected charge on what should be a $0 stack is worth knowing about immediately.

---

## Risk Register

Known risks, ranked by priority. Revisit this before each milestone.

|ID|Risk|Likelihood|Impact|Mitigation|
|---|---|---|---|---|
|R1|IoT Hub F1 quota exhaustion|Medium|Medium|Batch readings (one payload per 30s); implement `403002` handler with local queue fallback; quota resets at midnight UTC. Upgrade to S1 at first paying customer.|
|R2|BACnet integration complexity|High|High|Abstracted behind `ISensorReader`; defer real BACnet to v1.5; budget 4–8 weeks when you reach a real building|
|R3|Azure cost creep|Low (free tier)|Medium|Local dev = $0; cloud portfolio = $0 on free tiers; Cost Alert set at $10; Neon.tech replaces Azure PostgreSQL; ghcr.io replaces ACR|
|R4|Solo dev bandwidth|Medium|High|One milestone at a time — don't start M2 until M1 is committed and working|
|R5|C++ skills gap (Azure IoT C SDK)|Medium|Medium|Spend 1–2 weeks on SDK samples before starting M1; learning is front-loaded|
|R6|SignalR 20-connection free tier cap|High|Low|Document it; upgrade to Standard before any live demo|
|R7|Direct SMB sales difficulty|Medium|High|Channel sales via HVAC contractors and property managers — not cold outreach to building owners|
|R8|Large competitor downmarket entry|Low|High|Move fast; customer relationships are the moat; build on open protocols (BACnet, MQTT)|
|R9|On-site hardware variability|Low (dev) / High (prod)|High|BACnet sniffer during first real install; budget 2× estimated integration time; `ISensorReader` abstraction limits blast radius|

---

## Azure Services Setup

### Prerequisites

- Azure CLI installed: `winget install Microsoft.AzureCLI` (Windows) or `brew install azure-cli` (Mac)
- Azure subscription — a free account works. Create at [azure.microsoft.com/free](https://azure.microsoft.com/free) ($200 credit + 12 months of select free services)
- Docker Desktop installed (for local PostgreSQL and IoT Edge simulation)
- GitHub account (for ghcr.io container registry and GitHub Actions)
- Neon.tech account — free PostgreSQL at [neon.tech](https://neon.tech/) (no credit card required)

### 0. Prefer Bicep over manual CLI (see next section)

The commands below are for understanding what gets created. In practice, use `infra/main.bicep` to provision everything in one command. Manual provisioning introduces configuration drift that's painful to debug.

### 1. Create a Resource Group

```bash
az login
az group create --name rg-edgemonitor --location eastus
```

### 2. Azure IoT Hub — F1 free tier (sufficient for portfolio phase)

```bash
# F1 free tier: 8,000 messages/day at 0.5KB chunks.
# At 30-second intervals batching 5 readings (~0.3KB/payload) = ~2,880 msgs/day. Safely under limit.
# One F1 hub allowed per Azure subscription.
az iot hub create \
  --name edgemonitor-iothub \
  --resource-group rg-edgemonitor \
  --sku F1 \
  --partition-count 2

# Register an edge device
az iot hub device-identity create \
  --hub-name edgemonitor-iothub \
  --device-id edge-device-01 \
  --edge-enabled

# Save this connection string — needed for the edge module
az iot hub device-identity connection-string show \
  --hub-name edgemonitor-iothub \
  --device-id edge-device-01

# UPGRADE TRIGGER: when first paying customer onboards
# az iot hub update --name edgemonitor-iothub --resource-group rg-edgemonitor --sku S1
```

### 3. Container Registry — GitHub Container Registry (free, replaces Azure ACR)

No Azure resource needed. Use `ghcr.io` which is free for public repos.

```bash
# Authenticate Docker to ghcr.io (one-time setup)
echo $GITHUB_TOKEN | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin

# Push images like this (used in CI/CD pipeline):
# docker tag edgemonitor-api ghcr.io/YOUR_GITHUB_USERNAME/edgemonitor-api:latest
# docker push ghcr.io/YOUR_GITHUB_USERNAME/edgemonitor-api:latest

# UPGRADE TRIGGER: when you want private images or Azure DevOps integration
# az acr create --name edgemonitoracr --resource-group rg-edgemonitor --sku Basic --admin-enabled true
```

### 4. Azure Container Apps — free tier, min-replicas=0

```bash
az containerapp env create \
  --name edgemonitor-env \
  --resource-group rg-edgemonitor \
  --location eastus

# min-replicas=0 = scales to zero when idle = $0 cost when not in use
# Free tier covers 180,000 vCPU-seconds + 2M requests/month
# Cold start on first request: ~5–15 seconds (acceptable for portfolio)
az containerapp create \
  --name edgemonitor-api \
  --resource-group rg-edgemonitor \
  --environment edgemonitor-env \
  --image mcr.microsoft.com/azuredocs/containerapps-helloworld:latest \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 3

# UPGRADE TRIGGER: first paying customer — eliminate cold starts
# az containerapp update --name edgemonitor-api --resource-group rg-edgemonitor --min-replicas 1
```

### 5. Azure SignalR Service — Free_F1 (20 concurrent connections)

```bash
az signalr create \
  --name edgemonitor-signalr \
  --resource-group rg-edgemonitor \
  --sku Free_F1 \
  --service-mode Default

# UPGRADE TRIGGER: day before any multi-viewer demo or interview
# az signalr update --name edgemonitor-signalr --resource-group rg-edgemonitor --sku Standard_S1
# Downgrade after: az signalr update ... --sku Free_F1
```

### 6. PostgreSQL — Neon.tech free tier (replaces Azure PostgreSQL)

Azure PostgreSQL Flexible Server has no free tier (~$15/month minimum). Use Neon.tech instead — it's free PostgreSQL 15 with a standard connection string.

```
1. Go to https://neon.tech and create a free account (no credit card)
2. Create a new project: "edgemonitor"
3. Create a database: "edgemonitordb"
4. Copy the connection string — it looks like:
   postgresql://edgeadmin:<password>@ep-xxx.us-east-2.aws.neon.tech/edgemonitordb?sslmode=require
5. This connection string works identically with Npgsql and EF Core
```

Use this in `appsettings.Development.json` and `appsettings.Production.json` (stored as a Container App secret).

```bash
# UPGRADE TRIGGER: first paying customer (need Azure-native connectivity + backups)
# az postgres flexible-server create \
#   --name edgemonitor-db --resource-group rg-edgemonitor \
#   --location eastus --admin-user edgeadmin --admin-password <password> \
#   --sku-name Standard_B1ms --tier Burstable --storage-size 32 --version 15
```

### 7. Azure Static Web Apps — free tier

Create via Azure Portal linked to your GitHub repo. The Static Web Apps free tier is permanently free with no upgrade trigger needed.

```bash
az staticwebapp create \
  --name edgemonitor-dashboard \
  --resource-group rg-edgemonitor \
  --source https://github.com/YOUR_GITHUB_USERNAME/EdgeMonitor \
  --location eastus2 \
  --branch main \
  --app-location "dashboard/EdgeMonitor.Dashboard" \
  --output-location "wwwroot"
```

Or create via Azure Portal → Static Web Apps → Connect to GitHub repo. The portal wizard is simpler for first-time setup.

---

## Infrastructure as Code (Bicep)

**This is required, not optional.** Every tear-down/rebuild cycle without Bicep costs 20–30 minutes of manual CLI work and introduces configuration drift. A working Bicep file is also a strong portfolio artifact — it shows you understand IaC, which maps directly to the DevOps practices in both target roles.

### One-command provision

```bash
az deployment group create \
  --resource-group rg-edgemonitor \
  --template-file infra/main.bicep \
  --parameters @infra/parameters.json
```

### One-command teardown (pause between work sessions to save cost)

```bash
az group delete --name rg-edgemonitor --yes --no-wait
# Reprovision with the deploy command above when you resume
```

### `infra/main.bicep` — free-tier starter template

This provisions everything that has a free tier in Azure. PostgreSQL (Neon.tech) and Container Registry (ghcr.io) are handled outside Bicep — see their sections above.

```bicep
@description('Environment name — used as suffix on all resource names')
param environment string = 'dev'

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
}

// Container Apps Environment
resource caEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: 'edgemonitor-env-${suffix}'
  location: location
  properties: {}
}

// Container App (API)
// Free tier: 180,000 vCPU-seconds + 2M requests/month per subscription.
// min-replicas=0 scales to zero — no charge when idle.
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
      containers: [{
        name: 'api'
        image: 'ghcr.io/YOUR_GITHUB_USERNAME/edgemonitor-api:latest'
        resources: { cpu: '0.5', memory: '1.0Gi' }
      }]
    }
  }
}

// SignalR — Free_F1
// Limit: 20 concurrent connections. Fine for solo dev and simple demos.
// UPGRADE: change 'Free_F1' to 'Standard_S1' day before any multi-viewer demo.
resource signalR 'Microsoft.SignalRService/signalR@2023-08-01-preview' = {
  name: 'edgemonitor-signalr-${suffix}'
  location: location
  sku: {
    name: 'Free_F1'
    capacity: 1
  }
  properties: { serviceMode: 'Default' }
}

// Static Web App — free tier (permanently free, no upgrade needed)
resource staticWebApp 'Microsoft.Web/staticSites@2022-09-01' = {
  name: 'edgemonitor-dashboard-${suffix}'
  location: 'eastus2'   // Static Web Apps not available in all regions
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: 'https://github.com/YOUR_GITHUB_USERNAME/EdgeMonitor'
    branch: 'main'
    buildProperties: {
      appLocation: 'dashboard/EdgeMonitor.Dashboard'
      outputLocation: 'wwwroot'
    }
  }
}

// Cost Alert — triggers at $8 (80% of $10 budget)
// Any charge on a free-tier stack is unexpected and worth investigating.
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'edgemonitor-budget-${suffix}'
  properties: {
    amount: 10
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2025-01-01'
    }
    notifications: {
      overBudget: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: ['your@email.com']
      }
    }
  }
}

output iotHubName string = iotHub.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output dashboardUrl string = staticWebApp.properties.defaultHostname
```

### `infra/parameters.json`

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environment": { "value": "dev" },
    "dbPassword": { "value": "" }
  }
}
```

---

## Repository Structure

```
EdgeMonitor/
│
├── edge/                              # C++ IoT Edge Module
│   ├── src/
│   │   ├── main.cpp
│   │   ├── ISensorReader.h            # Abstraction interface — key architecture decision
│   │   ├── SimulatedSensorReader.h    # v1: generates sine-wave sensor data
│   │   ├── SimulatedSensorReader.cpp
│   │   ├── BACnetSensorReader.h       # v1.5: real building integration (stub in v1)
│   │   ├── BACnetSensorReader.cpp
│   │   ├── TelemetryBatch.h           # Batches multiple readings per MQTT payload
│   │   ├── TelemetryPublisher.h       # Wraps Azure IoT C SDK; includes quota guard
│   │   └── TelemetryPublisher.cpp
│   ├── CMakeLists.txt
│   ├── Dockerfile
│   └── module.json                    # IoT Edge module manifest
│
├── api/                               # C# .NET Container App
│   ├── EdgeMonitor.Api/
│   │   ├── Controllers/
│   │   │   ├── TelemetryController.cs
│   │   │   └── AlertsController.cs
│   │   ├── Hubs/
│   │   │   └── TelemetryHub.cs        # SignalR hub (thin — logic in services)
│   │   ├── Services/
│   │   │   ├── IoTHubListenerService.cs      # Production: reads from IoT Hub
│   │   │   ├── FileSystemListenerService.cs  # Local dev: reads from /tmp/edgemonitor/
│   │   │   ├── TelemetryService.cs
│   │   │   └── AlertEvaluationService.cs
│   │   ├── Models/
│   │   │   ├── SensorReading.cs       # Includes TenantId from day one
│   │   │   └── Alert.cs
│   │   ├── Data/
│   │   │   ├── EdgeMonitorDbContext.cs
│   │   │   └── Migrations/
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   └── appsettings.Production.json
│   ├── EdgeMonitor.Api.Tests/
│   │   ├── TelemetryServiceTests.cs
│   │   └── AlertEvaluationTests.cs
│   └── EdgeMonitor.Api.sln
│
├── dashboard/                         # Blazor WASM
│   ├── EdgeMonitor.Dashboard/
│   │   ├── Pages/
│   │   │   ├── Index.razor            # Live sensor tiles (plain-English labels)
│   │   │   ├── History.razor          # Historical charts + energy cost calculator
│   │   │   ├── Alerts.razor           # Alert config — plain-English language
│   │   │   └── Report.razor           # Monthly PDF report (v1.5)
│   │   ├── Components/
│   │   │   ├── SensorTile.razor
│   │   │   ├── TelemetryChart.razor
│   │   │   ├── EnergyCostWidget.razor # "This month: +14% vs last month = ~$47 extra"
│   │   │   └── AlertBadge.razor
│   │   ├── Services/
│   │   │   ├── SignalRService.cs
│   │   │   └── ApiClient.cs
│   │   ├── wwwroot/
│   │   └── Program.cs
│   └── EdgeMonitor.Dashboard.sln
│
├── infra/                             # Infrastructure as Code — required, not optional
│   ├── main.bicep
│   └── parameters.json
│
├── .azure/
│   └── azure-pipelines.yml
│
├── deployment/
│   └── edge-deployment.json           # IoT Edge deployment manifest
│
├── docs/
│   ├── architecture.md
│   ├── cost-model.md                  # Per-building cost breakdown
│   ├── bacnet-integration.md          # Notes for v1.5 real building work
│   ├── local-dev-setup.md
│   └── diagrams/
│       └── sequence-telemetry.md
│
├── .gitignore
└── README.md
```

---

## Module 1 — C++ IoT Edge Agent

**Goal:** A containerized C++ program that generates simulated sensor telemetry and publishes batched payloads to Azure IoT Hub (F1 free tier in portfolio phase) — with a clean `ISensorReader` abstraction that accepts real BACnet hardware in v1.5 without changing the cloud pipeline. Batching keeps message count within F1 limits at 30-second intervals.

### The ISensorReader interface — the most important architectural decision in the project

```cpp
// ISensorReader.h
#pragma once
#include <vector>
#include <string>

enum class SensorType { Temperature, PowerDraw, Occupancy };

struct SensorReading {
    SensorType type;
    double value;
    std::string unit;
    std::string zoneId;
    std::string deviceId;
    long long timestampMs;
};

class ISensorReader {
public:
    virtual ~ISensorReader() = default;
    virtual std::vector<SensorReading> readAll() = 0;
    virtual bool isAvailable() const = 0;
};
```

This interface is the reason the BACnet integration (R2) stays a contained risk. `SimulatedSensorReader` implements it for v1. `BACnetSensorReader` will implement it for v1.5. The `TelemetryPublisher` never knows which one it's talking to.

### SimulatedSensorReader

```cpp
// SimulatedSensorReader.h
#pragma once
#include "ISensorReader.h"

class SimulatedSensorReader : public ISensorReader {
public:
    std::vector<SensorReading> readAll() override;
    bool isAvailable() const override { return true; }
private:
    double sineWave(double base, double amplitude, double periodSeconds);
    long long nowMs();
};
```

### BACnetSensorReader stub (v1 — interface only, not implemented)

```cpp
// BACnetSensorReader.h — stub for v1; implemented in v1.5
#pragma once
#include "ISensorReader.h"

class BACnetSensorReader : public ISensorReader {
public:
    explicit BACnetSensorReader(const std::string& networkInterface);
    std::vector<SensorReading> readAll() override;
    bool isAvailable() const override;
    // v1.5: link against bacnet-stack (github.com/bacnet-stack/bacnet-stack)
    // BACnet/IP discovery → ReadProperty → map object model to SensorReading
private:
    std::string networkInterface_;
};
```

### TelemetryBatch — solves the IoT Hub quota problem

Instead of one MQTT message per reading (which burns quota fast), batch multiple readings into a single payload up to the 256KB message limit.

```cpp
// TelemetryBatch.h
#pragma once
#include "ISensorReader.h"
#include <vector>
#include <string>

struct TelemetryBatch {
    std::vector<SensorReading> readings;
    std::string toJson() const;
    size_t estimatedSizeBytes() const;
    static constexpr size_t MAX_BATCH_BYTES = 200 * 1024; // 200KB — stay under 256KB limit
};
```

### TelemetryPublisher — with quota guard and local queue fallback

```cpp
// TelemetryPublisher.h
#pragma once
#include "TelemetryBatch.h"
#include <string>
#include <queue>

class TelemetryPublisher {
public:
    TelemetryPublisher(const std::string& connectionString, bool localMode = false);
    ~TelemetryPublisher();
    void publishBatch(const TelemetryBatch& batch);

private:
    void publishToCloud(const TelemetryBatch& batch);
    void writeToLocalFile(const TelemetryBatch& batch); // --local mode
    void handleQuotaExceeded();    // catches 403002 IoTHubQuotaExceeded
    void drainLocalQueue();        // replays queued batches after quota resets

    bool localMode_;
    void* iotHandle_;              // IOTHUB_DEVICE_CLIENT_LL_HANDLE
    std::queue<TelemetryBatch> localQueue_;
    static constexpr const char* LOCAL_OUTPUT_DIR = "/tmp/edgemonitor/readings/";
};
```

### main.cpp — wires ISensorReader to TelemetryPublisher

```cpp
// main.cpp
#include "ISensorReader.h"
#include "SimulatedSensorReader.h"
#include "BACnetSensorReader.h"
#include "TelemetryPublisher.h"
#include <iostream>
#include <thread>
#include <chrono>

int main(int argc, char* argv[]) {
    bool localMode = false;
    bool useBACnet = false;

    for (int i = 1; i < argc; i++) {
        if (std::string(argv[i]) == "--local") localMode = true;
        if (std::string(argv[i]) == "--bacnet") useBACnet = true;
        if (std::string(argv[i]) == "--output-stdout") localMode = true;
    }

    std::unique_ptr<ISensorReader> reader;
    if (useBACnet) {
        reader = std::make_unique<BACnetSensorReader>("eth0");
    } else {
        reader = std::make_unique<SimulatedSensorReader>();
    }

    std::string connStr = localMode ? "" : std::getenv("IOTHUB_CONNECTION_STRING");
    TelemetryPublisher publisher(connStr, localMode);

    while (true) {
        auto readings = reader->readAll();
        TelemetryBatch batch{readings};
        publisher.publishBatch(batch);
        std::this_thread::sleep_for(std::chrono::seconds(30)); // 30s interval = ~2,880 msgs/day
    }
}
```

> **Interval note:** At a 30-second read interval, batching all 5 sensor readings into one ~0.3KB payload, you generate approximately 2,880 messages per day — safely within the F1 free tier limit of 8,000 messages. Do not reduce the interval below 20 seconds while on F1. The `TelemetryPublisher` quota guard catches `403002 IoTHubQuotaExceeded` and queues locally until midnight UTC resets the counter.

### CMakeLists.txt

```cmake
cmake_minimum_required(VERSION 3.16)
project(EdgeMonitorAgent)

set(CMAKE_CXX_STANDARD 17)

find_package(azure_iot_sdks REQUIRED)

add_executable(edge_agent
    src/main.cpp
    src/SimulatedSensorReader.cpp
    src/BACnetSensorReader.cpp
    src/TelemetryBatch.cpp
    src/TelemetryPublisher.cpp
)

target_link_libraries(edge_agent
    iothub_client
    iothub_client_mqtt_transport
)

# v1.5: add bacnet-stack linkage here
# find_library(BACNET_LIB bacnet-stack HINTS /usr/local/lib)
# target_link_libraries(edge_agent ${BACNET_LIB})
```

### Dockerfile

```dockerfile
FROM ubuntu:22.04 AS build
RUN apt-get update && apt-get install -y \
    cmake g++ git curl \
    libssl-dev uuid-dev \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/Azure/azure-iot-sdk-c.git --recurse-submodules
RUN cmake -S azure-iot-sdk-c -B azure-iot-sdk-c/build \
    -Duse_mqtt=ON -Dskip_samples=ON \
    && cmake --build azure-iot-sdk-c/build --target install

WORKDIR /app
COPY . .
RUN cmake -S . -B build && cmake --build build

FROM ubuntu:22.04
RUN apt-get update && apt-get install -y libssl3 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/build/edge_agent /usr/local/bin/edge_agent

# Default: cloud mode. Override with --local for dev.
CMD ["edge_agent"]
```

### Build & test locally

```bash
cd edge/
cmake -S . -B build
cmake --build build

# Local mode — no Azure needed, writes to /tmp/edgemonitor/readings/
./build/edge_agent --local

# Stdout mode — prints JSON to terminal
./build/edge_agent --output-stdout

# Verify Azure connectivity (after provisioning IoT Hub S1)
IOTHUB_CONNECTION_STRING="<your-connection-string>" ./build/edge_agent
```

---

## Module 2 — C# .NET API (Container App)

**Goal:** A .NET 8 Web API with two listener services — `IoTHubListenerService` for production and `FileSystemListenerService` for local dev — feeding a shared processing pipeline.

### Initial setup

```bash
cd api/
dotnet new webapi -n EdgeMonitor.Api
dotnet new xunit -n EdgeMonitor.Api.Tests
dotnet new sln -n EdgeMonitor.Api
dotnet sln add EdgeMonitor.Api/EdgeMonitor.Api.csproj
dotnet sln add EdgeMonitor.Api.Tests/EdgeMonitor.Api.Tests.csproj
```

### NuGet packages

```bash
cd EdgeMonitor.Api/

dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Azure.Messaging.EventHubs
dotnet add package Microsoft.Azure.SignalR
dotnet add package Grpc.AspNetCore          # Uncomment in v2 for C++ direct channel
```

### Database schema — TenantId on all entities from day one

```csharp
// SensorReading.cs
public class SensorReading
{
    public int Id { get; set; }
    public string TenantId { get; set; }      // Multi-tenancy — required from day one
    public string DeviceId { get; set; }
    public string SensorType { get; set; }    // "temperature" | "power" | "occupancy"
    public double Value { get; set; }
    public string Unit { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string ZoneId { get; set; }
}

// Alert.cs
public class Alert
{
    public int Id { get; set; }
    public string TenantId { get; set; }      // Multi-tenancy
    public string ZoneId { get; set; }
    public string SensorType { get; set; }
    public double ThresholdValue { get; set; }
    public string Condition { get; set; }     // "above" | "below"
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
}
```

> **Why TenantId now:** Adding it after first customer means a migration on live data, re-testing all queries, and rewriting every API filter. Adding it now costs one extra column and 10 minutes. Do it now.

### FileSystemListenerService — the local dev mode

```csharp
// FileSystemListenerService.cs
public class FileSystemListenerService : BackgroundService
{
    private const string WatchDir = "/tmp/edgemonitor/readings/";
    private readonly TelemetryService _telemetry;
    private readonly ILogger<FileSystemListenerService> _logger;

    public FileSystemListenerService(TelemetryService telemetry,
        ILogger<FileSystemListenerService> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(WatchDir);
        using var watcher = new FileSystemWatcher(WatchDir, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        watcher.Created += async (_, e) =>
        {
            await Task.Delay(50, ct);  // brief wait for file write to complete
            var json = await File.ReadAllTextAsync(e.FullPath, ct);
            var batch = JsonSerializer.Deserialize<TelemetryBatchDto>(json);
            if (batch != null)
                await _telemetry.ProcessBatchAsync(batch, ct);
            File.Delete(e.FullPath);
        };

        await Task.Delay(Timeout.Infinite, ct);
    }
}
```

### IoTHubListenerService — production

```csharp
// IoTHubListenerService.cs
public class IoTHubListenerService : BackgroundService
{
    // Uses EventHubConsumerClient to read from IoT Hub's built-in Event Hub endpoint.
    // On each event: deserialize TelemetryBatchDto → TelemetryService.ProcessBatchAsync
    // which persists to Postgres and broadcasts via SignalR.
}
```

### Program.cs — environment-aware listener registration

```csharp
var isDev = builder.Environment.IsDevelopment();

builder.Services.AddDbContext<EdgeMonitorDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Use Azure SignalR in production; in-process hub in development
if (isDev)
    builder.Services.AddSignalR();
else
    builder.Services.AddSignalR()
        .AddAzureSignalR(builder.Configuration["AzureSignalR:ConnectionString"]);

// Register the right listener for the environment
if (isDev)
    builder.Services.AddHostedService<FileSystemListenerService>();
else
    builder.Services.AddHostedService<IoTHubListenerService>();

builder.Services.AddScoped<TelemetryService>();
builder.Services.AddScoped<AlertEvaluationService>();

// v2: gRPC direct channel from C++ agent (bypasses IoT Hub for low-latency use cases)
// builder.Services.AddGrpc();
```

### Database indexes

```csharp
// In EdgeMonitorDbContext.OnModelCreating:
modelBuilder.Entity<SensorReading>()
    .HasIndex(r => new { r.TenantId, r.ZoneId, r.TimestampUtc });

modelBuilder.Entity<SensorReading>()
    .HasIndex(r => new { r.TenantId, r.DeviceId });
```

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EdgeMonitor.Api.dll"]
```

---

## Module 3 — Azure SignalR Real-Time Layer

No custom code beyond the API — Azure SignalR acts as the managed backplane.

### Tier decision

|Scenario|Tier|Connections|Cost|
|---|---|---|---|
|Local dev|In-process (no Azure)|Unlimited|$0|
|Portfolio/testing|Free_F1|**20 max**|$0|
|Live demo / interview|Standard_S1|1,000|~$40/month|
|Production (per building)|Standard_S1|1,000|Shared across tenants|

**Upgrade before any demo with multiple viewers:**

```bash
az signalr update \
  --name edgemonitor-signalr \
  --resource-group rg-edgemonitor \
  --sku Standard_S1
```

### Hub events

|Event|Payload|Triggered by|
|---|---|---|
|`ReadingReceived`|`{ sensorType, value, unit, zoneId, tenantId, timestamp }`|New IoT Hub batch|
|`AlertTriggered`|`{ alertId, zoneId, condition, currentValue, plainEnglishMessage }`|Threshold breach|
|`DeviceStatusChanged`|`{ deviceId, status, tenantId }`|IoT Hub device twin update|

Note the `plainEnglishMessage` field on `AlertTriggered`. Dashboard shows this directly to building owners — no technical jargon. Example: `"Zone 2 temperature has been above 78°F for 20 minutes."` not `"ThresholdCondition: above, CurrentValue: 79.4"`.

---

## Module 4 — Blazor Dashboard (Static Web App)

### Initial setup

```bash
cd dashboard/
dotnet new blazorwasm -n EdgeMonitor.Dashboard
cd EdgeMonitor.Dashboard/
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

### Pages to build (in order)

1. **`Index.razor`** — live sensor tiles labeled in plain English. "Zone 2 is 74°F — normal" not "SensorType: Temperature, Value: 74.2". Subscribe to SignalR `ReadingReceived` on `OnInitializedAsync`.
2. **`History.razor`** — 30-day chart with `EnergyCostWidget.razor` showing estimated cost delta vs prior period. This is the feature property managers ask building owners about.
3. **`Alerts.razor`** — threshold configuration using plain-English descriptions. "Alert me when Zone 2 stays above 78°F for more than 15 minutes" not "Condition: above, Threshold: 78, Hysteresis: 15".

### EnergyCostWidget — the key non-technical feature

```razor
@* EnergyCostWidget.razor *@
@* Shows energy cost delta in dollars — the one metric building owners care about *@

<div class="cost-widget">
    <span class="label">This month vs last month</span>
    <span class="delta @(Delta > 0 ? "higher" : "lower")">
        @(Delta > 0 ? "+" : "")@Delta.ToString("C0") estimated
    </span>
    <span class="detail">Based on @KwhDelta kWh difference at @RatePerKwh/kWh</span>
</div>
```

---

## Module 5 — PostgreSQL (Neon.tech free → Azure when ready)

### Connection string formats

Local development (Docker):

```
Host=localhost;Database=edgemonitordb;Username=edgeadmin;Password=localpassword
```

Cloud portfolio (Neon.tech free tier):

```
Host=ep-xxx.us-east-2.aws.neon.tech;Database=edgemonitordb;Username=edgeadmin;Password=<your-password>;SslMode=Require;
```

Production upgrade (Azure PostgreSQL Flexible Server):

```
Host=edgemonitor-db.postgres.database.azure.com;Database=edgemonitordb;Username=edgeadmin;Password=<your-password>;SslMode=Require;
```

All three use the same Npgsql driver and EF Core config — only the connection string changes. This is why Neon.tech is a clean drop-in replacement during the portfolio phase.

### Local PostgreSQL via Docker

```bash
docker run -d \
  --name edgemonitor-postgres \
  -e POSTGRES_USER=edgeadmin \
  -e POSTGRES_PASSWORD=localpassword \
  -e POSTGRES_DB=edgemonitordb \
  -p 5432:5432 \
  postgres:15
```

### Neon.tech setup (cloud portfolio phase — free)

```
1. Create account at https://neon.tech (no credit card)
2. New project → name: "edgemonitor"
3. New database → name: "edgemonitordb"
4. Copy connection string from the dashboard
5. Add to Azure Container App as a secret:
   az containerapp secret set \
     --name edgemonitor-api \
     --resource-group rg-edgemonitor \
     --secrets "postgres-conn=<your-neon-connection-string>"
```

### UPGRADE: Azure PostgreSQL (at first paying customer)

```bash
az postgres flexible-server create \
  --name edgemonitor-db \
  --resource-group rg-edgemonitor \
  --location eastus \
  --admin-user edgeadmin \
  --admin-password <your-password> \
  --sku-name Standard_B1ms \
  --tier Burstable \
  --storage-size 32 \
  --version 15

az postgres flexible-server db create \
  --server-name edgemonitor-db \
  --resource-group rg-edgemonitor \
  --database-name edgemonitordb

# Run EF Core migrations against the new server — connection string swap is all that changes
dotnet ef database update
```

---

## CI/CD — Azure DevOps Pipeline

```yaml
# azure-pipelines.yml
trigger:
  branches:
    include:
      - main

stages:

  - stage: Build_Edge_Module
    jobs:
      - job: BuildAndPushCpp
        pool:
          vmImage: ubuntu-latest
        steps:
          - task: Docker@2
            displayName: Build & push C++ edge module to ghcr.io (free)
            inputs:
              command: buildAndPush
              repository: YOUR_GITHUB_USERNAME/edgemonitor-edge-agent
              dockerfile: edge/Dockerfile
              containerRegistry: ghcr.io
              tags: $(Build.BuildId)

  - stage: Build_API
    jobs:
      - job: BuildTestPush
        pool:
          vmImage: ubuntu-latest
        steps:
          - task: UseDotNet@2
            inputs:
              version: 8.x
          - script: dotnet restore api/EdgeMonitor.Api.sln
          - script: dotnet build api/EdgeMonitor.Api.sln --no-restore
          - script: dotnet test api/EdgeMonitor.Api.Tests --no-build --logger trx
          - task: Docker@2
            displayName: Build & push API image to ghcr.io (free)
            inputs:
              command: buildAndPush
              repository: YOUR_GITHUB_USERNAME/edgemonitor-api
              dockerfile: api/EdgeMonitor.Api/Dockerfile
              containerRegistry: ghcr.io
              tags: $(Build.BuildId)

  - stage: Deploy_API
    dependsOn: Build_API
    jobs:
      - job: DeployContainerApp
        steps:
          - task: AzureContainerApps@1
            inputs:
              azureSubscription: <service-connection-name>
              containerAppName: edgemonitor-api
              resourceGroup: rg-edgemonitor
              imageToDeploy: ghcr.io/YOUR_GITHUB_USERNAME/edgemonitor-api:$(Build.BuildId)
              # UPGRADE: swap ghcr.io for edgemonitoracr.azurecr.io when ACR is provisioned

  - stage: Deploy_Dashboard
    dependsOn: Build_API
    jobs:
      - job: DeployStaticWebApp
        steps:
          - script: dotnet publish dashboard/EdgeMonitor.Dashboard -c Release -o dashboard/publish
          - task: AzureStaticWebApp@0
            inputs:
              app_location: dashboard/publish/wwwroot
              azure_static_web_apps_api_token: $(AZURE_STATIC_WEB_APPS_TOKEN)
```

---

## Local Development Setup (Zero Azure Cost)

The goal is a complete inner loop that costs $0 and has no network dependency on Azure.

### Prerequisites checklist

- [ ] .NET 8 SDK
- [x] CMake 3.16+, GCC/Clang (C++17)
- [x] Docker Desktop
- [x] Azure CLI (only needed when provisioning cloud resources)
- [x] Azure DevOps account (free tier — only needed for CI/CD setup)

### Step 1 — Start local PostgreSQL

```bash
docker run -d \
  --name edgemonitor-postgres \
  -e POSTGRES_USER=edgeadmin \
  -e POSTGRES_PASSWORD=localpassword \
  -e POSTGRES_DB=edgemonitordb \
  -p 5432:5432 \
  postgres:15
```

### Step 2 — Start the C# API in local mode

```bash
cd api/EdgeMonitor.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run
# FileSystemListenerService starts watching /tmp/edgemonitor/readings/
# In-process SignalR hub starts (no Azure SignalR needed)
```

### Step 3 — Run the C++ edge agent in local mode

```bash
cd edge/
cmake -S . -B build && cmake --build build
./build/edge_agent --local
# Writes JSON batches to /tmp/edgemonitor/readings/ every 30 seconds
# FileSystemListenerService picks them up and feeds the pipeline
```

### Step 4 — Start the Blazor dashboard

```bash
cd dashboard/EdgeMonitor.Dashboard
dotnet watch run
# Dashboard opens at https://localhost:5001
# Connects to local API SignalR hub
```

You now have the full pipeline running: C++ agent → local file → C# API → local SignalR → Blazor dashboard. No Azure services. No cost.

### `appsettings.Development.json` — local, $0

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=edgemonitordb;Username=edgeadmin;Password=localpassword"
  },
  "Listener": {
    "Mode": "FileSystem",
    "LocalDirectory": "/tmp/edgemonitor/readings/"
  },
  "AzureSignalR": {
    "ConnectionString": ""
  }
}
```

### `appsettings.Production.json` — cloud free tier

```json
{
  "ConnectionStrings": {
    "Postgres": "<Neon.tech connection string — set via Azure Container App secret>"
  },
  "Listener": {
    "Mode": "IoTHub",
    "EventHubCompatibleEndpoint": "<from Azure Portal → IoT Hub → Built-in endpoints>",
    "ConsumerGroup": "$Default"
  },
  "AzureSignalR": {
    "ConnectionString": "<Free_F1 connection string — set via Azure Container App secret>"
  }
}
```

Set secrets on the Container App (keeps credentials out of source control):

```bash
az containerapp secret set \
  --name edgemonitor-api \
  --resource-group rg-edgemonitor \
  --secrets \
    "postgres-conn=<neon-connection-string>" \
    "signalr-conn=<signalr-connection-string>" \
    "iothub-endpoint=<event-hub-compatible-endpoint>"
```

---

## Build Order & Milestones

Work strictly in order. Do not start the next milestone until the current one is committed, working, and pushed to GitHub.

### Pre-work — C++ SDK familiarization (1–2 weeks before M1)

Before writing any EdgeMonitor code, run through a standalone Azure IoT C SDK sample that publishes a hardcoded JSON string to a real IoT Hub S1 instance. This front-loads the SDK learning so it doesn't block M1 mid-build.

- [ ] Azure IoT C SDK compiled and linked via CMake
- [ ] Simple "Hello IoT Hub" program sends a message and you verify it in the Azure Portal
- [ ] You understand IOTHUB_DEVICE_CLIENT_LL_HANDLE lifecycle and the `403002` quota error

### Milestone 1 — Local pipeline end-to-end (Week 1–2)

**Goal:** Sensor data flows from C++ agent to dashboard without touching Azure.

- [ ] `ISensorReader` interface defined
- [ ] `SimulatedSensorReader` generates realistic sine-wave temp/power/occupancy data
- [ ] `TelemetryBatch` serializes multiple readings to JSON
- [ ] `TelemetryPublisher` with `--local` flag writes batches to `/tmp/edgemonitor/readings/`
- [ ] `FileSystemListenerService` picks up JSON files and logs readings to console
- [ ] `SensorReading` and `Alert` EF Core models include `TenantId`; migration applied to local Postgres
- [ ] Blazor `Index.razor` shows live sensor tiles updating from local SignalR
- [ ] **Demo:** C++ agent running + dashboard open in browser, tiles update every 30 seconds

### Milestone 2 — Cloud pipeline on free tier (Week 2–3)

**Goal:** Same pipeline as M1, but data flows through Azure IoT Hub F1 (free) and Neon.tech PostgreSQL (free).

- [ ] Neon.tech project and database created; connection string saved
- [ ] Bicep provisions IoT Hub F1, Container App, SignalR Free_F1, Static Web App in one command
- [ ] Azure Cost Alert set at $10/month
- [ ] `TelemetryPublisher` sends batched MQTT payloads to IoT Hub F1 (30s interval)
- [ ] `IoTHubListenerService` reads from Event Hub-compatible endpoint
- [ ] `403002` quota exception handled with local queue fallback
- [ ] EF Core migrations run against Neon.tech PostgreSQL
- [ ] `GET /api/telemetry` returns stored readings as JSON
- [ ] Unit tests for `TelemetryService` and `AlertEvaluationService` pass
- [ ] **Demo:** Sensor readings appear in Azure Portal → IoT Hub → Message routing within 60 seconds; Azure cost still $0

### Milestone 3 — Real-time dashboard on Azure (Week 3–4)

**Goal:** Live dashboard accessible at a public URL.

- [ ] Azure SignalR Standard tier active (upgrade from Free before testing)
- [ ] Blazor dashboard deployed to Azure Static Web App with public URL
- [ ] `Index.razor` shows live updating tiles from Azure SignalR
- [ ] All sensor labels use plain English ("Zone 1 is 72°F — normal")
- [ ] **Demo:** Share public URL with someone else; they open it on their phone; tiles update live

### Milestone 4 — Alerts and energy cost widget (Week 4–5)

- [ ] `AlertEvaluationService` evaluates thresholds and emits `AlertTriggered` with `plainEnglishMessage`
- [ ] `POST /api/alerts` creates threshold rules
- [ ] `AlertBadge.razor` shows plain-English alert notifications
- [ ] `EnergyCostWidget.razor` shows estimated cost delta vs prior period
- [ ] `Alerts.razor` lets user configure thresholds in plain English
- [ ] **Demo:** Set a threshold lower than current temperature; confirm alert fires and shows readable message

### Milestone 5 — CI/CD and portfolio polish (Week 5–6)

- [ ] Azure DevOps pipeline pushes C++ and API images to ghcr.io (free) and deploys to Container App
- [ ] Blazor dashboard deployed to Azure Static Web Apps (free) via GitHub Actions
- [ ] `README.md` includes architecture diagram, live demo URL, and one-paragraph project description
- [ ] Sequence diagram in `/docs/diagrams/` (C++ agent → IoT Hub → API → SignalR → Blazor)
- [ ] `docs/cost-model.md` documents per-building cost breakdown and upgrade triggers
- [ ] Record 2–3 minute screen capture demo for LinkedIn and GitHub README
- [ ] Confirm Azure bill is $0 after full pipeline is running
- [ ] **Demo:** Push a commit; watch pipeline run; dashboard updates live at public URL; cost = $0

### Milestone 6 — Add to resume (after M5 is complete)

- [ ] Add EdgeMonitor to Projects section of resume
- [ ] Update resume summary to reference Azure IoT Edge experience
- [ ] Submit application to Schneider Electric Senior role

### Milestone 1.5 — BACnet real building integration (post-portfolio, pre-first-customer)

- [ ] `BACnetSensorReader` implemented using `bacnet-stack` (github.com/bacnet-stack/bacnet-stack)
- [ ] BACnet/IP device discovery working on local network
- [ ] `ReadProperty` maps BACnet object model to `SensorReading`
- [ ] Tested against a real HVAC controller or BACnet simulator (VTS or YABE)
- [ ] `--bacnet` flag routes through `BACnetSensorReader` in `main.cpp`
- [ ] Budget 4–8 weeks — real BACnet implementations vary significantly across vendors

---

## Business Packaging Notes

### Go-to-market: channel sales, not direct SMB outreach

Building owners don't buy software categories they don't understand. HVAC contractors and property management firms already have customer relationships. The sales motion:

1. Find 2–3 local HVAC contractors willing to run a free 3-month pilot at one client building
2. Operate the pilots; fix issues; collect testimonials
3. Offer contractors a white-label reseller arrangement: they charge $99/month, you charge them $49/month
4. First paying customer target: months 9–12 (not months 3–6 — relationship sales takes time)

### Dashboard language is a product decision

Every label visible to a building owner should pass this test: would a restaurant manager understand it without explanation? "Zone 2 has been running 12% warmer than normal this week — estimated extra cost: $23" passes. "Temperature delta threshold exceeded, ZoneId: zone-2, variance: +4.2°F" fails.

### Multi-tenancy is in the schema from day one

`TenantId` on all entities. Every query filters by `TenantId`. Every API endpoint validates the caller's tenant. Adding this after the first customer means a migration on live data — do it now.

### Pricing model

|Tier|Price|Included|
|---|---|---|
|Free trial|$0 / 90 days|1 building, 5 sensors|
|Starter|$49/month per building|Unlimited sensors, 1-year history, email/SMS alerts, monthly PDF report|
|Pro|$99/month per building|Multi-building dashboard, API access, white-label branding|
|Reseller|Volume pricing|10+ buildings, co-branded portal for HVAC contractors|

### Per-building Azure cost model (after upgrades at first customer)

At 10+ buildings sharing one IoT Hub S1, PostgreSQL Flexible Server, and Container App, per-building Azure infrastructure cost drops to approximately $8–12/month — leaving $37–87 margin per building on the Starter plan. During the portfolio phase, infrastructure cost is $0.

### When to upgrade each service

|Event|Action|
|---|---|
|Live demo with multiple viewers|SignalR Free_F1 → Standard_S1 (~$40/month — downgrade after)|
|First paying customer signed|IoT Hub F1 → S1 (~$25/month)|
|First paying customer signed|Neon.tech → Azure PostgreSQL B1ms (~$15/month)|
|First paying customer signed|ghcr.io → Azure Container Registry Basic (~$5/month)|
|10+ paying buildings|Container App min-replicas 0 → 1 (~$13/month, eliminates cold starts)|

### What to build in v2 (post first 10 customers)

- Multi-tenant auth (Azure AD B2C or Auth0)
- gRPC direct channel from C++ agent to C# API (bypasses IoT Hub for latency-sensitive use cases — great resume talking point and performance improvement)
- Azure IoT Central integration (no-code device management UI — reduces support burden)
- Exportable PDF monthly energy reports
- Anomaly detection (Azure ML or simple moving-average baseline) — predictive maintenance angle