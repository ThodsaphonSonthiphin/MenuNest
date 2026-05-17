// =============================================================
// menunest — Main IaC orchestrator
// =============================================================
// Idempotent deployment ที่ทำงานกับ resource ที่มีอยู่แล้ว:
//   1. Upgrade App Service Plan F1 → B1 (เพื่อ Always On)
//   2. Configure App Service "menunest" — attach UAMI + Always On
//                                         + App Settings (placeholders/KV refs)
//   3. สร้าง Azure SQL Server + Basic DB (Entra-only auth)
//   4. [Optional] สร้าง Key Vault (toggle ผ่าน useKeyVault parameter)
// =============================================================

targetScope = 'resourceGroup'

// ----- Required parameters -----
@description('Azure region — ต้อง match กับ resource เดิม (southeastasia)')
param location string = 'southeastasia'

@description('Base name prefix — ใช้เป็น prefix ของ new resources')
param baseName string = 'menunest'

@description('App Service Plan tier — B1 = ต่ำสุดที่รองรับ Always On')
@allowed([ 'B1', 'B2', 'S1', 'P0v3', 'P1v3' ])
param appServicePlanSku string = 'B1'

@description('SQL Database SKU')
@allowed([ 'Basic', 'S0', 'S1', 'S2' ])
param sqlSku string = 'Basic'

@description('Entra Object ID ของคน/group ที่เป็น SQL Admin (Entra-only auth)')
param sqlAdminObjectId string

@description('Entra UPN ของ SQL Admin (display only — sid ใช้ object ID ด้านบน)')
param sqlAdminLogin string

@description('สลับเป็น true เพื่อ deploy Key Vault + ใช้ KV references สำหรับ secrets')
param useKeyVault bool = false

// ----- Existing resource names (มีอยู่แล้วใน Azure) -----
@description('ชื่อ App Service Plan เดิม (F1) ที่จะ upgrade')
param existingAppServicePlanName string = 'ASP-MenuNest-a132'

@description('ชื่อ App Service เดิม')
param existingAppServiceName string = 'menunest'

@description('ชื่อ User-Assigned Managed Identity เดิม')
param existingUserAssignedIdentityName string = 'menunest-id-a4ef'

@description('ชื่อ Application Insights เดิม')
param existingAppInsightsName string = 'menunest'

// =============================================================
// REFERENCE EXISTING RESOURCES (read-only)
// =============================================================

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: existingUserAssignedIdentityName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: existingAppInsightsName
}

// =============================================================
// MODULES
// =============================================================

module sql 'modules/sql.bicep' = {
  name: 'deploy-sql'
  params: {
    location: location
    sqlServerName: '${baseName}-sql'
    sqlDatabaseName: 'MenuNest'
    sqlSku: sqlSku
    sqlAdminObjectId: sqlAdminObjectId
    sqlAdminLogin: sqlAdminLogin
  }
}

// Always deploy Storage — สำหรับ photos (drug packaging, episode photos)
module storage 'modules/storage.bicep' = {
  name: 'deploy-storage'
  params: {
    location: location
    // Storage Account name = globally unique, lowercase + digits, max 24 chars
    storageAccountName: '${baseName}st${uniqueString(resourceGroup().id)}'
    uamiPrincipalId: uami.properties.principalId
  }
}

// Conditional: เฉพาะถ้า useKeyVault = true
module keyVault 'modules/keyvault.bicep' = if (useKeyVault) {
  name: 'deploy-keyvault'
  params: {
    location: location
    keyVaultName: '${baseName}-kv'
  }
}

module appService 'modules/app-service.bicep' = {
  name: 'deploy-appservice'
  params: {
    location: location
    appServicePlanName: existingAppServicePlanName
    appServicePlanSku: appServicePlanSku
    appServiceName: existingAppServiceName
    uamiResourceId: uami.id
    uamiClientId: uami.properties.clientId
    appInsightsConnectionString: appInsights.properties.ConnectionString
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    blobEndpoint: storage.outputs.blobEndpoint
    drugImagesContainer: storage.outputs.drugImagesContainerName
    episodeImagesContainer: storage.outputs.episodeImagesContainerName
    useKeyVault: useKeyVault
    keyVaultUri: useKeyVault ? keyVault!.outputs.vaultUri : ''
  }
}

// Grant UAMI access to KV — เฉพาะถ้าใช้ KV
module roles 'modules/role-assignments.bicep' = if (useKeyVault) {
  name: 'deploy-roles'
  params: {
    keyVaultName: keyVault!.outputs.vaultName
    uamiPrincipalId: uami.properties.principalId
  }
}

// =============================================================
// OUTPUTS
// =============================================================
output appServiceHostName string = appService.outputs.defaultHostName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output keyVaultUri string = useKeyVault ? keyVault!.outputs.vaultUri : '(not deployed)'
output blobEndpoint string = storage.outputs.blobEndpoint
output storageAccountName string = storage.outputs.storageAccountName
output uamiPrincipalId string = uami.properties.principalId
output uamiClientId string = uami.properties.clientId
