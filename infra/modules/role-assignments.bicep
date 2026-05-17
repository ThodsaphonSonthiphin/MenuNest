// =============================================================
// Role assignments — grant UAMI access to Key Vault
// =============================================================
// SQL Database access: ต้องทำเป็น T-SQL ภายหลัง (ดู README)
//   CREATE USER [menunest-id-a4ef] FROM EXTERNAL PROVIDER
//   ALTER ROLE db_owner ADD MEMBER [menunest-id-a4ef]
// (Bicep ไม่ native รัน T-SQL — ใช้ deployment script หรือ manual)
// =============================================================

param keyVaultName string

@description('Principal ID (object ID) ของ UAMI')
param uamiPrincipalId string

// Built-in role definition IDs (Azure-wide constants)
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

// UAMI → Key Vault: Secrets User (read secrets only, no list other keys)
resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, uamiPrincipalId, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      keyVaultSecretsUserRoleId
    )
    principalId: uamiPrincipalId
    principalType: 'ServicePrincipal'
  }
}
