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

## Local Development

### Prerequisites

**Runtime / tooling**
- .NET 10 SDK
- Node.js 20.19+ (or 22 LTS) and npm — required by Vite 8 / React 19
- Azure SQL, SQL Server LocalDB, or a Docker SQL container — schema is created by EF Core migrations

**Cloud / external accounts** (you can stub out anything you don't plan to test)

| What | Why | Required for |
|---|---|---|
| **Azure Entra ID app registration** | Microsoft sign-in (multi-tenant + personal accounts) | Sign-in via Microsoft |
| **Google OAuth Client ID** (Google Cloud Console → APIs & Services → Credentials) | Google sign-in via GIS | Sign-in via Google (alternative to Entra) |
| **Azurite** or an **Azure Storage account** | Drug / episode / recipe photo uploads (direct browser → Blob via user-delegation SAS) | Photo upload in Health + Recipes |
| **Gemini API key** (Google AI Studio) | The `AiAssistant` chat agent (function-calling) | `/ai-assistant` page |
| **VAPID key pair** (`web-push generate-vapid-keys`) | Encrypted web push for follow-up pings | 0-tap follow-up notifications in Health |
| **Syncfusion Community License key** | Syncfusion components (Grid, Schedule, QR generator) | Suppresses the trial banner |
| **Azure Speech key** *(optional)* | Voice input in the AI assistant | Speech-to-text in `/ai-assistant` |

> Without VAPID, the follow-up dispatcher still runs but logs a warning and returns 0 — pings are still marked `Asked` and surface in the in-app modal. Without Gemini, the `/ai-assistant` page returns a friendly error. Without the Syncfusion key everything still works but you get a trial banner. So the minimum for "useful local dev" is: .NET + Node + SQL + Azurite + **one** of (Entra OR Google).

### Setup
```bash
# Backend
cd backend
dotnet restore
dotnet ef database update --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
dotnet run --project src/MenuNest.WebApi
# → https://localhost:5001/swagger

# Frontend (in a separate terminal)
cd frontend
npm install
npm run dev
# → http://localhost:5173
```

Copy `appsettings.Development.json.example` and `.env.example`, then fill in your own credentials.

---

## Deployment (Azure)

The app is split across two Azure services:

- **Frontend → Azure Static Web Apps.** Hosts the built `frontend/dist`.
  SPA routing and security headers live in
  [frontend/staticwebapp.config.json](frontend/staticwebapp.config.json).
  SWA's built-in `/.auth/*` endpoints are **not** used — auth is handled
  client-side by MSAL against Entra ID (needed for personal accounts).
- **Backend → Azure App Service (Linux, .NET 10).** Hosts the Web API,
  connects to Azure SQL and Blob Storage.

### Backend configuration (App Service → Application settings)

| Setting | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | Azure SQL connection string (use Managed Identity where possible) |
| `AzureAd__ClientId` | Entra ID app client ID |
| `AzureAd__Audience` | Entra ID app client ID (**GUID only**, not `api://{guid}`) — MSAL.js SPAs receive v2.0 tokens whose `aud` claim is the bare client ID |
| `AzureBlob__ConnectionString` | Storage account connection string (or use Managed Identity) |
| `Cors__AllowedOrigins` | Comma-separated list including the SWA origin, e.g. `https://menunest.azurestaticapps.net,https://menunest.app` |
| `AzureAd__ClientSecret` | Entra app client secret — the MCP OAuth proxy uses it to exchange auth codes with Entra server-side |
| `Jwt__SigningKey` | Strong random secret; HMAC-SHA256 key for the proxy's minted MCP access tokens |
| `MCP__ServerUrl` | Full MCP endpoint URL, e.g. `https://menunest.azurewebsites.net/mcp` (used as `aud`/`iss` of proxy JWTs) |

### Frontend configuration (SWA → Application settings / `.env.production`)

| Setting | Value |
|---|---|
| `VITE_MSAL_CLIENT_ID` | Entra ID app client ID |
| `VITE_MSAL_AUTHORITY` | `https://login.microsoftonline.com/common` |
| `VITE_API_SCOPE` | `api://<api-app-id>/access_as_user` |
| `VITE_API_BASE_URL` | `https://menunest.azurewebsites.net` |
| `VITE_SYNCFUSION_LICENSE_KEY` | Your Syncfusion Community License key |

### Entra ID App Registration (one-time setup)

- Platform: **Single-page application** with redirect URIs for both
  `http://localhost:5173` (dev) and the production SWA URL.
- Add a **Web** redirect URI `https://<your-host>/oauth/callback` for the MCP OAuth proxy.
- Expose an API scope `access_as_user`.
- Supported account types: **multi-tenant + personal Microsoft accounts**.

---

## Contributing

This is a family/personal project — external pull requests are not accepted.

---

## License

Private / unpublished (TBD)
