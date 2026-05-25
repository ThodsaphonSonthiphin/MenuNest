// =============================================================
// App Service Plan upgrade + App Service configuration
// =============================================================
// - Upgrade plan F1 (Free) → B1 (Basic, Always On supported)
// - Attach existing User-Assigned Managed Identity (สำหรับ SQL Entra auth)
// - Always On = true (สำหรับ BackgroundService)
// - HTTPS only, TLS 1.2+, FTPS disabled
// - App Settings: App Insights, AZURE_CLIENT_ID (UAMI), secret placeholders
// - Connection String: SQL ผ่าน Entra UAMI auth (no password)
//
// ⚠️ App Settings ที่เป็น "REPLACE-..." = ต้อง set จริงหลัง deploy
//    ด้วย Azure portal หรือ `az webapp config appsettings set`
//    ถ้า redeploy Bicep จะ reset กลับเป็น placeholder!
// =============================================================

param location string

// ----- App Service Plan -----
param appServicePlanName string

@allowed([ 'F1', 'B1', 'B2', 'S1', 'P0v3', 'P1v3' ])
param appServicePlanSku string

// ----- App Service -----
param appServiceName string

// ----- Identity -----
@description('Resource ID ของ User-Assigned Managed Identity')
param uamiResourceId string

@description('Client ID ของ UAMI — ใช้ใน env AZURE_CLIENT_ID')
param uamiClientId string

// ----- Connections -----
param appInsightsConnectionString string
param sqlServerFqdn string
param sqlDatabaseName string

// ----- Storage (always required) -----
@description('Blob endpoint, e.g., https://menuneststxyz.blob.core.windows.net/')
param blobEndpoint string

@description('Container name for drug images')
param drugImagesContainer string = 'drug-images'

@description('Container name for episode images')
param episodeImagesContainer string = 'episode-images'

// ----- Optional: Key Vault integration -----
@description('สลับเป็น true เพื่อใช้ KV references สำหรับ secrets')
param useKeyVault bool = false

@description('Key Vault URI (จำเป็นถ้า useKeyVault = true)')
param keyVaultUri string = ''

// ----- Auth (MSAL + Google) -----
@description('Entra ID app registration Client ID (public)')
param azureAdClientId string

@description('Entra ID Audience (typically same as ClientId)')
param azureAdAudience string

@description('Entra ID instance URL — default to the current cloud\'s login endpoint')
param azureAdInstance string = environment().authentication.loginEndpoint

@description('Google OAuth Client ID (public)')
param googleClientId string

// ----- Web push (real values; the legacy VapidPrivateKey/PublicKey above are unused placeholders) -----
@description('VAPID public key for web push (public)')
param pushVapidPublicKey string

@description('VAPID subject — mailto: or https: identifier (public)')
param pushVapidSubject string

@secure()
@description('VAPID private key for web push (SECRET — pass via --parameters at deploy time)')
param pushVapidPrivateKey string

// ----- Share-link signing -----
@secure()
@description('Doctor-share token signing key (SECRET — pass via --parameters)')
param shareTokenSigningKey string

@description('Base URL of the SPA — used to build share links')
param shareBaseUrl string

// ----- CORS -----
@description('Comma-separated allowed SPA origins for CORS')
param corsAllowedOrigins string

// =============================================================
// Secret values — ใช้ KV reference หรือ placeholder
// =============================================================
// ถ้า useKeyVault = false → placeholder ที่ user ต้องไป set ใน portal
// ถ้า useKeyVault = true  → KV reference (Bicep redeploy ไม่ทับ secret จริง)
var vapidPrivateKey = useKeyVault
  ? '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/VapidPrivateKey/)'
  : 'REPLACE-AFTER-DEPLOY-IN-PORTAL'

var vapidPublicKey = useKeyVault
  ? '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/VapidPublicKey/)'
  : 'REPLACE-AFTER-DEPLOY-IN-PORTAL'

var qrTokenSigningKey = useKeyVault
  ? '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/QrTokenSigningKey/)'
  : 'REPLACE-AFTER-DEPLOY-IN-PORTAL'

var googleClientSecret = useKeyVault
  ? '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/GoogleClientSecret/)'
  : 'REPLACE-AFTER-DEPLOY-IN-PORTAL'

// =============================================================
// Upgrade App Service Plan (F1 → B1)
// =============================================================
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'  // Linux — ราคา B1 ใน SEA ถูกกว่า Windows ~$40/mo
  properties: {
    reserved: true  // true = Linux (จำเป็น)
  }
}

// =============================================================
// Configure App Service (existing resource — update properties)
// =============================================================
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uamiResourceId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    // keyVaultReferenceIdentity ใช้เมื่อมี KV เท่านั้น
    keyVaultReferenceIdentity: useKeyVault ? uamiResourceId : null
    siteConfig: {
      // alwaysOn ใช้ได้เฉพาะ B1+ (F1 Free ไม่รองรับ — deploy จะ fail)
      alwaysOn: appServicePlanSku != 'F1'
      linuxFxVersion: 'DOTNETCORE|10.0'         // .NET 10 (Linux)
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      // F1 (Free) requires 32-bit; Linux runtime ignores the flag (process is always 64-bit in container).
      // For B1+ flip back to false if you want enforcement.
      use32BitWorkerProcess: appServicePlanSku == 'F1'
      webSocketsEnabled: false
      // ⚠️ appSettings REPLACE ของเดิมทั้งหมด — review what-if ก่อน deploy
      appSettings: [
        // ----- Managed by Bicep (safe to redeploy) -----
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: uamiClientId  // ใช้กับ DefaultAzureCredential เพื่อ pick UAMI
        }
        {
          name: 'AzureAd__TenantId'
          value: subscription().tenantId
        }
        {
          name: 'Storage__BlobEndpoint'
          value: blobEndpoint
        }
        {
          name: 'Storage__DrugImagesContainer'
          value: drugImagesContainer
        }
        {
          name: 'Storage__EpisodeImagesContainer'
          value: episodeImagesContainer
        }
        // ----- Auth: Microsoft Entra ID (MSAL) -----
        {
          name: 'AzureAd__ClientId'
          value: azureAdClientId
        }
        {
          name: 'AzureAd__Audience'
          value: azureAdAudience
        }
        {
          name: 'AzureAd__Instance'
          value: azureAdInstance
        }
        // ----- Auth: Google OAuth (client ID is public) -----
        {
          name: 'Google__ClientId'
          value: googleClientId
        }
        // ----- CORS -----
        {
          name: 'Cors__AllowedOrigins'
          value: corsAllowedOrigins
        }
        // ----- Web push (Push__* are the real keys; legacy Vapid* below are placeholders) -----
        {
          name: 'Push__VapidPublicKey'
          value: pushVapidPublicKey
        }
        {
          name: 'Push__VapidPrivateKey'
          value: pushVapidPrivateKey
        }
        {
          name: 'Push__VapidSubject'
          value: pushVapidSubject
        }
        // ----- Doctor-share links -----
        {
          name: 'Share__TokenSigningKey'
          value: shareTokenSigningKey
        }
        {
          name: 'Share__BaseUrl'
          value: shareBaseUrl
        }
        // ----- Legacy placeholders (kept for backward compat; values currently unused by code) -----
        {
          name: 'VapidPrivateKey'
          value: vapidPrivateKey
        }
        {
          name: 'VapidPublicKey'
          value: vapidPublicKey
        }
        {
          name: 'QrTokenSigningKey'
          value: qrTokenSigningKey
        }
        {
          name: 'Google__ClientSecret'
          value: googleClientSecret
        }
      ]
      connectionStrings: [
        {
          name: 'DefaultConnection'
          type: 'SQLAzure'
          // ใช้ Entra UAMI auth — Active Directory Default จะใช้ AZURE_CLIENT_ID env
          connectionString: 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default;'
        }
      ]
    }
  }
}

output defaultHostName string = site.properties.defaultHostName
output appServiceId string = site.id
