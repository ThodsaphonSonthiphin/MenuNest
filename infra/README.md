# menunest — Infrastructure as Code (Bicep)

โครงสร้าง Azure ของ menunest พร้อม migraine tracker feature

## 📁 ไฟล์ในโฟลเดอร์นี้

```
infra/
├── README.md                       ← คุณอยู่ที่นี่
├── main.bicep                      ← orchestrator (เรียก modules)
├── main.bicepparam                 ← parameters file (ค่าจริง)
└── modules/
    ├── app-service.bicep           ← upgrade plan F1 → B1, configure App Service
    ├── sql.bicep                   ← SQL Server + Basic DB (Entra-only)
    ├── keyvault.bicep              ← KV Standard (deploy เฉพาะถ้า useKeyVault=true)
    └── role-assignments.bicep      ← grant UAMI roles to KV (เฉพาะถ้า useKeyVault=true)
```

## 🔑 Key Vault toggle

**Default = `useKeyVault: false`** — เริ่มจาก **ไม่ใช้** KV (เหมาะ MVP)
- Secrets อยู่ใน App Settings (placeholder "REPLACE-AFTER-DEPLOY-IN-PORTAL")
- ต้องไปตั้งค่าจริงใน Portal/CLI หลัง deploy
- ⚠️ ถ้า redeploy Bicep จะ reset secrets กลับเป็น placeholder → ต้องตั้งใหม่

**สลับเป็น `useKeyVault: true`** เมื่ออยากใช้ KV (production-grade)
- Bicep สร้าง KV + grant UAMI access
- Secrets ใน KV จริง — Bicep redeploy ไม่ทับ
- App Settings เปลี่ยนเป็น `@Microsoft.KeyVault(SecretUri=...)`

## 🎯 ภาพรวมที่ deployment จะทำ (`useKeyVault=false`)

| Resource | Action |
|---|---|
| `MenuNestWeb` (Static Web App) | ❌ ไม่แตะ |
| `ASP-MenuNest-a132` (App Service Plan) | ⬆ Upgrade F1 → B1 |
| `menunest` (App Service) | 🔧 Attach UAMI, Always On, App Settings (กับ secret placeholders) |
| `menunest-id-a4ef` (UAMI) | 🔗 Reference only (existing) |
| `menunest` (App Insights) | 🔗 Reference only (existing) |
| `menunest-sql` (SQL Server) | 🆕 Create — Entra-only auth |
| `MenuNest` (SQL Database) | 🆕 Create — Basic 5 DTU, 2 GB |
| `menunest-kv` (Key Vault) | ⏸ **Skip** (deploy เมื่อสลับ `useKeyVault=true`) |

## ⚠️ ก่อน deploy ต้องเตรียม

### 1. หา Entra Object ID + UPN (สำหรับ SQL Admin)

```powershell
az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" -o json
```

แล้วใส่ค่าใน [main.bicepparam](main.bicepparam):

```bicep
param sqlAdminObjectId = '<paste-object-id>'
param sqlAdminLogin    = '<paste-upn>'
```

### 2. ตรวจ subscription พร้อม deploy

```powershell
az account show --query "{name:name, state:state}"
# state ต้องเป็น "Enabled" — ถ้า "Warned" ต้อง re-activate ก่อน
```

### 3. Login กับ tenant ที่ถูก (Pay-As-You-Go = personal tenant)

```powershell
az login --tenant d500e2f4-1325-41d2-9f92-2f2f39e8ea19
az account set --subscription "Pay-As-You-Go"
```

## 🔍 Preview deployment (what-if) — สำคัญสุด!

ดูว่าจะเปลี่ยนอะไรบ้าง **ก่อน** apply จริง:

```powershell
az deployment group what-if `
  --resource-group MenuNest `
  --template-file infra/main.bicep `
  --parameters infra/main.bicepparam
```

อ่าน output อย่างละเอียด:
- `+` = create new
- `~` = modify existing
- `-` = delete (❌ ถ้าเห็น = check ทันที)
- `=` = no change

⚠️ **ระวัง**: `appSettings` ของ App Service ใน Bicep จะ **replace** ของเดิม — ถ้ามี settings ที่ตั้ง manual ใน portal จะหาย ให้ add เข้า `app-service.bicep` ก่อน

## 🚀 Deploy

ถ้า what-if ดูโอเค:

```powershell
az deployment group create `
  --resource-group MenuNest `
  --template-file infra/main.bicep `
  --parameters infra/main.bicepparam `
  --name menunest-$(Get-Date -Format yyyyMMdd-HHmm)
```

ใช้เวลา ~5-10 นาที (SQL Server สร้างนานสุด ~3-5 นาที)

## 🔐 หลัง deploy — ตั้งค่าที่ Bicep ทำไม่ได้

### A. ตั้ง secret values ใน App Settings (กรณี `useKeyVault=false`)

```powershell
# Generate VAPID keypair (one-time, save ทั้งคู่)
npx web-push generate-vapid-keys

# ใส่ VAPID public + private (replace placeholder)
az webapp config appsettings set --name menunest --resource-group MenuNest --settings `
  VapidPublicKey="<your-vapid-pub>" `
  VapidPrivateKey="<your-vapid-priv>"

# QR token signing key (HMAC random 64 bytes base64)
$qrKey = [Convert]::ToBase64String((1..64 | %{Get-Random -Min 0 -Max 255}))
az webapp config appsettings set --name menunest --resource-group MenuNest --settings `
  QrTokenSigningKey="$qrKey"

# Google OAuth client secret (ถ้าใช้)
az webapp config appsettings set --name menunest --resource-group MenuNest --settings `
  Google__ClientSecret="<your-google-secret>"
```

### A2. ใส่ secrets ลง Key Vault (กรณี `useKeyVault=true`)

```powershell
# หลัง deploy ด้วย useKeyVault=true แล้ว
az keyvault secret set --vault-name menunest-kv --name VapidPrivateKey --value "<your-vapid-priv>"
az keyvault secret set --vault-name menunest-kv --name VapidPublicKey  --value "<your-vapid-pub>"
az keyvault secret set --vault-name menunest-kv --name QrTokenSigningKey --value "$qrKey"
az keyvault secret set --vault-name menunest-kv --name GoogleClientSecret --value "<your-google-secret>"

# App Settings ใน Bicep จะ reference เป็น @Microsoft.KeyVault(...) อยู่แล้ว
# App Service จะ fetch secret ผ่าน UAMI อัตโนมัติ
```

### B. Grant UAMI access ใน SQL Database (T-SQL)

Connect ด้วย Entra admin (ผู้ที่กำหนดใน `sqlAdminLogin`):

```sql
-- ใน Azure Data Studio หรือ SQL portal Query Editor
CREATE USER [menunest-id-a4ef] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [menunest-id-a4ef];
-- (ถ้าต้องการ granular ใช้ db_datareader + db_datawriter + db_ddladmin)
```

### C. Run EF migration เพื่อสร้าง schema

```powershell
# จาก backend/src/MenuNest.WebApi/
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ConnectionStrings__DefaultConnection = "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
dotnet ef database update --project ../MenuNest.Infrastructure --startup-project .
```

### D. Restart App Service (apply identity binding)

```powershell
az webapp restart --name menunest --resource-group MenuNest
```

## 🔄 Re-deploy / update

แก้ Bicep → run what-if → deploy. Bicep idempotent ดังนั้น run ซ้ำกี่ครั้งก็ได้

```powershell
# After editing modules/sql.bicep (e.g., scale Basic → S0):
az deployment group what-if `
  --resource-group MenuNest `
  --template-file infra/main.bicep `
  --parameters infra/main.bicepparam
```

## 💰 Cost summary

```
                            useKeyVault=false   useKeyVault=true
App Service Plan B1       :  $13.14            $13.14
SQL Database Basic        :  $ 4.83            $ 4.83
Storage Account LRS       :  $ 0.50            $ 0.50  (~50MB photos + ops)
Key Vault Standard        :  $ 0.00            ~$0.30 (per-op)
App Insights              :  $ 0.00            $ 0.00 (under 5GB free)
Static Web Apps Free      :  $0                $0
UAMI                      :  $0                $0
Microsoft Entra (50k MAU) :  $0                $0
─────────────────────────────────────────────────────
TOTAL                     ≈ $18.47 /mo        ≈ $18.77 /mo
                            (~฿650)            (~฿665)
```

## 🔁 Migrate ไป Key Vault (เมื่ออยาก)

```powershell
# 1. แก้ main.bicepparam
#    param useKeyVault = true

# 2. Preview
az deployment group what-if `
  --resource-group MenuNest `
  --template-file infra/main.bicep `
  --parameters infra/main.bicepparam
# จะเห็น:
#   + Microsoft.KeyVault/vaults menunest-kv
#   + Microsoft.Authorization/roleAssignments (UAMI → KV Secrets User)
#   ~ menunest (App Service) — appSettings change: placeholder → @Microsoft.KeyVault(...)

# 3. Deploy
az deployment group create `
  --resource-group MenuNest `
  --template-file infra/main.bicep `
  --parameters infra/main.bicepparam

# 4. ใส่ secret values ลง KV (ดู section A2)
az keyvault secret set --vault-name menunest-kv --name VapidPrivateKey --value "..."
# ...

# 5. Restart app (apply KV refs)
az webapp restart --name menunest --resource-group MenuNest
```

## 🆘 Troubleshooting

### "Subscription is read-only"
Sub ยัง disabled — re-activate ที่ Azure portal → Subscriptions → Reactivate

### "SQL server name not available"
ชื่อ `menunest-sql` ซ้ำ — override param `baseName` หรือเปลี่ยน server name

### "App Service properties reset"
appSettings ใน Bicep replace ของเดิม — เพิ่ม settings ที่หายเข้า `app-service.bicep`

### "Cannot upgrade plan: Linux/Windows mismatch"
ถ้า existing plan เป็น Linux — ปรับ `kind: 'linux'` และ `reserved: true` ใน app-service.bicep

## 📚 References

- [Bicep documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure SQL Entra auth](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview)
- [Key Vault references in App Service](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
- [Azure best practices (MCP)](https://github.com/microsoft/mcp)
