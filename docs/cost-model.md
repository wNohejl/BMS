# EdgeMonitor — Cost Model

Free-first at every layer. You only pay when there's a reason to.

## Phase 1: Local development — $0/month

| Service | Approach | Cost |
|---|---|---|
| IoT Hub | Not used — `--local` writes JSON to disk | $0 |
| Container App | `dotnet run` locally | $0 |
| SignalR | In-process hub | $0 |
| PostgreSQL | Docker container | $0 |
| Static Web App | `dotnet watch` locally | $0 |
| Container Registry | Not needed | $0 |
| **Total** | | **$0** |

## Phase 2: Cloud portfolio / public demo — $0/month

| Service | Free tier | Limit to know |
|---|---|---|
| IoT Hub F1 | 1 hub per subscription | 8,000 msgs/day at 0.5KB chunks |
| Container Apps | 180K vCPU-sec + 2M requests/month | Scales to zero — ~5–15s cold start |
| SignalR Free_F1 | 20 concurrent connections | Solo demo only, not multi-viewer |
| PostgreSQL | Neon.tech free (Azure has no free tier) | 0.5 GB storage |
| Container Registry | ghcr.io (Azure ACR has no free tier) | Free for public repos |
| Static Web App | Free tier | $0 always |
| **Total** | | **$0** |

**F1 throughput strategy:** at a 30s read interval batching 5 readings into one ~0.3KB payload, the agent generates ~2,880 messages/day — well within the 8,000 limit. Don't read more often than every 20–30s on F1. If a send hangs, that's `403002 IoTHubQuotaExceeded` — the `TelemetryPublisher` quota guard queues locally until midnight UTC.

## Upgrade triggers

| Trigger | Upgrade | Cost added |
|---|---|---|
| Live demo, multiple viewers | SignalR Free_F1 → Standard_S1 | +$40/month (downgrade after) |
| First paying customer | IoT Hub F1 → S1 | +$25/month |
| First paying customer | Neon.tech → Azure PostgreSQL B1ms | +$15/month |
| First paying customer | ghcr.io → Azure Container Registry Basic | +$5/month |
| 10+ paying buildings | Container App min-replicas 0 → 1 | +$10–13/month |

## Per-building production cost (at scale, after upgrades)

| Scenario | Azure cost | Revenue at $49/mo | Margin |
|---|---|---|---|
| 1 building | ~$45/month | $49 | ~9% |
| 10 buildings | ~$80/month (shared hub) | $490 | ~84% |
| 50 buildings | ~$200/month | $2,450 | ~92% |

At 10+ buildings sharing one IoT Hub S1, one PostgreSQL server, and one Container App, per-building infrastructure cost drops to ~$8–12/month — leaving $37–87 margin per building on the $49 Starter plan.

## Cost alert — set even on the free tier

The Bicep template (`infra/main.bicep`) includes a $10/month budget that emails at 80%.
Any charge on what should be a $0 stack is worth knowing about immediately.

```bash
az consumption budget create \
  --budget-name edgemonitor-budget \
  --amount 10 \
  --time-grain Monthly \
  --resource-group rg-edgemonitor \
  --notifications "[{\"enabled\":true,\"operator\":\"GreaterThan\",\"threshold\":80,\"contactEmails\":[\"your@email.com\"]}]"
```

## Teardown between work sessions

```bash
az group delete --name rg-edgemonitor --yes --no-wait
# Reprovision in one command when you resume:
az deployment group create -g rg-edgemonitor -f infra/main.bicep -p @infra/parameters.json
```
