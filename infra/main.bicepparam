using './main.bicep'

// =============================================================
// Parameters file — ค่าจริงสำหรับการ deploy
// =============================================================

param location = 'southeastasia'
param baseName = 'menunest'

// App Service Plan tier — F1 (Free) for family-app cost
// Trade-off: no Always On, 60 min CPU/day, cold start. Switch to B1 if needed.
param appServicePlanSku = 'F1'

// SQL Database SKU
param sqlSku = 'Basic'

// ----- Key Vault toggle -----
// false = ใส่ secrets ตรงใน App Settings (manual update ใน portal หลัง deploy)
// true  = deploy KV + ใช้ KV references → secret values คงอยู่แม้ redeploy
param useKeyVault = false

// ⚠️  ก่อน deploy ต้องใส่ค่าจริง:
//
//   หา Entra Object ID + UPN ของคุณ:
//     az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" -o json
//
//   หรือถ้าใช้ group เป็น SQL admin:
//     az ad group show --group "menunest-admins" --query "{id:id, displayName:displayName}"
//
// Real values supplied at deploy time via `--parameters`:
//   az deployment group create ... \
//     --parameters sqlAdminObjectId=$(az ad signed-in-user show --query id -o tsv) \
//     --parameters sqlAdminLogin=$(az ad signed-in-user show --query userPrincipalName -o tsv)
//
// Do NOT commit personal Entra Object ID / UPN into this file.
param sqlAdminObjectId = '<REPLACE-WITH-ENTRA-OBJECT-ID>'
param sqlAdminLogin = '<REPLACE-WITH-ENTRA-UPN>'

// ----- Existing resources (มีอยู่แล้ว) -----
// ค่า default ใน main.bicep ตรงกับของจริง ไม่ต้อง override
// ถ้าต้องการ override ปลด comment:
//
// param existingAppServicePlanName = 'ASP-MenuNest-a132'
// param existingAppServiceName = 'menunest'
// param existingUserAssignedIdentityName = 'menunest-id-a4ef'
// param existingAppInsightsName = 'menunest'

// =============================================================
// App Service settings — public config (safe to commit)
// =============================================================
// These were previously set manually in the portal; now declared in
// Bicep so a redeploy doesn't wipe them.

param azureAdClientId = 'e65fd81b-7a28-439b-a2ea-98734b5b5a36'
param azureAdAudience = 'e65fd81b-7a28-439b-a2ea-98734b5b5a36'
// azureAdInstance defaults to the current cloud's login endpoint
param googleClientId = '519165074825-op6v9vj01tijjudbc42jaufja5dj410h.apps.googleusercontent.com'
param pushVapidPublicKey = 'BBInGwYHqhqjlkjhxBm47ueF2IUAO2pXISY2pX7IRn7nHKPKUrWrTU1eEQoEf_W9TYP2Jf0D_FXLi4yCA11FMnw'
param pushVapidSubject = 'mailto:thodsaphonSP@hotmail.co.th'
param shareBaseUrl = 'https://green-rock-098e70e00.7.azurestaticapps.net'
param corsAllowedOrigins = 'https://green-rock-098e70e00.7.azurestaticapps.net'

// =============================================================
// App Service settings — SECRETS
// =============================================================
// ⚠️ Do NOT put real secret values here. Pass at deploy time:
//
//   az deployment group create ... \
//     --parameters pushVapidPrivateKey=<from .azure-migration-backup> \
//     --parameters shareTokenSigningKey=<from .azure-migration-backup>
//
// Placeholder values below let the bicepparam load locally; deploy
// overrides them with the real values via --parameters.
param pushVapidPrivateKey = 'REPLACE-AT-DEPLOY-TIME'
param shareTokenSigningKey = 'REPLACE-AT-DEPLOY-TIME'
