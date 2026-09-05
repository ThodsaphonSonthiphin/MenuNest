# 🍽️ MenuNest

Two-in-one personal web app: a **migraine / symptom tracker** that produces shareable doctor reports, and a **family meal planner** that turns recipes + pantry stock into meal plans and shopping lists.

**Domain:** menunest.app
**Default landing:** `/health` (the migraine tracker — meal-planning lives at `/dashboard` and the top nav)

---

## Features

### 🤒 Health — migraine & symptom tracker (personal, single-user)

- 🔐 Sign in with **Microsoft Entra ID** (work / school / personal) or **Google** — works without joining a family
- 📝 **Quick-log attack** — pick a symptom + severity 1–10; optional migraine attributes (aura, location, quality, associated symptoms, functional impact, triggers, on-period flag)
- 💊 **Take medication** — drugs are bucketed into *active in effect* / *takeable* / *blocked* (daily-dose cap + still-active window enforced server-side); "ไม่กินยา" fallback records the reason
- ⏰ **+30 min follow-up push** — VAPID web-push from a 1-min `BackgroundService`; lock-screen **0-tap response** for *Resolved* / *Same* (the SW POSTs without opening the app)
- 📸 **Drug photos** — multi-photo per drug, uploaded direct browser → Blob via short-lived user-delegation SAS
- 📊 **History & active episode** — timeline of all episodes + a dedicated screen for an in-progress attack
- 👨‍⚕️ **Doctor report share link** — date-bounded, HMAC-signed token, rendered as a QR code; doctor scans → opens an **anonymous** report page with summary, MOH/chronic clinical flags, trigger correlations, per-drug treatment efficacy (relief rate, avg onset), and a per-day timeline. Only a SHA-256 hash is stored — a DB leak does not expose live tokens.
- 📱 **PWA** — installable, service worker handles push + notification actions

### 🍳 Meal planning (family-scoped, multi-user)

- 👪 **Family management** — create a family, invite members with a code, set relationships between members
- 🧂 **Ingredient master** — per-family list with autocomplete and on-the-fly creation
- 📖 **Recipe library** — store recipes with photos (Blob SAS) and ingredient quantities
- 📦 **Stock** — manually track what you have on hand; every change is audit-logged
- 📅 **Meal plan** — plan meals by day × slot (breakfast / lunch / dinner)
- ✅ **Stock check** — compare planned meals against current stock and report what's missing
- 🍳 **Cook action** — one click deducts ingredients automatically (clamped at zero, partial deductions allowed with a warning)
- 🛒 **Shopping list** — persistent lists you can build manually or auto-generate from a meal plan range; ticking an item as bought auto-restocks the pantry
- 💸 **Budget** — track spend per shopping list
- 🤖 **AI assistant (Gemini)** — function-calling agent that can search recipes, check stock, get the meal plan, and (with explicit Thai/English confirmation) create recipes, add to the meal plan, or create shopping lists

> 📐 For end-to-end sequence diagrams of every flow above, see **[docs/architecture.md](docs/architecture.md)**.

---

## Tech Stack

### Frontend — `frontend/`
- React 18 + TypeScript + Vite
- Redux Toolkit (RTK + RTK Query) — state and API client
- React Router v6
- MSAL.js (`@azure/msal-react`) — Entra ID authentication
- Syncfusion (Community License) — Grid, Schedule, inputs
- Pattern: page-scoped folders (`pages/{feature}/{components,hooks,api,slice}`) with a component-plus-hook style

### Backend — `backend/`
- ASP.NET 10 (LTS) Clean Architecture
- EF Core 10 with the SQL Server provider (Azure SQL)
- `Mediator` (martinothamar) — CQRS with pipeline behaviors
- `FluentValidation` — request validation
- `Mapster` — DTO mapping
- `Microsoft.Identity.Web` — JWT bearer auth (multi-tenant + personal accounts)
- `Azure.Storage.Blobs` — recipe image storage
- Serilog + Application Insights

### Infra — `infra/`
- Azure App Service (backend)
- Azure Static Web Apps (frontend)
- Azure SQL Database
- Azure Storage Account (blob container: `recipe-images`)
- Application Insights
- Azure App Registration (Entra ID — multi-tenant + personal)

---

## Folder Structure

```
menunest/
├── backend/          # ASP.NET 10 Clean Architecture solution
├── frontend/         # Vite + React + TypeScript app
├── docs/             # Architecture, design spec, API docs
└── infra/            # Bicep / ARM templates (optional)
```

- **Architecture & flows** (sequence diagrams for every major feature): [docs/architecture.md](docs/architecture.md)
- **Implementation plan** (scope, data model): [docs/plan.md](docs/plan.md)

---

## Running it

```bash
# Backend  → https://localhost:5001/swagger
cd backend && dotnet run --project src/MenuNest.WebApi

# Frontend → http://localhost:5173
cd frontend && npm install && npm run dev
```

Full prerequisites, external accounts and first-run setup: **[docs/development.md](docs/development.md)**
Azure topology and every configuration setting: **[docs/deployment.md](docs/deployment.md)**

## Contributing

This is a family/personal project — external pull requests are not accepted.

---

## License

Private / unpublished (TBD)
