// =============================================================
// Azure Storage Account + Blob Container — สำหรับ photos
// =============================================================
// - Standard_LRS = ถูกสุด (~$0.018/GB/mo)
// - Disabled shared key — Managed Identity เท่านั้น (security)
// - allowBlobPublicAccess = false (ต้อง SAS URL หรือ MI)
// - CORS เปิดให้ frontend upload ตรงจาก browser
// - Inline role assignment: UAMI → "Storage Blob Data Contributor"
// =============================================================

param location string

@description('Storage Account name — globally unique, 3-24 chars, lowercase + digits only')
param storageAccountName string

@description('Principal ID ของ UAMI ที่จะ grant role')
param uamiPrincipalId string

@description('Allowed CORS origins สำหรับ frontend upload')
param corsOrigins array = [
  'https://green-rock-098e70e00.7.azurestaticapps.net'
  // เพิ่ม dev/staging origin ที่นี่
]

resource storageAccount 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false       // ✓ no anonymous read
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowSharedKeyAccess: false         // ✓ Managed Identity only
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: corsOrigins
          allowedMethods: [ 'GET', 'PUT', 'POST', 'HEAD' ]
          allowedHeaders: [ '*' ]
          exposedHeaders: [ '*' ]
          maxAgeInSeconds: 3600
        }
      ]
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 7   // soft-delete for accidental delete
    }
  }
}

// =============================================================
// Containers
// =============================================================

resource drugImagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'drug-images'        // รูปยา (Drug Master)
  properties: { publicAccess: 'None' }
}

resource episodeImagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'episode-images'     // รูปจาก attack (food, environment, prescription, aura)
  properties: { publicAccess: 'None' }
}

// =============================================================
// Role assignment — UAMI → Storage Blob Data Contributor
// =============================================================

var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, uamiPrincipalId, storageBlobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      storageBlobDataContributorRoleId
    )
    principalId: uamiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output drugImagesContainerName string = drugImagesContainer.name
output episodeImagesContainerName string = episodeImagesContainer.name
