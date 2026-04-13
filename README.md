# 🍽️ MenuNest

Family meal-planning web app — สร้าง recipe library, บันทึก stock วัตถุดิบ, วางแผนมื้ออาหาร และเช็คว่าของพอสำหรับเมนูที่เลือกหรือไม่

**Domain:** menunest.app

---

## Features (MVP)

- 🔐 Login ด้วย Microsoft Entra ID (work, school, หรือ personal account)
- 👪 Family — สร้าง family ใหม่ / เข้าร่วมด้วย invite code, กำหนดความสัมพันธ์ของสมาชิก
- 🧂 Ingredient master — รายการวัตถุดิบกลางของครอบครัว (autocomplete + สร้างใหม่ได้)
- 📖 Recipe library — เก็บสูตรอาหาร + รูป + วัตถุดิบและปริมาณ
- 📦 Stock — บันทึกวัตถุดิบที่มี (manual, เพิ่ม/ลดด้วยตนเอง)
- 📅 Meal plan — วางแผนแต่ละวัน × มื้อ (breakfast/lunch/dinner)
- ✅ Stock check — เทียบ recipe กับ stock ปัจจุบัน บอกว่าขาดอะไรบ้าง

---

## Tech Stack

### Frontend — `frontend/`
- React 18 + TypeScript + Vite
- Redux Toolkit (RTK + RTK Query) — state + API client
- React Router v6
- MSAL.js (`@azure/msal-react`) — Entra ID auth
- Syncfusion (Community License) — Grid, Schedule, inputs
- Pattern: page-scoped folders (`pages/{feature}/{components,hooks,api,slice}`) + component + hook style

### Backend — `backend/`
- ASP.NET 10 (LTS) Clean Architecture
- EF Core 10 + SQL Server provider (Azure SQL)
- `Mediator` (martinothamar) — CQRS + pipeline behaviors
- `FluentValidation` — request validation
- `Mapster` — DTO mapping
- `Microsoft.Identity.Web` — JWT bearer auth (multi-tenant + personal)
- `Azure.Storage.Blobs` — recipe image storage
- Serilog + Application Insights

### Infra — `infra/`
- Azure App Service (backend)
- Azure Static Web Apps (frontend)
- Azure SQL Database
- Azure Storage Account (Blob container: `recipe-images`)
- Application Insights
- Azure App Registration (Entra ID — multi-tenant + personal)

---

## Folder Structure

```
menunest/
├── backend/          # ASP.NET 10 Clean Architecture solution
├── frontend/         # Vite + React + TypeScript app
├── docs/             # Architecture, design spec, API docs
└── infra/            # Bicep / ARM templates (optional)
```

Detailed implementation plan: [docs/plan.md](docs/plan.md)

---

## Local Development

### Prerequisites
- .NET 10 SDK
- Node.js 20+ and npm
- Azure SQL / SQL Server LocalDB หรือ Docker SQL container
- Azurite (Blob Storage emulator) หรือ Azure Storage account
- Azure App Registration (สำหรับ Entra ID)

### Setup
```bash
# Backend
cd backend
dotnet restore
dotnet ef database update --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
dotnet run --project src/MenuNest.WebApi
# → https://localhost:5001/swagger

# Frontend (คนละ terminal)
cd frontend
npm install
npm run dev
# → http://localhost:5173
```

Copy `appsettings.Development.json.example` และ `.env.example` แล้วกรอก credential ของตัวเอง

---

## Contributing

โปรเจกต์นี้เป็น family/personal use — ไม่รับ PR ภายนอก

---

## License

Private / unpublished (TBD)
