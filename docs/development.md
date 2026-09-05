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
# → https://localhost:5001/swagger

# Frontend (in a separate terminal)
cd frontend
npm install
npm run dev
# → http://localhost:5173
```

Copy `appsettings.Development.json.example` and `.env.example`, then fill in your own credentials.

---

