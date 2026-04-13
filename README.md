# 🍽️ MenuNest

Family meal-planning web app — build a recipe library, track pantry stock, plan daily meals, and see what you need to buy when stock runs low.

**Domain:** menunest.app

---

## Features (MVP)

- 🔐 Sign in with Microsoft Entra ID (work, school, or personal account)
- 👪 Family management — create a family, invite members with a code, set relationships between members
- 🧂 Ingredient master — per-family list with autocomplete and on-the-fly creation
- 📖 Recipe library — store recipes with photos and ingredient quantities
- 📦 Stock — manually track what you have on hand
- 📅 Meal plan — plan meals by day × slot (breakfast / lunch / dinner)
- ✅ Stock check — compare a recipe against current stock and report what's missing
- 🍳 Cook action — one click on a planned meal deducts ingredients from stock automatically (clamped at zero; partial deductions allowed with a warning)
- 🛒 Shopping list — persistent lists you can build manually or auto-generate from meal plans; checking an item as bought adds it to stock automatically

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

Full implementation plan: [docs/plan.md](docs/plan.md)

---

## Local Development

### Prerequisites
- .NET 10 SDK
- Node.js 20+ and npm
- Azure SQL, SQL Server LocalDB, or a Docker SQL container
- Azurite (Blob Storage emulator) or an Azure Storage account
- An Azure App Registration (for Entra ID)

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

## Contributing

This is a family/personal project — external pull requests are not accepted.

---

## License

Private / unpublished (TBD)
