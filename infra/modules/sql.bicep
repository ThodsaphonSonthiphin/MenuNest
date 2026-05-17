// =============================================================
// Azure SQL Server + Database
// =============================================================
// - Entra-only auth (ห้ามมี SQL password)
// - Basic SKU 5 DTU / 2 GB ($4.83/mo)
// - TLS 1.2 minimum
// - Local-redundant backup (ถูกสุด)
// - Firewall: อนุญาต Azure services (App Service)
// =============================================================

param location string

@description('SQL Server name — must be globally unique. ถ้าซ้ำให้ override')
param sqlServerName string

@description('SQL Database name')
param sqlDatabaseName string

@allowed([ 'Basic', 'S0', 'S1', 'S2' ])
param sqlSku string

@description('Entra Object ID ของ SQL Admin (user or group)')
param sqlAdminObjectId string

@description('Entra UPN / display name ของ SQL Admin')
param sqlAdminLogin string

// Map SKU → tier + DTU
var skuTier = {
  Basic: { tier: 'Basic', capacity: 5,  maxBytes: 2147483648 }       // 2 GB
  S0:    { tier: 'Standard', capacity: 10, maxBytes: 268435456000 }   // 250 GB
  S1:    { tier: 'Standard', capacity: 20, maxBytes: 268435456000 }   // 250 GB
  S2:    { tier: 'Standard', capacity: 50, maxBytes: 268435456000 }   // 250 GB
}

resource sqlServer 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: sqlServerName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'  // ถ้าจะ lock ลง: Disabled + private endpoint
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'        // เปลี่ยนเป็น 'Group' ถ้าใช้ Entra group
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true  // ✓ ไม่มี SQL password
    }
  }
}

// อนุญาต Azure services (App Service ฯลฯ) ให้ connect — startIP=endIP=0.0.0.0
resource fwAllowAzure 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: sqlSku
    tier: skuTier[sqlSku].tier
    capacity: skuTier[sqlSku].capacity
  }
  properties: {
    maxSizeBytes: skuTier[sqlSku].maxBytes
    requestedBackupStorageRedundancy: 'Local'  // ถูกสุด (Geo +$$$)
    zoneRedundant: false
  }
}

output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = sqlDb.name
output serverName string = sqlServer.name
output serverId string = sqlServer.id
