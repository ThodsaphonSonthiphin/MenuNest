# Azure Cost Monitoring & Prevention Guide

Practical step-by-step guide for monitoring Azure cost and preventing
overspend on the personal Pay-As-You-Go subscription that hosts the
menunest workloads.

> Scope: personal PAYG only. Never apply these changes to the work
> AzureSubscriptionInALSO subscription.

---

## Golden Rule: Always check Azure — never guess

When a cost number looks wrong (e.g. SQL says $4.90 SKU but the bill
shows $9), do not reason about causes from Bicep / config alone. The
real cause is almost always something the config doesn't show —
orphan resources in another resource group, a forgotten free-trial
DB that converted to paid, audit logs hitting Log Analytics, etc.

**Workflow:**

1. Query the Cost Management API grouped by `ResourceId` + `Meter`
   to see exactly which resource and which meter is charging.
2. Cross-check with `az resource list` to find resources that no
   longer exist but were billed earlier in the month.
3. Only then explain the cause.

**Worked example (May 2026):**

`MenuNest` SQL DB showed `Estimated cost / month = $9` while the
deployed SKU was Basic ($4.90 / mo).

```powershell
az rest --method post `
  --url "/subscriptions/<sub-id>/providers/Microsoft.CostManagement/query?api-version=2023-11-01" `
  --headers "ClientType=GitHubCopilotForAzure" `
  --body '{
    "type": "ActualCost",
    "timeframe": "MonthToDate",
    "dataset": {
      "granularity": "None",
      "aggregation": { "totalCost": { "name": "Cost", "function": "Sum" } },
      "filter": {
        "dimensions": { "name": "ServiceName", "operator": "In", "values": ["SQL Database"] }
      },
      "grouping": [
        { "type": "Dimension", "name": "ResourceId" },
        { "type": "Dimension", "name": "Meter" }
      ]
    }
  }'
```

Result:

| Resource | Meter | MTD Cost |
|---|---|---|
| `menunest-sql/MenuNest` (Basic) | B DTU | $1.15 |
| `thodsaphon-family-db/free-sql-db-3606679` (orphan) | vCore | $7.87 |
| `thodsaphon-family-db/free-sql-db-3606679` | Storage | $0.11 |

The $9 number came from an orphan SQL DB on a different server that
was already deleted, not from anything in the menunest Bicep. No
amount of staring at [sql.bicep](../infra/modules/sql.bicep) would
have surfaced it.

---

## Part 1: Monitor Azure Cost (Step-by-Step)

### Step 1 — Confirm permissions & access
Required roles on the subscription:

- Cost Management Reader
- Monitoring Reader
- Reader

Verify:

```powershell
az login
az account show
```

### Step 2 — Use Cost Analysis in Azure Portal (daily / weekly)
Portal path: **Cost Management + Billing → Cost analysis**

- View `Accumulated cost` to see month-to-date spend vs forecast
- Group by `Service name` to find which service costs the most
- Group by `Resource group` to find which workload costs the most
- Group by `Tag` for project-level cost (requires tagging)
- Use timeframe `This month` / `Last month` / `Custom`

### Step 3 — Create Budgets with Alerts (highest-leverage control)
Path: **Cost Management → Budgets → + Add**

- Scope: the subscription
- Amount: e.g. 500 THB / month
- Alert thresholds: 50% (actual), 80% (actual), 100% (forecast)
- Notification email: thodsaphon.sonthipin@cartagena.no
- Action group (optional): Logic App / Function that auto-stops VMs
  when the threshold is hit

Without budgets there is no early warning — this is the #1 control
on PAYG.

### Step 4 — Enable Cost Anomaly Detection
Path: **Cost Management → Cost alerts → Anomaly alerts**

- Detects unusual daily spikes (e.g. a runaway test VM)
- Daily notifications, no extra cost

### Step 5 — Programmatic monitoring (CLI)
Query month-to-date cost grouped by service:

```powershell
az rest --method post `
  --url "/subscriptions/<sub-id>/providers/Microsoft.CostManagement/query?api-version=2023-11-01" `
  --headers "ClientType=GitHubCopilotForAzure" `
  --body '{
    "type": "ActualCost",
    "timeframe": "MonthToDate",
    "dataset": {
      "granularity": "None",
      "aggregation": { "totalCost": { "name": "Cost", "function": "Sum" } },
      "grouping": [ { "type": "Dimension", "name": "ServiceName" } ]
    }
  }'
```

API guardrails:

- Daily granularity max range: 31 days
- Monthly / None granularity max range: 12 months
- Absolute max range: 37 months
- Max GroupBy dimensions: 2
- Max rows per page: 5,000
- Per-scope rate limit: 4 requests / minute

### Step 6 — Weekly 5-minute review
Every Monday:

1. Portal → Cost analysis → "This month" view
2. Compare forecast vs budget
3. Inspect top 3 services — does spend match actual usage?
4. Open **Azure Advisor → Cost** for new recommendations

---

## Part 2: Prevent High Costs

### A. Subscription-level guardrails (set once)

| Control | How | Why |
|---|---|---|
| Spending limit | PAYG subs converted from a free trial expose `Remove spending limit` — keep it ON unless premium services are required | Hard cap; subscription disabled when reached |
| Budgets with action groups | Step 3 above + Logic App that stops VMs | Auto-mitigation, not just alerts |
| Resource tagging policy | Azure Policy: require `project`, `env` tags | Enables cost-by-tag attribution |
| Region lock | Policy: allow only `southeastasia` | Prevents accidental deploys to expensive regions |
| SKU restrictions | Policy: deny `Standard_E*`, `Standard_M*` VM sizes | Blocks oversized resources |

### B. Resource-level habits

1. Always check the **free tier** first — App Service F1, Functions
   consumption (1M req free), Cosmos free tier (1000 RU/s), Static
   Web Apps Free, 5 GB blob storage. Many small apps fit entirely
   inside free tier.
2. Pick consumption / serverless when traffic is bursty — Functions,
   Container Apps (scale-to-zero), Logic Apps consumption.
3. **Auto-shutdown VMs** — DevTest Labs auto-shutdown, or
   VM → Operations → Auto-shutdown (e.g. 19:00 daily). A B2s left
   running 24/7 ≈ 1,100 THB / month wasted.
4. Use B-series (burstable) for dev / test, not D/E/F-series.
5. Right-size storage tiers — Hot → Cool → Cold → Archive based on
   access pattern.
6. Reserved Instances / Savings Plans — only when something will run
   for 1y+ at a known size. Skip on PAYG until usage is steady.
7. Delete on every spike — unattached disks, unassigned public IPs,
   Log Analytics (drop retention to 30 d), App Service plans without
   webapps.

### C. Find waste monthly with Azure Quick Review (azqr)

```powershell
azqr scan --subscription-id <sub-id>
```

azqr flags orphaned disks, idle NAT gateways, oversized SKUs, and
missing tags — the most common cost leaks.

### D. Use Azure Advisor every week
Portal: **Advisor → Cost** tab — surfaces:

- Idle / underutilized VMs to shut down
- Reservation purchase recommendations
- Right-sizing suggestions with savings estimates

### E. menunest-specific defaults
For the migraine tracker module and any new menunest service:

- App Service **B1** (~450 THB / mo) or **F1 (free)** for APIs, not P1v3
- Static Web Apps **Free tier** for the SPA
- Cosmos **Serverless** for low-traffic workloads, or
  **Free tier (1000 RU/s)**
- Set a **500 THB / month budget** with 50 / 80 / 100% alerts before
  deploy
- Tag every resource with `project=migraine-tracker`

---

## Quick checklist

- [ ] Budget created with 50 / 80 / 100% alerts
- [ ] Anomaly alerts enabled
- [ ] Spending limit kept ON
- [ ] All resources tagged (`project`, `env`)
- [ ] VMs have auto-shutdown configured
- [ ] Weekly: Cost Analysis review + Advisor check
- [ ] Monthly: `azqr scan` to find orphans

---

## References

- [Cost Management API — Query](https://learn.microsoft.com/rest/api/cost-management/query)
- [Cost Management API — Forecast](https://learn.microsoft.com/rest/api/cost-management/forecast)
- [Azure Advisor — Cost recommendations](https://learn.microsoft.com/azure/advisor/advisor-reference-cost-recommendations)
- [Azure Quick Review (azqr)](https://github.com/Azure/azqr)
