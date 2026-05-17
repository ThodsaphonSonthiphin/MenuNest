// =============================================================
// Azure Key Vault — Standard tier, RBAC mode
// =============================================================
// - RBAC authorization (recommend over legacy access policies)
// - Purge protection ON (best practice — DO NOT disable)
// - Soft delete 90 days
// - Public access enabled (lock down ภายหลังด้วย private endpoint)
//
// Secrets ที่จะใช้ (set ภายหลังด้วย az CLI หรือ portal):
//   - SqlConnectionString-DefaultConnection
//   - VapidPrivateKey
//   - VapidPublicKey (อาจไม่ต้อง — public key ใส่ใน app config ก็ได้)
//   - QrTokenSigningKey (HMAC sec ของ doctor share token)
//   - GoogleClientSecret (ถ้ามี)
// =============================================================

param location string

@description('Key Vault name — must be globally unique, 3-24 chars')
param keyVaultName string

resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'  // $0.03/10k ops — pay-as-you-go ไม่มี fixed monthly
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true        // ✓ RBAC mode (not access policies)
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true           // ✓ Required by best practice
    publicNetworkAccess: 'Enabled'        // Switch to 'Disabled' + PE later
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

output vaultName string = keyVault.name
output vaultUri string = keyVault.properties.vaultUri
output vaultId string = keyVault.id
