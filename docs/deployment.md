# Deployment (Azure)

> Part of [MenuNest](../README.md). This is the operational setup guide; the README is the project overview.

The app is split across two Azure services:

- **Frontend → Azure Static Web Apps.** Hosts the built `frontend/dist`.
  SPA routing and security headers live in
  [frontend/public/staticwebapp.config.json](../frontend/public/staticwebapp.config.json).
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

