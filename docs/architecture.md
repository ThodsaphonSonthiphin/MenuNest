# MenuNest — Architecture & Flows

End-to-end view of MenuNest's two products under one repo:

- **Meal Planning** — recipes, stock, meal plan, shopping list, AI assistant, budget (family-scoped, multi-user).
- **Health** — migraine / symptom tracker with medication intake, follow-up push notifications, doctor-report share links, drug photo uploads (user-scoped, single-user).

Each section below is a **GitHub-flavored Mermaid sequence diagram** of one real flow, traced against the actual handlers in `backend/src/MenuNest.Application/UseCases/**` and the matching React pages in `frontend/src/pages/**`. References point at the canonical handler/component so you can read the diagram next to the code.

> Mermaid renders natively in GitHub markdown — no plugins needed.

---

## Table of contents

1. [System context](#1-system-context)
2. [Authentication & user provisioning](#2-authentication--user-provisioning) — MSAL / Google → JWT → `UserProvisioner`
3. [Family — join via invite code](#3-family--join-via-invite-code)
4. [Recipe — create with photo upload](#4-recipe--create-with-photo-upload) (Blob SAS)
5. [Meal plan — cook batch & stock deduction](#5-meal-plan--cook-batch--stock-deduction)
6. [Shopping list — mark bought → auto-restock](#6-shopping-list--mark-bought--auto-restock)
7. [AI assistant — Gemini tool-loop with confirm-before-write](#7-ai-assistant--gemini-tool-loop-with-confirm-before-write)
8. [Health — quick-log attack (start episode)](#8-health--quick-log-attack-start-episode)
9. [Health — take medication & schedule follow-up](#9-health--take-medication--schedule-follow-up)
10. [Health — follow-up push (0-tap response)](#10-health--follow-up-push-0-tap-response)
11. [Health — drug photo upload (SAS, multi-photo)](#11-health--drug-photo-upload-sas-multi-photo)
12. [Health — doctor report share link (QR + HMAC)](#12-health--doctor-report-share-link-qr--hmac)

---

## 1. System context

The app is two SPAs against one ASP.NET Core API, with Azure-managed services for data and identity. The frontend is delivered by Static Web Apps; the API runs on App Service Linux.

```mermaid
flowchart LR
    User["👤 User<br/>(browser / PWA)"]
    Doctor["🩺 Doctor<br/>(scans QR)"]

    subgraph Azure[" "]
      direction LR
      SWA["Static Web App<br/>(React + Vite)"]
      API["App Service Linux<br/>(.NET 10 Web API)"]
      SQL[("Azure SQL<br/>Basic")]
      Blob[("Blob Storage<br/>drug-images<br/>episode-images")]
      AI["Gemini API<br/>(external)"]
      Push["Web Push<br/>(VAPID)"]
      Entra["Microsoft Entra ID<br/>+ Google OAuth"]
    end

    User -->|MSAL / GIS| Entra
    User -->|HTTPS| SWA
    SWA -->|/api Bearer JWT| API
    Doctor -->|/share/&lt;token&gt;| SWA
    SWA -->|/api/public/report?t=| API
    API -->|EF Core| SQL
    API -->|User-delegation SAS| Blob
    User -->|PUT blob with SAS| Blob
    API -->|Function-calling| AI
    API -->|VAPID encrypted| Push
    Push -->|push event| User
```

**Trust boundaries**

| Boundary | Who authenticates | How |
|---|---|---|
| SPA → API | The signed-in user | `Authorization: Bearer <JWT>` validated by `Microsoft.Identity.Web` |
| Doctor → API | Nobody (anonymous) | Only `/api/public/report?t=<token>` — HMAC-signed token + DB hash lookup |
| Browser → Blob | The signed-in user, **once** | Short-lived (15 min) user-delegation SAS scoped to one blob path |
| API → Gemini | The API service | Server-side API key (`Gemini__ApiKey` config) |

---

## 2. Authentication & user provisioning

Users sign in with **either** Microsoft (MSAL.js, Entra ID multi-tenant + personal accounts) or **Google** (GIS). Both produce a JWT that the API validates. On the **first** authenticated request, [`UserProvisioner.GetOrProvisionCurrentAsync`](../backend/src/MenuNest.Infrastructure/Authentication/UserProvisioner.cs) lazily creates the `User` row from the token's claims. There is no separate "register" call.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as React SPA
    participant IDP as Entra ID / Google
    participant API as ASP.NET API
    participant Prov as UserProvisioner
    participant DB as Azure SQL

    U->>SPA: Click "Sign in with Microsoft"
    SPA->>IDP: loginPopup() / GIS prompt
    IDP-->>SPA: id_token + access_token (JWT)
    SPA->>SPA: Cache token (msal-browser / sessionStorage)

    Note over SPA,API: Subsequent requests
    SPA->>API: GET /api/me<br/>Authorization: Bearer [JWT]
    API->>API: Microsoft.Identity.Web<br/>validates signature, aud, iss, exp
    API->>Prov: GetOrProvisionCurrentAsync()
    Prov->>DB: SELECT * FROM Users<br/>WHERE ExternalId = oid

    alt First time signing in
      DB-->>Prov: ∅ (no row)
      Prov->>Prov: User.CreateFromExternalLogin(oid, email, name, provider)
      Prov->>DB: INSERT Users (ExternalId, Email, DisplayName, Provider)
      DB-->>Prov: User row
    else Returning user
      DB-->>Prov: User row
    end

    Prov-->>API: User { Id, FamilyId? }
    API-->>SPA: 200 { id, email, displayName, familyId }
    SPA->>SPA: Cache /me in RTK Query
    SPA->>U: Render /health (default landing)
```

**Key decisions**

- `ExternalId` = the IdP's stable subject (Entra `oid` claim or Google `sub`). It's `UNIQUE` and the only join key.
- `FamilyId` is nullable. `RequireFamilyAsync` throws `DomainException` → HTTP 400 if the caller hits a family-only endpoint without joining a family. Health endpoints use `GetOrProvisionCurrentAsync` instead (no family required).
- The SPA's `ProtectedRoute` gate ([`frontend/src/shared/components/ProtectedRoute.tsx`](../frontend/src/shared/components/ProtectedRoute.tsx)) waits for MSAL's `inProgress === None` before deciding — otherwise the first render flashes false and bounces signed-in users to `/login`.

---

## 3. Family — join via invite code

A `Family` has a 6-character invite code. Anyone with the code can join via the `/join-family` page until the owner rotates it. One user belongs to at most one family.

```mermaid
sequenceDiagram
    autonumber
    actor U as User (no family)
    participant SPA as React SPA
    participant API as POST /api/families/join
    participant H as JoinFamilyHandler
    participant DB as Azure SQL

    U->>SPA: Enter invite code "ABC123"
    SPA->>API: POST /api/families/join { inviteCode: "ABC123" }
    API->>H: JoinFamilyCommand
    H->>H: validator.ValidateAndThrowAsync<br/>(format, length)
    H->>H: UserProvisioner.GetOrProvisionCurrentAsync
    alt User already in a family
      H-->>API: DomainException<br/>"You already belong to a family"
      API-->>SPA: 400 + message
      SPA-->>U: Toast: "Leave it first…"
    else User has no family
      H->>DB: SELECT * FROM Families<br/>WHERE InviteCode = 'ABC123'
      alt Code not found
        DB-->>H: ∅
        H-->>API: DomainException<br/>"Invite code is invalid or expired"
        API-->>SPA: 400
      else Found
        DB-->>H: Family { Id, Members }
        H->>H: user.JoinFamily(family.Id)
        H->>DB: UPDATE Users SET FamilyId = @id<br/>WHERE Id = @user
        DB-->>H: ok
        H-->>API: FamilyDto { Id, Name, InviteCode, MemberCount }
        API-->>SPA: 200
        SPA->>SPA: invalidateTags(["Me", "Family"])
        SPA-->>U: Navigate to /dashboard
      end
    end
```

Source: [`JoinFamilyHandler`](../backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyHandler.cs), [`InviteCode`](../backend/src/MenuNest.Domain/ValueObjects/InviteCode.cs) value object.

---

## 4. Recipe — create with photo upload

Photos go **direct browser → Blob** using a short-lived user-delegation SAS. The API never proxies the bytes — it only mints the SAS and stores the resulting blob path on the `Recipe` row. Same shape as the [Health drug-photo flow](#11-health--drug-photo-upload-sas-multi-photo) below; the recipe flow is single-photo.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as React SPA
    participant API as ASP.NET API
    participant Sas as AzureBlobSasGenerator
    participant Blob as Azure Blob<br/>(recipe-images)
    participant DB as Azure SQL

    U->>SPA: Fill recipe form, pick photo
    SPA->>API: POST /api/recipes/photo-sas<br/>{ contentType: "image/jpeg" }
    API->>Sas: GenerateUploadSasAsync(...)
    Sas->>Blob: GetUserDelegationKey(15 min)
    Blob-->>Sas: delegation key
    Sas-->>API: { uploadUrl, blobUrl }
    API-->>SPA: 200 { uploadUrl, blobUrl, expiresAt }

    SPA->>Blob: PUT [uploadUrl]<br/>x-ms-blob-type: BlockBlob<br/>Content-Type: image/jpeg<br/>(JPEG bytes)
    Blob-->>SPA: 201 Created

    SPA->>API: POST /api/recipes<br/>{ name, ingredients, imageBlobPath }
    API->>API: CreateRecipeHandler<br/>RequireFamilyAsync
    API->>DB: INSERT Recipes (FamilyId, ImageBlobPath, …)<br/>INSERT RecipeIngredients
    DB-->>API: Recipe.Id
    API-->>SPA: 201 RecipeDto
    SPA-->>U: Navigate to /recipes/:id
```

The browser is the **only** path the photo bytes ever travel. `Storage.allowSharedKeyAccess` is `false` on the storage account — user-delegation SAS (an Entra-issued key, not the account key) is the only mint mechanism, so a compromised app config can't forge a SAS without a valid Entra principal.

---

## 5. Meal plan — cook batch & stock deduction

"Cook" turns one or more `Planned` meal plan entries into `Cooked` and **deducts ingredients from family stock**. If stock is short, deduction clamps at zero, the missing ingredient is recorded in `CookNotes`, and the user gets a warning instead of an error — the meal still cooks.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as MealPlanPage
    participant API as POST /api/mealplan/cook
    participant H as CookBatchHandler
    participant DB as Azure SQL

    U->>SPA: Select entries → click "Cook now"
    SPA->>API: { entryIds: [g1, g2, g3] }
    API->>H: CookBatchCommand
    H->>H: validator + RequireFamilyAsync<br/>(must have a family)

    H->>DB: SELECT MealPlanEntries WHERE Id IN (..) AND FamilyId = @f
    DB-->>H: entries
    alt Any entry not Planned (already cooked/skipped)
      H-->>API: DomainException "Only planned entries…"
      API-->>SPA: 400
    end

    H->>DB: SELECT Recipes (Include Ingredients)<br/>WHERE Id IN distinct recipeIds
    DB-->>H: recipes
    H->>H: Aggregate required[ingredientId] = sum across entries × ingredient.Quantity

    H->>DB: SELECT Ingredients WHERE Id IN required.Keys
    H->>DB: SELECT StockItems WHERE IngredientId IN required.Keys

    loop for each required ingredient
      H->>H: applied = stock?.ApplyDelta(-needed, user)<br/>(clamped at 0, returns delta actually applied)
      alt applied > 0
        H->>H: deducted += { ingredient, qty }
        H->>DB: INSERT StockTransactions<br/>(source = Cook, delta = -qty)
      end
      alt missing = needed - applied > 0
        H->>H: partial += shortfall
        H->>H: notes += "ขาด X 2 ฟอง"
      end
    end

    loop for each entry
      H->>H: entry.MarkCooked(user, cookNotes)
    end
    H->>DB: SaveChangesAsync (one transaction)
    DB-->>H: ok

    H-->>API: { deducted[], partial[], cookedEntryIds[] }
    API-->>SPA: 200
    SPA->>SPA: invalidateTags(["Stock", "MealPlan"])
    SPA-->>U: Toast: "Cooked 3 meals" (+ shortfall list)
```

Source: [`CookBatchHandler`](../backend/src/MenuNest.Application/UseCases/MealPlan/CookBatch/CookBatchHandler.cs). The `StockTransaction` audit row uses the **first** entry's id as `SourceRefId` for a batch cook — there's no canonical single source row, but the audit trail still ties back to a real meal plan entry.

---

## 6. Shopping list — mark bought → auto-restock

Marking a shopping list item as **bought** auto-increments the family stock by that item's quantity and records a `StockTransaction` with `Source = ShoppingListBought`. Stock is created lazily — buying an ingredient the family has never tracked just creates a fresh `StockItem` row.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as ShoppingListDetailPage
    participant API as POST /api/shopping-lists/{listId}/items/{itemId}/buy
    participant H as BuyShoppingListItemHandler
    participant DB as Azure SQL

    U->>SPA: Tap checkbox on item
    SPA->>API: POST /buy
    API->>H: BuyShoppingListItemCommand

    H->>H: RequireFamilyAsync
    H->>DB: SELECT ShoppingLists (Include Items)<br/>WHERE Id = @list AND FamilyId = @f
    DB-->>H: list + items
    H->>H: item.MarkBought(user)
    alt Already bought
      H-->>API: DomainException
      API-->>SPA: 400
    end

    H->>DB: SELECT StockItems<br/>WHERE FamilyId = @f AND IngredientId = item.IngredientId
    alt Stock row exists
      DB-->>H: stockItem
      H->>H: stockItem.SetQuantity(qty + item.Quantity, user)
    else No stock yet
      H->>H: StockItem.Create(family, ingredient, item.Quantity, user)
      H->>DB: INSERT StockItems
    end

    H->>DB: INSERT StockTransactions<br/>(Delta = +item.Quantity,<br/>Source = ShoppingListBought,<br/>SourceRefId = item.Id)
    H->>DB: SaveChangesAsync
    DB-->>H: ok

    H-->>API: ShoppingListItemDto
    API-->>SPA: 200
    SPA->>SPA: invalidateTags(["ShoppingList","Stock"])
    SPA-->>U: Item ticked, stock badge increments
```

Source: [`BuyShoppingListItemHandler`](../backend/src/MenuNest.Application/UseCases/ShoppingList/BuyShoppingListItem/BuyShoppingListItemHandler.cs). The mirror operation `unbuy` reverses both effects.

---

## 7. AI assistant — Gemini tool-loop with confirm-before-write

The assistant uses **function calling** against Gemini. Read tools (`SearchRecipes`, `CheckStock`, `GetMealPlan`, `GetShoppingLists`, `GetFamilyInfo`) execute immediately. **Write tools** (`CreateRecipe`, `AddToMealPlan`, `CreateShoppingList`, `AddShoppingItems`) are **deferred** — the assistant proposes them, persists the tool-call JSON on its message, and waits for the user to type a Thai/English confirmation phrase (`ตกลง`, `confirm`, `ok`, …) on the next turn before executing.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as AiAssistantPage
    participant API as POST /api/chat/{convId}/messages
    participant H as SendMessageHandler
    participant DB as Azure SQL
    participant Gem as Gemini API

    U->>SPA: "หาเมนูสำหรับคืนนี้หน่อย"
    SPA->>API: { content: "หาเมนู…" }
    API->>H: SendMessageCommand
    H->>H: RequireFamilyAsync
    H->>DB: Load conversation + history (ChatMessages)
    DB-->>H: history

    H->>H: Is this a confirmation of pending writes?<br/>(last assistant.ToolCalls != null<br/>AND content matches confirm phrase)
    alt Not a confirmation
      H->>DB: INSERT ChatMessages (user)
      H->>Gem: ChatAsync(history + userMessage, tools=[Search,CheckStock,…])
      loop Gemini tool loop (server-side)
        Gem-->>H: FunctionCall: SearchRecipes(query="ไก่ผัด…")
        H->>H: tool.ExecuteAsync(args, familyId, userId)
        H->>DB: SELECT Recipes WHERE FamilyId AND Name LIKE …
        DB-->>H: results
        H->>Gem: FunctionResponse(results)
      end
      Gem-->>H: assistant text (+ pendingToolCalls if it proposes a write)
      H->>DB: INSERT ChatMessages (assistant,<br/>ToolCalls = pendingJson)
      H->>H: conversation.UpdateTitle() or Touch()
      H->>DB: SaveChangesAsync
      H-->>API: SendMessageResponseDto
      API-->>SPA: 200
      SPA-->>U: "พบ 3 เมนู … อยากเพิ่ม X เข้า meal plan วันพรุ่งนี้ไหม?"
    else Confirmation arrives
      Note over U,SPA: Next turn: U sends "ตกลง"
      H->>H: IsConfirmation("ตกลง") → true
      H->>Gem: ExecutePendingActionsAsync(history, pendingToolCallsJson)
      loop For each pending tool call
        H->>H: tool.ExecuteAsync (= the write — INSERT MealPlanEntries, etc.)
        H->>DB: persist
      end
      Gem-->>H: confirmation text
      H->>DB: INSERT ChatMessages (assistant final)
      H-->>API: response
      API-->>SPA: 200
      SPA-->>U: "เพิ่มแล้วค่ะ ✓"
    end
```

Source: [`SendMessageHandler`](../backend/src/MenuNest.Application/UseCases/Chat/SendMessage/SendMessageHandler.cs), [`GeminiChatService`](../backend/src/MenuNest.Infrastructure/AI/GeminiChatService.cs), tools in [`backend/src/MenuNest.Infrastructure/AI/Tools/`](../backend/src/MenuNest.Infrastructure/AI/Tools/).

`AutoCallFunction = false` is set on the Gemini model — the **API** drives the tool loop, not the SDK. This lets us audit each tool invocation, return Thai-localized errors, and inject the confirm-gate.

---

## 8. Health — quick-log attack (start episode)

`/health` is the default landing page. Tapping a symptom + severity creates a `SymptomEpisode`, optionally with migraine-specific attributes (aura, location, quality, associated symptoms, functional impact, triggers).

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as QuickLogAttackPage
    participant API as POST /api/episodes
    participant H as StartEpisodeHandler
    participant DB as Azure SQL

    U->>SPA: Pick symptom + severity (+ optional triggers, aura …)
    SPA->>API: StartEpisodeCommand { symptomId, severity, hasAura, location, … }
    API->>H: command
    H->>H: validator.ValidateAndThrowAsync<br/>(severity 1–10, symptomId not empty …)
    H->>H: GetOrProvisionCurrentAsync (no family required)

    H->>H: SymptomEpisode.Start(user, symptom, severity,<br/>isOnPeriod, startedAt, triggerIds, notes)
    alt Any migraine attribute provided
      H->>H: episode.SetMigraineAttributes(hasAura, auraTypes, location, …)
    end
    H->>DB: INSERT SymptomEpisodes
    H->>DB: SELECT Symptoms.Name WHERE Id = symptomId (for DTO)
    DB-->>H: name
    H->>DB: SaveChangesAsync

    H-->>API: EpisodeDto { id, symptomName, startedAt, severity, … }
    API-->>SPA: 201
    SPA->>SPA: invalidateTags(["Episode","ActiveEpisodes"])
    SPA-->>U: Navigate to /health/active/:id
```

Source: [`StartEpisodeHandler`](../backend/src/MenuNest.Application/UseCases/Health/Episodes/StartEpisode/StartEpisodeHandler.cs). The domain method `SetMigraineAttributes` overwrites the whole migraine block — the handler skips the call if no attribute was provided so future-default values aren't clobbered with nulls.

---

## 9. Health — take medication & schedule follow-up

On the active episode page, the user picks from three drug buckets returned by `/take-medication-context`:

- **Active in effect** — drug already taken for this episode and still within its `OnsetMinutes + DurationMinutes` window. Disabled to prevent double-dose.
- **Takeable** — under daily-dose cap and no overlap.
- **Blocked** — daily-dose cap reached.

`LogIntake` writes the intake row **and** reschedules a fresh +30 min follow-up ping, cancelling any older pending ping for the same episode.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as TakeMedicationPage
    participant Ctx as GET /api/episodes/{id}/take-medication-context
    participant API as POST /api/intakes
    participant H as LogIntakeHandler
    participant DB as Azure SQL

    U->>SPA: Open active episode → "Take medication"
    SPA->>Ctx: load 3 drug buckets
    Ctx-->>SPA: { activeDrugs[], takeableDrugs[], blockedDrugs[] }
    SPA-->>U: Render bucketed list + ไม่กินยา (no-drug) fallback

    U->>SPA: Pick a drug + dose
    SPA->>API: LogIntakeCommand<br/>{ drugId, doseAmount, symptomEpisodeId, takenAt }
    API->>H: command

    H->>H: validator + GetOrProvisionCurrentAsync
    H->>DB: SELECT Drugs WHERE Id = @d<br/>AND UserId = @user AND DeletedAt IS NULL
    alt Drug not owned / deleted
      DB-->>H: ∅
      H-->>API: DomainException "Drug not found"
      API-->>SPA: 400
    end
    DB-->>H: drug

    alt symptomEpisodeId provided
      H->>DB: SELECT EXISTS SymptomEpisodes<br/>WHERE Id = @ep AND UserId = @user
      alt Not owned
        H-->>API: DomainException "Episode not found"
      end
    end

    H->>H: Intake.Create(user, drug, dose, episode, takenAt, notes)
    H->>DB: INSERT Intakes

    alt Linked to active episode
      H->>DB: SELECT FollowUpPings<br/>WHERE EpisodeId = @ep AND Status = Pending
      DB-->>H: pending pings
      loop for each pending
        H->>H: ping.MarkMissed()
      end
      H->>H: FollowUpPing.Schedule(episodeId, now + 30m)
      H->>DB: INSERT FollowUpPings (Status = Pending)
    end

    H->>DB: SaveChangesAsync (one transaction)
    H-->>API: IntakeDto
    API-->>SPA: 201
    SPA-->>U: Navigate back to /health/active/:id
```

Source: [`LogIntakeHandler`](../backend/src/MenuNest.Application/UseCases/Health/Intakes/LogIntake/LogIntakeHandler.cs). Only **one** pending ping exists per episode at any moment — old pings are marked `Missed` rather than `Cancelled` so the doctor report can still see "scheduled but missed" history.

---

## 10. Health — follow-up push (0-tap response)

A `BackgroundService` on the API polls pending pings every minute and sends a web push via the **WebPush** NuGet package + VAPID keys. The service worker on the user's device shows a notification with up to 4 action buttons. Two of them (**Resolved**, **Same**) POST back directly from the SW without opening the app — that's the headline UX feature: a one-tap response from the lock screen.

```mermaid
sequenceDiagram
    autonumber
    participant Disp as FollowUpDispatcher<br/>(BackgroundService, 1 min)
    participant Med as IMediator
    participant DB as Azure SQL
    participant Push as WebPushSender<br/>(VAPID)
    participant SW as Service Worker<br/>(sw.js)
    actor U as User
    participant API as POST /api/followups/{id}/respond
    participant Rec as RecordPingResponseHandler

    loop Every 1 min while running
      Disp->>Med: GetPendingPingsQuery(50)
      Med->>DB: SELECT FollowUpPings WHERE Status = Pending<br/>AND ScheduledAt <= now LIMIT 50
      DB-->>Disp: pendingDtos

      loop For each pending ping
        Disp->>DB: load ping aggregate
        Disp->>Push: SendFollowUpAsync(ping)
        Push->>DB: SELECT WebPushSubscriptions WHERE UserId = ping.UserId
        DB-->>Push: subscriptions
        loop for each subscription
          Push->>SW: HTTP POST (encrypted VAPID payload)
        end
        Disp->>Disp: ping.MarkAsked() (regardless of send result)
      end
      Disp->>DB: SaveChangesAsync
    end

    Note over SW,U: Push arrives
    SW->>SW: showNotification("ดีขึ้นไหม?",<br/>actions = [resolved, improved, same, worse])
    U->>SW: Tap "✓ หายแล้ว" (action = "resolved")

    alt 0-tap path: resolved / same
      SW->>API: POST /api/followups/{pingId}/respond<br/>{ response: "Resolved" }<br/>credentials: include
      API->>Rec: RecordPingResponseCommand
      Rec->>DB: UPDATE FollowUpPings SET Status=Answered, Response=Resolved
      alt Response = Resolved
        Rec->>DB: UPDATE SymptomEpisodes SET EndedAt = now, SeverityAfter = …
      end
      DB-->>Rec: ok
      Rec-->>API: ok
      API-->>SW: 200
      SW-->>U: silent ack notification "✓ บันทึกคำตอบแล้ว"
    else Needs more input: improved / worse / default click
      SW->>SW: openWindow("/health/active/{ep}?ping={id}&action=worse")
      U->>SW: app opens, episode page reads ?ping=&action=,<br/>prompts severity slider, then POSTs respond
    end
```

Source: [`FollowUpDispatcher`](../backend/src/MenuNest.Infrastructure/BackgroundServices/FollowUpDispatcher.cs), [`WebPushSender`](../backend/src/MenuNest.Infrastructure/Services/WebPushSender.cs), [`sw.js`](../frontend/public/sw.js).

**Fail-quiet design:** if VAPID keys aren't configured, `WebPushSender` logs a warning and returns 0 — the dispatcher keeps running. Pings still get `MarkAsked` so the in-app modal picks them up the next time the user opens the app.

---

## 11. Health — drug photo upload (SAS, multi-photo)

Drug packaging supports multiple photos. Same SAS pattern as recipe photos but loops over N files and the API verifies the user owns the parent `Drug` before minting each SAS — without that check, a caller could request a SAS scoped under another user's blob path prefix.

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant SPA as DrugFormPage
    participant Sas as POST /api/photos/upload-sas
    participant SasH as RequestUploadSasHandler
    participant Blob as Azure Blob<br/>(drug-images)
    participant Att as POST /api/drugs/{id}/photos
    participant DB as Azure SQL

    U->>SPA: Create/edit drug → pick N packaging photos
    SPA->>SPA: Create drug first → drugId

    loop For each photo
      SPA->>Sas: { containerKey: "drug", parentId: drugId, contentType }
      Sas->>SasH: RequestUploadSasCommand
      SasH->>SasH: validate inputs<br/>GetOrProvisionCurrentAsync
      SasH->>DB: SELECT EXISTS Drugs<br/>WHERE Id = @drug AND UserId = @user AND DeletedAt IS NULL
      alt Not owned
        SasH-->>Sas: DomainException "Drug not found"
        Sas-->>SPA: 400
      else Owned
        SasH->>Blob: GetUserDelegationKey(15 min) + build SAS<br/>scoped to one blob path
        SasH-->>Sas: { uploadUrl, blobUrl, expiresAt }
        Sas-->>SPA: 200

        SPA->>Blob: PUT [uploadUrl]<br/>x-ms-blob-type: BlockBlob<br/>(image bytes)
        Blob-->>SPA: 201
        SPA->>SPA: collect { blobUrl, contentType, fileSize }
      end
    end

    SPA->>Att: AttachPhotosToDrug<br/>{ photos: [{ blobUrl, contentType, fileSize }, …] }
    Att->>DB: validate drug ownership<br/>INSERT Photos (DrugId, BlobUrl, ContentType, FileSize)
    DB-->>Att: ok
    Att-->>SPA: 201 PhotoDto[]
    SPA-->>U: Drug card now shows packaging thumbnails
```

Source: [`RequestUploadSasHandler`](../backend/src/MenuNest.Application/UseCases/Health/Photos/RequestUploadSas/RequestUploadSasHandler.cs), [`AzureBlobSasGenerator`](../backend/src/MenuNest.Infrastructure/Services/AzureBlobSasGenerator.cs).

The attach step is separate from drug create on purpose — if the SAS upload fails after the drug is saved, the user can retry just the photos without re-entering the form. `Photo.Create` requires `fileSize > 0`, which is why a unit test failed if the handler tried to attach photos inline with zero-byte placeholders.

---

## 12. Health — doctor report share link (QR + HMAC)

The patient creates a date-bounded share link. The API returns the raw token **once** as a QR code; the DB stores only a SHA-256 hash. The doctor scans the QR, lands on `/share/<token>` (a public route, no auth), and the SPA calls `/api/public/report?t=<token>` to fetch the full report JSON.

```mermaid
sequenceDiagram
    autonumber
    actor U as Patient
    participant SPA as ShareLinksPage
    participant Cre as POST /api/share-links
    participant CreH as CreateShareLinkHandler
    participant Tok as HmacShareTokenService
    participant Url as ShareUrlBuilder
    participant DB as Azure SQL
    actor D as Doctor
    participant Pub as GET /api/public/report?t=[token]
    participant Rep as GetDoctorReportHandler

    U->>SPA: Pick date range + ValidForDays
    SPA->>Cre: { dateFrom, dateTo, validForDays }
    Cre->>CreH: CreateShareLinkCommand
    CreH->>CreH: validator + GetOrProvisionCurrentAsync
    CreH->>Tok: Issue(userId, dateFrom, dateTo, expiresAt)
    Note right of Tok: HMAC-SHA256 JWT signed<br/>with Share__TokenSigningKey
    Tok-->>CreH: { rawToken, hash (hex SHA-256) }
    CreH->>DB: INSERT ShareLinks<br/>(UserId, TokenHash, DateFrom, DateTo, ExpiresAt)<br/>— raw token is NOT stored
    DB-->>CreH: link

    CreH->>Url: BuildShareUrl(rawToken)
    Note right of Url: BaseUrl set? → "https://[host]/share/[t]"<br/>else → "/share/[t]"
    Url-->>CreH: shareUrl
    CreH-->>Cre: { Token, ShareUrl, ShareId, ExpiresAt }
    Cre-->>SPA: 200
    SPA-->>U: Render Syncfusion QR code of shareUrl<br/>(the only chance to see the token —<br/>cannot be recovered if closed)

    U->>D: Show QR / send link

    D->>SPA: Scan QR → opens [host]/share/[token]
    SPA->>Pub: GET /api/public/report?t=[token]<br/>(NO Authorization header)
    Pub->>Rep: GetDoctorReportQuery
    Rep->>Tok: Verify(token)
    Note right of Tok: validate signature + exp +<br/>issuer + audience claims
    alt Invalid / expired token
      Tok-->>Rep: throws (SecurityTokenException etc.)
      Rep->>Rep: catch (not DomainException) → rethrow as<br/>DomainException("Share link is invalid or expired")
      Rep-->>Pub: DomainException
      Pub-->>SPA: 400 + message
      SPA-->>D: "ลิงก์หมดอายุ"
    else Token verifies
      Tok-->>Rep: ShareTokenClaims { userId, dateFrom, dateTo }
      Rep->>Tok: Hash(token) → hex
      Rep->>DB: SELECT ShareLinks WHERE TokenHash = hash
      alt Revoked / not found
        Rep-->>Pub: DomainException
        Pub-->>SPA: 400
      end
      Rep->>Rep: link.IsValidAt(now)? (DB-side revocation check)
      Rep->>Rep: link.RecordAccess() (for "X opens" indicator)

      Rep->>DB: load User
      Rep->>DB: SELECT SymptomEpisodes WHERE UserId AND StartedAt IN [from, to+1d)
      Rep->>DB: SELECT Intakes WHERE EpisodeId IN (...)
      Rep->>DB: SELECT FollowUpPings WHERE EpisodeId IN (...)
      Rep->>DB: SELECT Symptoms / Triggers / Drugs name lookups
      Rep->>DB: SaveChangesAsync (persist RecordAccess)

      Rep->>Rep: BuildSummary / ClinicalFlags<br/>(MOH risk 10/30, near-chronic 8/30, MIDAS 4d)
      Rep->>Rep: BuildTriggerCorrelations<br/>BuildTreatmentEfficacy (relief = end-of-episode<br/>or improved ping within 60 min of intake)<br/>BuildPatterns (time-of-day, day-of-week, period)<br/>BuildDays (per-day timeline)

      Rep-->>Pub: DoctorReportDto
      Pub-->>SPA: 200
      SPA-->>D: Render full report (timeline + flags + tables)
    end
```

Source: [`CreateShareLinkHandler`](../backend/src/MenuNest.Application/UseCases/Health/Share/CreateShareLink/CreateShareLinkHandler.cs), [`GetDoctorReportHandler`](../backend/src/MenuNest.Application/UseCases/Health/Reports/GetDoctorReport/GetDoctorReportHandler.cs), [`HmacShareTokenService`](../backend/src/MenuNest.Infrastructure/Services/HmacShareTokenService.cs), [`ShareUrlBuilder`](../backend/src/MenuNest.Infrastructure/Services/ShareUrlBuilder.cs).

**Security properties**

- **Token never persisted.** A DB leak does not expose live share links — only their SHA-256 hashes.
- **Two independent checks.** The HMAC signature + `exp` claim verify the token cryptographically. The `ShareLink.IsValidAt(now)` check is what enforces user-driven *revocation* (a patient toggling a link off in `/health/share` flips a flag in the DB; the JWT itself doesn't know).
- **`Share__BaseUrl` must be set on App Service** so the QR encodes an absolute URL. Without it, `ShareUrlBuilder` falls back to `/share/<token>` (a relative path) which the doctor's phone camera can't open. This was the cause of the recent "invalid link when share to doctor" report.
- **Patient SPA opens 400, not 500.** The handler wraps `Verify` in `try/catch (Exception ex) when (ex is not DomainException)` so JWT-layer exceptions (`SecurityTokenException`, etc.) are remapped — the Application layer doesn't take a dependency on `Microsoft.IdentityModel.Tokens`.

---

## Conventions used in these diagrams

- **autonumber** is on for every diagram so steps are easy to reference in code review.
- Each diagram is traced against the actual handler — file names are linked, not paraphrased. If you change a handler, please update the matching diagram in the same PR.
- DB writes inside one handler all happen in **one transaction** (one `SaveChangesAsync`) unless explicitly noted.
- Errors shown as `DomainException` are mapped by `ProblemDetailsMiddleware` to HTTP 400 with the message body — no stack trace leaks to the client.

For implementation plans (per-task breakdowns, test plans), see [`docs/superpowers/plans/`](superpowers/plans/). For overall scope and data model, see [`docs/plan.md`](plan.md).
