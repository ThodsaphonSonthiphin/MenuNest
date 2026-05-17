# Migraine Tracker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a migraine/medication tracker module to MenuNest. Users log symptom episodes with severity, record medication intakes linked to episodes, get 30-minute follow-up web push pings, and generate QR-shareable doctor reports. Schema and UX are specifically designed for migraine clinical needs (MOH risk detection, aura tracking, treatment efficacy, trigger correlation), but the engine is generic enough for other symptom types (stomach, fever, etc.).

**Architecture:**
- Backend: New `Health` namespace alongside existing `MealPlan` / `Budget` / `Chat` modules — Domain entities, EF configs, CQRS handlers via Mediator, REST controllers.
- Frontend: New `pages/health/` directory with 7 patient screens + 1 public doctor report route. Reuses existing MSAL auth, Redux Toolkit + RTK Query, Syncfusion components.
- BackgroundService: `FollowUpDispatcher` polls `FollowUpPings` table every minute, sends VAPID web push.
- Storage: New `drug-images` + `episode-images` containers on Azure Blob, direct browser upload via SAS URL.
- Doctor report: SPA route guarded by HMAC-signed JWT (no Entra). React renders timeline from JSON API.
- Phase 1 = manual workflows; Phase 2 = Gemini AI-assisted drug entry from photos.

**Tech Stack:**
- Backend: ASP.NET 10, EF Core 10, `martinothamar/Mediator`, FluentValidation, Microsoft.Identity.Web, `WebPush` NuGet (VAPID), `Azure.Storage.Blobs`, xUnit + Moq + FluentAssertions + InMemory DbContext
- Frontend: React 19, Redux Toolkit + RTK Query, Syncfusion Pure React (AutoComplete, MultiSelect, BarcodeGenerator, Dialog), react-hook-form, Service Worker (Vite plugin or vanilla)
- Infra: Bicep IaC (`infra/`)
- Verification: Playwright smoke test of full attack → push → resolve flow

**Specs / Mocks:** All HTML mocks live in `docs/mocks/`:
- [doctor-report-mock.html](../../mocks/doctor-report-mock.html)
- [patient-active-episode-mock.html](../../mocks/patient-active-episode-mock.html)
- [patient-quick-log-mock.html](../../mocks/patient-quick-log-mock.html)
- [patient-take-medication-mock.html](../../mocks/patient-take-medication-mock.html)
- [patient-followup-mock.html](../../mocks/patient-followup-mock.html)
- [patient-history-mock.html](../../mocks/patient-history-mock.html)
- [patient-drug-master-mock.html](../../mocks/patient-drug-master-mock.html)
- [patient-search-photo-mock.html](../../mocks/patient-search-photo-mock.html)
- [patient-gemini-confirm-mock.html](../../mocks/patient-gemini-confirm-mock.html) (Phase 2)

---

## Phasing

| Phase | Scope | Effort estimate |
|---|---|---|
| **Phase 1 (MVP)** | Manual Drug Master, Quick Log Attack, Active Episode, Take Medication, Follow-up loop, History, Doctor Report (QR), Photo upload (manual), Search | 8-12 days |
| **Phase 2** | Gemini AI-assisted drug creation (multimodal + tools + confirmation) | 2-3 days |
| **Phase 3 (future)** | Multi-user/family, voice input (Azure Speech), advanced analytics, native mobile | TBD |

---

## Pre-flight (one-time setup)

- [ ] **Reactivate Pay-As-You-Go subscription** in Azure portal (currently `Warned` / `AdminDisabled`).
- [ ] **Verify `az login` tenant**: `az login --tenant d500e2f4-1325-41d2-9f92-2f2f39e8ea19`
- [ ] **Set `sqlAdminObjectId` + `sqlAdminLogin`** in `infra/main.bicepparam` from `az ad signed-in-user show --query "{id:id, upn:userPrincipalName}"`.
- [ ] **Generate VAPID keypair** locally: `npx web-push generate-vapid-keys` → save public + private for later step.

---

## Phase 1 (MVP) — File Structure

### Backend — Domain (`backend/src/MenuNest.Domain/`)

- `Entities/Drug.cs`
- `Entities/Symptom.cs`
- `Entities/Trigger.cs`
- `Entities/SymptomEpisode.cs`
- `Entities/Intake.cs`
- `Entities/FollowUpPing.cs`
- `Entities/WebPushSubscription.cs`
- `Entities/ShareLink.cs`
- `Entities/Photo.cs` *(decision: separate entity for audit + reuse — see open Q below)*
- `Enums/DrugType.cs` — Analgesic, NSAID, Triptan, Other
- `Enums/SymptomLocation.cs` — Left, Right, Bilateral, Frontal, Temporal, Occipital
- `Enums/SymptomQuality.cs` — Throbbing, Pressure, Stabbing, Burning
- `Enums/AssociatedSymptom.cs` — Nausea, Vomiting, Photophobia, Phonophobia, Osmophobia
- `Enums/AuraType.cs` — Visual, Sensory, Speech, Motor
- `Enums/FunctionalImpact.cs` — None, Mild, Moderate, SevereBedrest
- `Enums/NoDrugReason.cs` — MaxDoseReached, AllDrugsActive, OutOfStock, NoDrugTreatsThis, UserSkip
- `Enums/PingStatus.cs` — Pending, Asked, Answered, Missed
- `Enums/PingResponse.cs` — Resolved, Improved, Same, Worse, RetroResolved, RetroUnknown
- `Enums/PhotoParentType.cs` — Drug, Episode, Intake

### Backend — Application (`backend/src/MenuNest.Application/`)

#### Abstractions
- `Abstractions/IWebPushSender.cs`
- `Abstractions/IBlobSasGenerator.cs`
- `Abstractions/IShareTokenService.cs` *(HMAC sign/verify)*
- `Abstractions/IClock.cs` *(if not already present — for testable time)*

#### Use Cases (one folder per use case, contains Command/Query + Handler + Validator)

```
UseCases/Health/
├── DrugDtos.cs
├── SymptomDtos.cs
├── EpisodeDtos.cs
├── IntakeDtos.cs
├── DrugMaster/
│   ├── ListDrugs/
│   ├── GetDrug/
│   ├── CreateDrug/
│   ├── UpdateDrug/
│   ├── DeleteDrug/
│   └── AttachPhotosToDrug/
├── Symptoms/
│   ├── ListSymptoms/         (seeds + user-custom)
│   ├── CreateCustomSymptom/
│   └── ListTriggers/         + CreateCustomTrigger/
├── Episodes/
│   ├── StartEpisode/         (the "Quick Log Attack" command)
│   ├── GetActiveEpisodes/
│   ├── GetEpisode/
│   ├── UpdateEpisode/        (severity update, edit attrs)
│   ├── ResolveEpisode/       (manual + via follow-up response)
│   ├── ListEpisodes/         (history)
│   └── DeleteEpisode/
├── Intakes/
│   ├── LogIntake/
│   ├── GetTakeMedicationContext/  (returns: active drugs / takeable / blocked)
│   └── LogNoDrug/            (records reason)
├── FollowUps/
│   ├── ScheduleFollowUp/
│   ├── RecordPingResponse/   (called from push action OR in-app)
│   ├── RetroCloseEpisode/    (modal on next app open)
│   └── GetPendingPings/      (for BackgroundService)
├── Photos/
│   └── RequestUploadSas/     (returns SAS URL for direct browser upload)
├── PushSubscriptions/
│   ├── SubscribeWebPush/
│   └── UnsubscribePush/
├── Share/
│   ├── CreateShareLink/      (returns token + QR data URL)
│   ├── RevokeShareLink/
│   └── ListMyShareLinks/
└── Reports/
    └── GetDoctorReport/      (public, token-gated — main report payload)
```

#### Behaviors (Mediator pipeline)

- `Behaviors/CurrentUserResolver.cs` *(if not already present — gets `User` from Entra `oid` claim)*

### Backend — Infrastructure (`backend/src/MenuNest.Infrastructure/`)

- `Persistence/Configurations/DrugConfiguration.cs`
- `Persistence/Configurations/SymptomConfiguration.cs`
- `Persistence/Configurations/TriggerConfiguration.cs`
- `Persistence/Configurations/SymptomEpisodeConfiguration.cs` *(JSON columns for `trigger_ids`, `aura_types`, `associated_symptoms`)*
- `Persistence/Configurations/IntakeConfiguration.cs`
- `Persistence/Configurations/FollowUpPingConfiguration.cs`
- `Persistence/Configurations/WebPushSubscriptionConfiguration.cs`
- `Persistence/Configurations/ShareLinkConfiguration.cs`
- `Persistence/Configurations/PhotoConfiguration.cs`
- `Persistence/Seed/HealthSeed.cs` — seeds 20 common Symptoms + 15 Triggers + 5 Drugs as `is_seed=true`
- `Services/WebPushSender.cs` — implements `IWebPushSender` with `WebPush` NuGet
- `Services/AzureBlobSasGenerator.cs` — `DefaultAzureCredential` + user-delegation SAS
- `Services/HmacShareTokenService.cs` — sign/verify HMAC JWT for share links
- `BackgroundServices/FollowUpDispatcher.cs` — `BackgroundService` w/ `PeriodicTimer(1 min)`
- `Persistence/Migrations/YYYYMMDD_HealthInitial.cs` (created via `dotnet ef migrations add`)
- `Persistence/Migrations/YYYYMMDD_HealthSeed.cs` (created in a separate migration so seed can be re-run idempotently)

### Backend — WebApi (`backend/src/MenuNest.WebApi/`)

- `Controllers/DrugsController.cs`
- `Controllers/SymptomsController.cs`
- `Controllers/EpisodesController.cs`
- `Controllers/IntakesController.cs`
- `Controllers/FollowUpsController.cs`
- `Controllers/PhotosController.cs`
- `Controllers/PushSubscriptionsController.cs`
- `Controllers/ShareLinksController.cs`
- `Controllers/PublicReportController.cs` — `[AllowAnonymous]` + token-gating action filter
- `Filters/ShareTokenAuthorizationFilter.cs`
- `Program.cs` — register `AddHostedService<FollowUpDispatcher>()`, `IWebPushSender`, `IBlobSasGenerator`, `IShareTokenService`

### Backend — Tests (`backend/tests/`)

- `MenuNest.Application.UnitTests/Health/StartEpisodeHandlerTests.cs`
- `MenuNest.Application.UnitTests/Health/LogIntakeHandlerTests.cs`
- `MenuNest.Application.UnitTests/Health/GetTakeMedicationContextHandlerTests.cs` *(critical — 3 categories logic)*
- `MenuNest.Application.UnitTests/Health/RecordPingResponseHandlerTests.cs`
- `MenuNest.Application.UnitTests/Health/RetroCloseEpisodeHandlerTests.cs`
- `MenuNest.Application.UnitTests/Health/CreateShareLinkHandlerTests.cs`
- `MenuNest.Application.UnitTests/Health/GetDoctorReportHandlerTests.cs` *(verify stats calc: MOH risk, trigger correlation)*
- `MenuNest.Infrastructure.IntegrationTests/Health/FollowUpDispatcherTests.cs`

### Frontend — create (`frontend/src/pages/health/`)

```
pages/health/
├── HomePage.tsx                            (replaces or extends current home for Health module)
├── QuickLogAttackPage.tsx
├── ActiveEpisodePage.tsx
├── TakeMedicationPage.tsx
├── HistoryPage.tsx
├── EpisodeDetailPage.tsx
├── DrugMasterPage.tsx
├── DrugFormPage.tsx
├── SettingsPage.tsx                        (push permission toggle)
├── PublicReportPage.tsx                    (route /share/:token, NO auth required)
├── components/
│   ├── ActiveBanner.tsx
│   ├── SeveritySlider.tsx
│   ├── AttributePillRow.tsx
│   ├── DrugCard.tsx                        (used in list + picker)
│   ├── DrugPicker.tsx                      (3 categories)
│   ├── TimerLive.tsx                       (live-updating countdown)
│   ├── FollowUpModal.tsx
│   ├── RetroCloseModal.tsx
│   ├── PhotoUploader.tsx                   (multi-upload + compression)
│   ├── PhotoStrip.tsx                      (thumbnail row)
│   ├── EpisodeListItem.tsx
│   ├── TimelineView.tsx                    (Active Episode + Detail)
│   ├── SearchAutoComplete.tsx              (Syncfusion AutoComplete wrapper)
│   └── DoctorReport/
│       ├── ClinicalFlags.tsx
│       ├── MigraineProfile.tsx
│       ├── TriggerCorrelation.tsx
│       ├── TreatmentEfficacy.tsx
│       ├── PatternAnalysis.tsx
│       └── DayCard.tsx
├── hooks/
│   ├── useActiveEpisodes.ts
│   ├── useStartEpisode.ts
│   ├── useDrugs.ts
│   ├── useDrugMutations.ts
│   ├── useTakeMedicationContext.ts         (queries 3-category data)
│   ├── useLogIntake.ts
│   ├── useFollowUpPing.ts
│   ├── useRetroClose.ts
│   ├── usePhotoUpload.ts                   (compress + SAS + PUT)
│   ├── useShareLink.ts
│   ├── useDoctorReport.ts
│   └── useWebPushSubscription.ts           (Service Worker + VAPID)
└── styles/
    └── health.css                          (dark mode default, photophobia-friendly)
```

### Frontend — modify

- `frontend/src/router.tsx` — add new routes under `/health` + public `/share/:token`
- `frontend/src/shared/api/api.ts` — add all Health endpoints to RTK Query
- `frontend/public/sw.js` — Service Worker for web push (new file)
- `frontend/public/manifest.json` — PWA manifest (new file or update)
- `frontend/src/main.tsx` — register Service Worker

### Infrastructure (already drafted in `infra/`)

- [ ] Apply Bicep with what-if first (see Deployment Checklist below).

---

## Phase 1 — Tasks

### Task 1: Domain entities + enums

**Files:** create the Domain entities and enums listed above.

- [x] **Step 1: Enums** — Create all enums under `Enums/` (DrugType, SymptomLocation, SymptomQuality, AssociatedSymptom, AuraType, FunctionalImpact, NoDrugReason, PingStatus, PingResponse, PhotoParentType).
- [x] **Step 2: `Drug.cs`** — entity with `Create`, `UpdateProfile`, `UpdateStock`, `MarkExpired`, `AddTreats(symptomId)`, `RemoveTreats(symptomId)`, `AttachPhoto(blobUrl)`. Owns M2M with Symptom via owned `DrugTreatsSymptom`.
- [x] **Step 3: `Symptom.cs`** + `Trigger.cs` — simple entities with `Create(name, isSeed, userId)`. `is_seed=true` rows have `userId=null` and are global; otherwise per-user custom.
- [x] **Step 4: `SymptomEpisode.cs`** — entity with `Start(userId, symptomId, severity, attributes, triggerIds, isOnPeriod)`, `UpdateSeverity`, `UpdateAttributes`, `Resolve(endedAt, severityAfter)`, `MarkNoDrug(reason)`, `RetroClose(estimatedDuration, outcome)`, `AddPhoto`. Aura/location/quality/associated_symptoms[] are migraine-specific nullable properties.
- [x] **Step 5: `Intake.cs`** — `Create(userId, drugId, takenAt, doseAmount, episodeId?)`, `AttachPhoto`.
- [x] **Step 6: `FollowUpPing.cs`** — `Schedule(episodeId, scheduledAt)`, `MarkAsked`, `RecordResponse(response, severityAtCheck?)`, `MarkMissed`. Encapsulates state machine.
- [x] **Step 7: `WebPushSubscription.cs`** — stores PushSubscription JSON (endpoint, keys.p256dh, keys.auth). One per user per browser/device.
- [x] **Step 8: `ShareLink.cs`** — stores HMAC token hash (not raw token), date range, expiry, revoked_at. `Create`, `Revoke`, `IsValid(at)`.
- [x] **Step 9: `Photo.cs`** — generic photo entity with `parent_type` enum + `parent_id` Guid + `blob_url`. Allows photos to attach to Drug / Episode / Intake without separate tables.
- [x] **Step 10: Build** `dotnet build backend/MenuNest.sln` and fix compile errors.

### Task 2: EF configurations + initial migration

**Files:** create EF configs under `Infrastructure/Persistence/Configurations/` + add migration.

- [x] **Step 1:** Write all `IEntityTypeConfiguration<T>` classes. Use JSON columns for `trigger_ids`, `aura_types`, `associated_symptoms` on Episode (matches existing `ShoppingListConfiguration.cs` JSON pattern).
- [x] **Step 2:** Add `DbSet` properties to `AppDbContext`: `Drugs`, `Symptoms`, `Triggers`, `SymptomEpisodes`, `Intakes`, `FollowUpPings`, `WebPushSubscriptions`, `ShareLinks`, `Photos`, plus join table `DrugTreatsSymptoms`. Also updated `IApplicationDbContext` interface and `InMemoryAppDbContext` test fixture with JSON conversions.
- [x] **Step 3:** Create migration: `dotnet ef migrations add HealthInitial -p backend/src/MenuNest.Infrastructure -s backend/src/MenuNest.WebApi` → `20260517100425_HealthInitial.cs`
- [x] **Step 4:** Inspect generated migration, verify indexes (especially `(user_id, started_at)` on Episode, `(symptom_episode_id, scheduled_at, status)` on Ping for dispatcher query).
- [ ] **Step 5:** Local apply: `dotnet ef database update` against localdb. *(deferred — apply at user's convenience)*

### Task 3: Seed migration

- [x] **Step 1:** Create `HealthSeed` migration with predefined 20 Symptoms (ปวดหัว, ไมเกรน, ไข้, ปวดท้อง, ปวดประจำเดือน, ไอ, จาม, ปวดเมื่อย, ปวดข้อ, คลื่นไส้, …) and 15 Triggers (เครียด, นอนน้อย, ฮอร์โมน, อาหาร, อากาศ, แสง, เสียง, ออกกำลังกาย, ฯลฯ) — all with `is_seed=true`, `user_id=null`.
- [x] **Step 2:** `migrationBuilder.InsertData(...)` with fixed Guids (`11111111-0001-...` for Symptoms, `22222222-0001-...` for Triggers) — idempotent across runs. Down() removes only those rows, preserving user-custom data.
- [ ] **Step 3:** Apply and verify rows present. *(deferred — apply at user's convenience)*

### Task 4: Application DTOs + Abstractions

- [ ] **Step 1:** Create `DrugDtos.cs`, `SymptomDtos.cs`, `EpisodeDtos.cs`, `IntakeDtos.cs` — sealed records mirroring DB shape but flattened for API.
- [ ] **Step 2:** Add abstractions:
  - `IWebPushSender.SendAsync(WebPushSubscription, PushPayload, CancellationToken)`
  - `IBlobSasGenerator.GenerateUploadSas(container, blobName, duration)` and `.GenerateReadSas(blobUrl, duration)`
  - `IShareTokenService.Sign(userId, dateFrom, dateTo, expiresAt)` returns `string token`; `.Verify(token)` returns `ShareTokenClaims` or throws.
- [ ] **Step 3:** Register in `Program.cs` (DI).

### Task 5: Drug Master CRUD handlers + controller

**Files:** `UseCases/Health/DrugMaster/*`, `WebApi/Controllers/DrugsController.cs`

- [ ] **Step 1:** `ListDrugsHandler` — returns user's drugs, optional `symptomId` filter (for "ยาสำหรับ X" view).
- [ ] **Step 2:** `GetDrugHandler` — single drug detail.
- [ ] **Step 3:** `CreateDrugHandler` + `CreateDrugValidator` — required: name, drug_type, dose_strength, effect_duration_min/max, max_daily_dose. Optional: stock, expiration, treats[], usage_note.
- [ ] **Step 4:** `UpdateDrugHandler` — same fields, partial update.
- [ ] **Step 5:** `DeleteDrugHandler` — soft delete (set `deleted_at`) to preserve referential integrity with historical Intakes.
- [ ] **Step 6:** `AttachPhotosToDrugHandler` — accepts blob URLs list, validates each starts with our blob endpoint, attaches Photo records.
- [ ] **Step 7:** `DrugsController` — `[Authorize]`, REST verbs, returns `DrugDto`/`DrugListDto`.
- [ ] **Step 8:** Unit tests with `HandlerTestFixture` (InMemory DbContext).

### Task 6: Symptom + Trigger handlers

- [ ] **Step 1:** `ListSymptomsHandler` — returns seeds (user_id IS NULL) UNION user-custom (user_id = current). For AutoComplete data source.
- [ ] **Step 2:** `CreateCustomSymptomHandler` — validates name uniqueness within user's scope.
- [ ] **Step 3:** Same pair for Trigger.
- [ ] **Step 4:** `SymptomsController` exposes both as `/api/symptoms` + `/api/triggers`.

### Task 7: Episode handlers (Quick Log + Active + History)

- [ ] **Step 1:** `StartEpisodeHandler` — creates SymptomEpisode with required severity + optional migraine attributes. Returns episode ID + redirect hint to Active Episode.
- [ ] **Step 2:** `GetActiveEpisodesHandler` — returns episodes with `ended_at IS NULL` for current user.
- [ ] **Step 3:** `GetEpisodeHandler` — single episode with intakes, follow-up pings, photos.
- [ ] **Step 4:** `ListEpisodesHandler` — paginated, filters by date range, symptom, outcome, period, aura. Used in History list.
- [ ] **Step 5:** `UpdateEpisodeHandler` — edit attributes + severity. Marks `updated_at`.
- [ ] **Step 6:** `ResolveEpisodeHandler` — sets `ended_at`, `severity_after=0`, cancels pending pings.
- [ ] **Step 7:** `DeleteEpisodeHandler` — soft delete with confirmation flag.
- [ ] **Step 8:** `EpisodesController` exposes all + `GET /api/episodes/active`.

### Task 8: Take Medication context + Intake handlers

This is the **critical 3-categories logic** — gets thorough tests.

- [ ] **Step 1:** `GetTakeMedicationContextHandler` — given `episodeId`, returns:
  - **Active drugs**: intakes where `taken_at + effect_duration_max_hr > NOW()`. Include drug info, remaining time, progress %.
  - **Takeable drugs**: drugs that `treat` the episode's symptom, NOT in active list, `stock > 0`, `daily_dose_sum(last 24h) < max_daily_dose`.
  - **Blocked drugs**: drugs that should be takeable but are blocked. Include reason: `max_dose_reached` / `still_active` / `out_of_stock`. Include `available_again_at` timestamp.
- [ ] **Step 2:** `LogIntakeHandler` — creates Intake, decrements drug stock (if Phase 1 manual = NO auto-decrement; just record). Schedule a `FollowUpPing` for `+30 minutes` (configurable per-drug if exists).
- [ ] **Step 3:** `LogNoDrugHandler` — marks episode `no_drug_taken=true` + reason. No Intake row created. Still schedules a "self-resolving" follow-up at +60 min.
- [ ] **Step 4:** `IntakesController` REST + `GET /api/episodes/{id}/take-medication-context`.
- [ ] **Step 5:** Comprehensive unit tests:
  - All takeable
  - Some active (yellow)
  - Some blocked by max dose
  - Some blocked by stock
  - All blocked → empty takeable, full blocked list
  - Drug not treating symptom → not in either list

### Task 9: Follow-up handlers + BackgroundService

- [ ] **Step 1:** `ScheduleFollowUpHandler` — called by `LogIntakeHandler` and `LogNoDrugHandler`. Idempotent (returns existing if pending).
- [ ] **Step 2:** `RecordPingResponseHandler` — accepts `pingId` + response (resolved/improved/same/worse) + optional severity. If `resolved` → also call `ResolveEpisode`. If `improved`/`worse` → schedule another ping +30 min (max 3 total).
- [ ] **Step 3:** `RetroCloseEpisodeHandler` — accepts `episodeId` + outcome (resolved/improved/unknown) + estimated_duration. Sets `retro_closed=true`. Cancels pending pings.
- [ ] **Step 4:** `GetPendingPingsHandler` — for dispatcher. Returns pings where `scheduled_at <= NOW()` AND `status=pending` LIMIT 50.
- [ ] **Step 5:** `FollowUpDispatcher : BackgroundService` — in `Infrastructure/BackgroundServices/`:
  ```csharp
  protected override async Task ExecuteAsync(CancellationToken ct)
  {
      using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
      while (await timer.WaitForNextTickAsync(ct))
      {
          using var scope = _sp.CreateScope();
          var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
          var pings = await mediator.Send(new GetPendingPingsQuery(), ct);
          foreach (var ping in pings)
          {
              try { await _pushSender.SendAsync(ping, ct); }
              catch (Exception ex) { _logger.LogError(ex, "Push failed"); }
          }
      }
  }
  ```
- [ ] **Step 6:** Register in `Program.cs`: `builder.Services.AddHostedService<FollowUpDispatcher>();`
- [ ] **Step 7:** Integration test that schedules 3 pings, runs dispatcher tick, asserts pushes attempted.

### Task 10: Web push subscription + VAPID

- [ ] **Step 1:** Generate VAPID keypair (one-time, see Pre-flight). Store public in `appsettings.json` as `Push:VapidPublicKey` (NOT secret). Private will go to App Service config later.
- [ ] **Step 2:** `WebPushSender.cs` — uses `WebPush` NuGet:
  ```csharp
  public async Task SendAsync(FollowUpPing ping, CancellationToken ct)
  {
      var subscription = ping.Episode.User.PushSubscriptions.First();
      var payload = JsonSerializer.Serialize(new {
          title = $"🤒 {ping.SymptomName}เป็นยังไงบ้าง?",
          body = $"~{ping.MinutesSinceIntake} นาทีหลังกิน {ping.LastDrugName}",
          actions = [
              new { action = "resolved", title = "หาย" },
              new { action = "improved", title = "ดีขึ้น" },
              new { action = "same", title = "เท่าเดิม" },
              new { action = "worse", title = "แย่ลง" }
          ],
          data = new { pingId = ping.Id, episodeId = ping.EpisodeId }
      });
      var pushSubscription = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
      var vapidDetails = new VapidDetails("mailto:admin@menunest.com", _vapidPublicKey, _vapidPrivateKey);
      await _client.SendNotificationAsync(pushSubscription, payload, vapidDetails, ct);
  }
  ```
- [ ] **Step 3:** `SubscribeWebPushHandler` / `UnsubscribePushHandler` + REST endpoints.
- [ ] **Step 4:** Add `Push:VapidPrivateKey` to App Service config placeholder.

### Task 11: Photo upload (SAS pattern)

- [ ] **Step 1:** `RequestUploadSasHandler` — accepts `container` ("drug-images" or "episode-images") + suggested filename. Generates a user-delegation SAS valid 15 min, write-only, returns `{ uploadUrl, blobUrl }`.
- [ ] **Step 2:** `AzureBlobSasGenerator` implementation using `DefaultAzureCredential` (UAMI in prod) and `GetUserDelegationKey`.
- [ ] **Step 3:** `PhotosController` `POST /api/photos/upload-sas`.
- [ ] **Step 4:** Frontend `usePhotoUpload.ts` hook:
  - Browser-side compress to ~500KB via canvas (max width 1600px, JPEG quality 0.85)
  - Request SAS
  - `fetch(uploadUrl, { method: 'PUT', body: blob, headers: { 'x-ms-blob-type': 'BlockBlob' } })`
  - Return `blobUrl` to caller
- [ ] **Step 5:** Photo entity records the upload (no separate "register photo" call — Drug/Episode handlers accept `blob_urls[]` and create Photo rows).

### Task 12: Share link + Doctor report

- [ ] **Step 1:** Generate `Push:QrTokenSigningKey` (256-bit random) — placeholder in App Service config (`base64(64 random bytes)`).
- [ ] **Step 2:** `HmacShareTokenService` — JWT with claims: `sub=userId, df=dateFrom, dt=dateTo, exp=expiresAt, jti=tokenId`. Signed with HMAC-SHA256.
- [ ] **Step 3:** `CreateShareLinkHandler` — accepts date range, generates token, hashes it (so DB never stores raw token), persists ShareLink row, returns `{ token, shareUrl, qrDataUrl }`. QR generated via Syncfusion `BarcodeGenerator` on frontend OR `QRCoder` NuGet on backend.
- [ ] **Step 4:** `RevokeShareLinkHandler` + `ListMyShareLinksHandler`.
- [ ] **Step 5:** `GetDoctorReportHandler` — given token (passed via filter), loads user's episodes in date range, computes:
  - Total episodes, days affected, avg duration, peak severity
  - **MOH risk**: `acute_med_days > 10 in last 30` → flag
  - Aura prevalence, associated symptoms breakdown
  - Location/quality distribution
  - Trigger correlation %
  - Treatment efficacy table per drug
  - Pattern: time of day, day of week, menstrual correlation rate
  - Day-by-day timeline with episode + intake + follow-up events
- [ ] **Step 6:** `ShareTokenAuthorizationFilter` — extracts token from query string `?t=...`, verifies signature + expiry + revoked status, sets `User.Identity` claims, allows endpoint.
- [ ] **Step 7:** `PublicReportController.GetReport([FromQuery] string t)` — `[AllowAnonymous]` + filter applied.
- [ ] **Step 8:** Frontend `PublicReportPage.tsx` reads `?t=` from URL, calls report endpoint, renders `DoctorReport/*` components (built from `doctor-report-mock.html`).

### Task 13: Frontend RTK Query + routing

- [ ] **Step 1:** Update `shared/api/api.ts` with all Health endpoints (matches Controllers above).
- [ ] **Step 2:** Update `router.tsx`:
  ```tsx
  { path: 'health',           element: <ProtectedRoute><HealthHomePage /></ProtectedRoute> }
  { path: 'health/log',       element: <ProtectedRoute><QuickLogAttackPage /></ProtectedRoute> }
  { path: 'health/active/:id', element: <ProtectedRoute><ActiveEpisodePage /></ProtectedRoute> }
  { path: 'health/take-med/:episodeId', element: <ProtectedRoute><TakeMedicationPage /></ProtectedRoute> }
  { path: 'health/history',   element: <ProtectedRoute><HistoryPage /></ProtectedRoute> }
  { path: 'health/episode/:id', element: <ProtectedRoute><EpisodeDetailPage /></ProtectedRoute> }
  { path: 'health/drugs',     element: <ProtectedRoute><DrugMasterPage /></ProtectedRoute> }
  { path: 'health/drugs/new', element: <ProtectedRoute><DrugFormPage /></ProtectedRoute> }
  { path: 'health/drugs/:id/edit', element: <ProtectedRoute><DrugFormPage /></ProtectedRoute> }
  { path: 'share/:token',     element: <PublicReportPage /> }
  ```

### Task 14: Frontend components (per react-structure convention)

For each screen: UI component in `*.tsx`, business logic in `hooks/*.ts`. Build mocks-faithful with Syncfusion components:

- [ ] **Step 1: HomePage** — uses `useActiveEpisodes`. If active → ActiveBanner (pulse animation). Buttons: "🤒 มี Migraine Attack" → `/health/log`, "💊 กินยา" → drug picker modal.
- [ ] **Step 2: QuickLogAttackPage** — `useStartEpisode` + smart pre-fill from `useLastEpisodeAttributes(within: 7 days)`. Severity slider (custom, NOT Syncfusion since it needs gradient). Chips for attributes. Sticky save bar.
- [ ] **Step 3: ActiveEpisodePage** — `useEpisodeDetail` polled every 30s for live timer + drug progress bars. Big action buttons: ✅ Resolve / 💊 More Med / 📈 Worse.
- [ ] **Step 4: TakeMedicationPage** — `useTakeMedicationContext(episodeId)`. Renders 3 sections (active/takeable/blocked) per mock. Tap dose button → `useLogIntake.mutate` → toast → router.back().
- [ ] **Step 5: HistoryPage** — `useEpisodes({ filters })`. Syncfusion `MultiSelect` for filter chips, `TextBox` for search, group by date.
- [ ] **Step 6: EpisodeDetailPage** — full timeline view + edit/delete actions. Edit opens QuickLogAttackPage in edit mode.
- [ ] **Step 7: DrugMasterPage** — list with photo thumbnails + Syncfusion `AutoComplete` search. Phase 1 = no Gemini button (Phase 2 adds it).
- [ ] **Step 8: DrugFormPage** — react-hook-form with all Drug fields. PhotoUploader for multi-photo. Syncfusion `MultiSelect` for treats.
- [ ] **Step 9: SettingsPage** — push permission toggle (calls `useWebPushSubscription.subscribe()`), revoke share links list, light/dark mode.
- [ ] **Step 10: PublicReportPage** — fetches `/api/public/report?t=...`, renders `DoctorReport/*` components. No MSAL.

### Task 15: Service Worker + Web push client

- [ ] **Step 1:** `frontend/public/sw.js`:
  ```js
  self.addEventListener('push', e => {
      const data = e.data.json();
      e.waitUntil(self.registration.showNotification(data.title, {
          body: data.body,
          actions: data.actions,
          data: data.data,
          icon: '/icons/icon-192.png'
      }));
  });
  self.addEventListener('notificationclick', e => {
      e.notification.close();
      const { pingId, episodeId } = e.notification.data;
      const action = e.action || 'open';
      if (action === 'resolved' || action === 'same') {
          // 0-tap response — POST directly
          e.waitUntil(fetch(`/api/followups/${pingId}/respond`, {
              method: 'POST', body: JSON.stringify({ response: action })
          }));
      } else {
          // open app to modal
          e.waitUntil(clients.openWindow(`/health/active/${episodeId}?ping=${pingId}&action=${action}`));
      }
  });
  ```
- [ ] **Step 2:** `frontend/public/manifest.json` — PWA manifest with name, icons, start_url.
- [ ] **Step 3:** Register SW in `main.tsx`:
  ```ts
  if ('serviceWorker' in navigator) {
      navigator.serviceWorker.register('/sw.js');
  }
  ```
- [ ] **Step 4:** `useWebPushSubscription.ts`:
  ```ts
  async function subscribe() {
      const reg = await navigator.serviceWorker.ready;
      const sub = await reg.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: VAPID_PUBLIC_KEY
      });
      await api.subscribeWebPush(sub.toJSON());
  }
  ```
- [ ] **Step 5:** Prompt permission at right time: NOT on first launch. Trigger on first `LogIntake` action with a friendly modal.

### Task 16: Doctor Report frontend components

Mirror `doctor-report-mock.html` structure as React components consuming JSON from `GetDoctorReportQuery`:

- [ ] **Step 1: ClinicalFlags** — MOH risk banner, frequency, disability days
- [ ] **Step 2: MigraineProfile** — metric rows with thresholds
- [ ] **Step 3: TriggerCorrelation** — horizontal bars per trigger
- [ ] **Step 4: TreatmentEfficacy** — table with relief % + avg onset
- [ ] **Step 5: PatternAnalysis** — onset time, day of week, menstrual correlation
- [ ] **Step 6: DayCard** — episode wrappers with attributes, timeline, episode-close banner

Use inline SVG for charts (no chart library — matches mock approach).

### Task 17: Search (Syncfusion AutoComplete integration)

- [ ] **Step 1:** `SearchAutoComplete.tsx` — wraps `@syncfusion/react-dropdowns AutoComplete` with `allowCustom` and `filterType=Contains`.
- [ ] **Step 2:** Use in `QuickLogAttackPage` (symptom picker), `DrugFormPage` (treats[] picker), `TakeMedicationPage` (drug search).
- [ ] **Step 3:** Optional global search route `/health/search` with grouped results (drugs / episodes / triggers).

### Task 18: Deployment

- [ ] **Step 1: Re-enable Pay-As-You-Go subscription** in Azure portal.
- [ ] **Step 2: Set parameters**: edit `infra/main.bicepparam` with real `sqlAdminObjectId`.
- [ ] **Step 3: Preview** — `az deployment group what-if --resource-group MenuNest --template-file infra/main.bicep --parameters infra/main.bicepparam`. Inspect output carefully for App Service app-settings replacement.
- [ ] **Step 4: App Settings snapshot** — `az webapp config appsettings list --name menunest --resource-group MenuNest > /tmp/old-app-settings.json` so manual settings can be re-applied if Bicep wipes them.
- [ ] **Step 5: Deploy** — `az deployment group create ...`.
- [ ] **Step 6: Post-deploy**:
  - Grant UAMI access to SQL via T-SQL: `CREATE USER [menunest-id-a4ef] FROM EXTERNAL PROVIDER; ALTER ROLE db_owner ADD MEMBER [menunest-id-a4ef];`
  - Set real secrets via `az webapp config appsettings set`: `VapidPublicKey`, `VapidPrivateKey`, `QrTokenSigningKey`, `Google__ClientSecret`.
  - Restart App Service.
- [ ] **Step 7: Run EF migration against Azure SQL** — `dotnet ef database update --connection "Server=...,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"`
- [ ] **Step 8: Smoke test** — login → add drug → log attack → wait 30 min → expect push → tap → episode resolves.

### Task 19: E2E Playwright smoke test

- [ ] **Step 1: Add Playwright** to frontend devDependencies.
- [ ] **Step 2: Test the critical flow**:
  ```ts
  test('full attack flow', async ({ page, context }) => {
      await login(page);
      await page.goto('/health');
      await page.click('text=มี Migraine Attack');
      await page.locator('input[type=range]').fill('7');
      await page.click('text=บันทึก');
      await expect(page).toHaveURL(/\/health\/active/);
      await page.click('text=กินยาเพิ่ม');
      await page.click('text=กิน 1 เม็ด');  // first takeable drug
      await expect(page).toHaveURL(/\/health\/active/);
      // simulate follow-up: directly call RecordPingResponseHandler endpoint
      // verify episode shows resolved badge
  });
  ```
- [ ] **Step 3: CI integration** in `.github/workflows/ci.yml`.

---

## Phase 2 — Gemini AI-assisted Drug creation

Adds AI shortcut on top of the manual Phase 1 form. **No core feature change.**

### Phase 2 Task A: Backend Gemini integration

- [ ] **Step 1:** Add `Google_GenerativeAI` NuGet (or call REST directly with `HttpClient`).
- [ ] **Step 2:** Implement `IGeminiClient.AnalyzeDrugPhotosAsync(imageBlobUrls[], tools[])` returning `GeminiAnalysisResult { proposedDrug, confidenceMap, toolCallLog }`.
- [ ] **Step 3:** Define tools schema:
  - `search_existing_drugs(query)` → mediated to `ListDrugsQuery`
  - `propose_drug_creation(...)` → does NOT persist; returns structured data + confidence per field
  - `report_unable_to_identify(reason)` → returns to user "AI couldn't read photos"
- [ ] **Step 4:** `AnalyzeDrugFromPhotosHandler` — accepts `photoBlobUrls[]`, calls Gemini with tool definitions, returns `DraftDrugDto` with confidence_map.
- [ ] **Step 5:** Endpoint `POST /api/drugs/analyze-photos`.

### Phase 2 Task B: Frontend AI flow

- [ ] **Step 1:** `DrugMasterPage` adds prominent CTA "📷 ถ่ายซองยา (AI)" alongside manual "+ Add" button.
- [ ] **Step 2:** New `GeminiDrugCapturePage` — multi-photo uploader (1-5) + uploads to Azure Blob → POST to `/api/drugs/analyze-photos` → shows Gemini loading state with tool call log → renders `GeminiConfirmDrugForm` with draft + confidence badges per field.
- [ ] **Step 3:** User confirms/edits → calls existing `CreateDrugHandler` from Phase 1 (no new save endpoint needed).
- [ ] **Step 4:** Manual fallback link "✏️ ใส่ข้อมูลเองแทน".

### Phase 2 Task C: Confidence display + telemetry

- [ ] **Step 1:** Persist Gemini confidence values in Drug record (`ai_confidence_map jsonb`) for future learning.
- [ ] **Step 2:** Add App Insights custom event for each AI extraction (success rate per field).

---

## Phase 3 — Future (out of scope here)

- Multi-user / family member tracking (uses existing Family entity)
- Azure Speech voice input for symptom log and note
- Drug interaction warnings (allergy DB + Gemini cross-check)
- Native iOS/Android app (PWA already works on Android; iOS needs separate push strategy)
- Pattern-based predictive alerts ("based on past 3 weeks you usually get a migraine on Wednesdays — take prophylactic now")

---

## Open Questions — Resolved Decisions

| Q | Decision |
|---|---|
| Photo storage: separate entity vs URL columns? | **Separate `Photo` entity** with `parent_type` + `parent_id` enum — allows reuse + audit + soft delete |
| Time zone | Store all `DateTime` as **UTC** in DB. Frontend converts to user's local on display. `IClock` abstraction injected. |
| Service Worker permission prompt timing | After first `LogIntake`, with friendly modal "อยากให้เราถามหลังกินยาว่าหายมั้ย?" |
| SAS expiry | Upload SAS: **15 min**. Read SAS for own data: **1 hour**. Read SAS for share token reads: **24 hours** (refresh on each report request) |
| Share token revocation | Check `revoked_at IS NULL` on every report request. Doctor sees blank page + "link revoked" if active. |
| Entra app registration | Existing API scope works; add `Health.ReadWrite` scope only if separating from rest of app (not needed for MVP) |
| VAPID keypair | Generate once via `npx web-push generate-vapid-keys`, set in App Service config (public + private as separate keys) |
| EF migration strategy | **Incremental** — one migration for `HealthInitial`, separate for `HealthSeed`, future phases get their own |
| Doctor report rendering | **SPA route** (React) — same code path as rest of app, easier maintenance, can cache |

---

## Risks + Mitigations

| Risk | Mitigation |
|---|---|
| App Service `appSettings` replaced by Bicep deploy → manual secrets wiped | Capture snapshot pre-deploy (Task 18 Step 4); after deploy re-apply via `az webapp config appsettings set` |
| Web push not supported on iOS Safari (without PWA install) | Document as limitation; instruct iOS users to "Add to Home Screen" first; fallback to in-app modal on next visit |
| User doesn't grant push permission | In-app banner reminds; episode page polls every 30s for pending pings → modal pops up |
| Gemini misidentifies drug (Phase 2) | Confirmation screen mandatory; confidence badges per field; manual fallback button |
| Azure SQL Basic 5 DTU limit | Monitor with App Insights queries; have S0 upgrade path one click; index hot queries (Episode by user+started_at, Ping by status+scheduled_at) |
| BackgroundService crashes silently | Application Insights + alert on `FollowUpDispatcher` error rate; restart App Service if hangs |
| Photo upload bypass attempts (SAS leak) | Container-scoped SAS only; user-delegation SAS (UAMI-derived); 15-min expiry; HTTPS-only; user can only request SAS for their own user_id path prefix |
| Subscription stays disabled | Document re-enable path in `infra/README.md`; have backup migration to Cartagena work sub if needed |
| Doctor report URL guessable | HMAC token 256-bit; check revocation; rate-limit unauthenticated `/api/public/report` endpoint |

---

## Test Strategy

### Unit tests (xUnit + Moq + InMemory DbContext)

| Handler | Critical scenarios |
|---|---|
| `StartEpisodeHandler` | Severity bounds (1-10); migraine attributes optional; auto-detect `is_on_period` from user period state |
| `GetTakeMedicationContextHandler` | **All 3 categories logic** — most thorough tests in suite |
| `LogIntakeHandler` | Schedules follow-up ping; links to episode |
| `RecordPingResponseHandler` | Resolved → closes episode; Improved → reschedules; max 3 re-pings |
| `RetroCloseEpisodeHandler` | Sets `retro_closed=true`; cancels pending pings; estimates `ended_at` |
| `GetDoctorReportHandler` | MOH risk calc (>10 acute med days); trigger correlation %; treatment efficacy |
| `CreateShareLinkHandler` | Token signed correctly; expiry set; raw token NOT in DB (hash only) |
| `HmacShareTokenService` | Round-trip sign + verify; rejects expired; rejects modified |

### Integration tests (Infrastructure)

| Test | Purpose |
|---|---|
| `FollowUpDispatcherTests` | Schedule 3 pings → tick → assert 3 push attempts |
| `BlobSasGeneratorTests` | Generate SAS → upload via HTTP → read via SAS — Azurite emulator |
| `MigrationTests` | Apply all migrations to fresh InMemory then real SQL → no errors |

### E2E (Playwright)

| Flow | Steps |
|---|---|
| Critical attack flow | Login → log attack → take med → wait or trigger ping → resolve → check History |
| Doctor share | Generate share → open URL in fresh context → verify report renders |
| Photo upload | Drug form → pick image → uploads → blob URL persisted |

---

## Deployment Checklist (per Task 18)

```
0. ✅ Sub re-enabled
1. ✅ Bicep params filled (sqlAdminObjectId)
2. ✅ what-if reviewed
3. ✅ App Settings snapshot saved
4. ✅ Bicep applied
5. ✅ T-SQL: UAMI grant on SQL DB
6. ✅ App Settings real values set (VAPID, QR HMAC, Google secret)
7. ✅ App Service restart
8. ✅ EF migrations applied
9. ✅ Smoke test passes (login → attack → push)
10. ✅ Static Web App rebuilt with new env vars (VAPID_PUBLIC_KEY)
```

---

## Estimated Effort

| Group | Effort |
|---|---|
| Task 1-4 (Domain + EF + DTOs) | 1.5 days |
| Task 5-8 (CRUD + Episode + Intake + 3-category logic) | 2.5 days |
| Task 9-10 (Follow-up + Web Push) | 1.5 days |
| Task 11-12 (Photo SAS + Share + Doctor report backend) | 1.5 days |
| Task 13-17 (Frontend 7 screens + components + Service Worker + Search) | 3.5 days |
| Task 18-19 (Deployment + E2E test) | 1 day |
| **Phase 1 total** | **~11.5 days** |
| Phase 2 (Gemini) | 2-3 days |

---

## Done = Phase 1 Acceptance Criteria

- [ ] User adds drug manually via form with photos (multi-upload works, all attached on save)
- [ ] User logs symptom attack with severity in ≤3 taps from Home (verified via Playwright)
- [ ] User logs intake linked to active episode → episode shows drug in "active drugs" list
- [ ] Web push fires within 1-2 min of scheduled time (verified by checking App Insights logs)
- [ ] Push action buttons (resolved/improved/same/worse) update episode correctly
- [ ] User missed 3 pings → retro-close modal pops up on next app open
- [ ] Tap "✅ หายแล้ว" → episode `ended_at` set, no further pings
- [ ] User generates QR share link → doctor scans → sees timeline report
- [ ] Doctor report renders all sections (clinical flags, profile, frequency chart, severity dots, aura/symptoms, location/quality, triggers, treatment efficacy, patterns, daily timeline)
- [ ] All `/api/*` routes auth-protected via MSAL except `/api/public/report` (HMAC-gated)
- [ ] Photos upload directly to Azure Blob (browser dev tools confirms PUT to *.blob.core.windows.net)
- [ ] App Insights shows error rate < 1% and BackgroundService heartbeat present

---

**End of Plan.** Implement task-by-task with `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
