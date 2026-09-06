# Local Development

> Part of [MenuNest](../README.md). This is the operational setup guide; the README is the project overview.

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
# → https://localhost:5001/scalar

# Frontend (in a separate terminal)
cd frontend
npm install
npm run dev
# → http://localhost:5173
```

Copy `frontend/.env.example` to `frontend/.env` and fill in your own credentials.

The backend has no `appsettings.Development.json`-style template to copy — no `.example` counterpart
exists in this repo, unlike the frontend's `.env.example`. `MenuNest.WebApi.csproj` sets a
`UserSecretsId`, and `WebApplication.CreateBuilder` loads the user-secrets store automatically in
Development — so the intended path is `dotnet user-secrets set <Key> <Value> --project src/MenuNest.WebApi`
for each credential listed in the prerequisites table above (`AzureAd:ClientId`, `AzureAd:ClientSecret`,
`Google:ClientId`, `Gemini:ApiKey`, `AzureSpeech:SubscriptionKey`, `Push:VapidPublicKey`,
`Push:VapidPrivateKey`, `Jwt:SigningKey`, `Share:TokenSigningKey`, and `ConnectionStrings:DefaultConnection`
if you're not using the LocalDB default in `appsettings.json`). An `appsettings.Development.json` you
create by hand works too — user secrets just avoid the risk of committing it.

---

