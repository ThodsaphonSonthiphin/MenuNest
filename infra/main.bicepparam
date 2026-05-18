using './main.bicep'

// =============================================================
// Parameters file — ค่าจริงสำหรับการ deploy
// =============================================================

param location = 'southeastasia'
param baseName = 'menunest'

// App Service Plan upgrade target
param appServicePlanSku = 'B1'

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
