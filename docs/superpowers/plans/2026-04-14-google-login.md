# Google Login Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Google as a second authentication provider alongside Microsoft Entra ID, with provider tracking on the User entity.

**Architecture:** Backend replaces `AddMicrosoftIdentityWebApi()` with a policy scheme that routes JWT validation to either a "Microsoft" or "Google" named scheme based on the token's `iss` claim. Frontend adds `@react-oauth/google` alongside MSAL, stores the Google ID token in sessionStorage, and sends it as Bearer token. User entity gains an `AuthProvider` enum set at first sign-in.

**Tech Stack:** ASP.NET 10 (multi-scheme JWT Bearer), `@react-oauth/google`, MSAL.js (unchanged), RTK Query, Syncfusion buttons.

**Spec:** `docs/superpowers/specs/2026-04-14-google-login-design.md`

---

## File Map

### Backend — New Files

| File | Purpose |
|------|---------|
| `Domain/Enums/AuthProvider.cs` | Enum: Microsoft = 1, Google = 2 |
| `Infrastructure/Persistence/Migrations/YYYYMMDD_AddAuthProvider.cs` | EF migration (auto-generated) |

### Backend — Modified Files

| File | Change |
|------|--------|
| `Domain/Entities/User.cs` | Add `AuthProvider` property, rename factory to `CreateFromExternalLogin` |
| `Application/Abstractions/ICurrentUserService.cs` | Add `AuthProvider? Provider` |
| `Infrastructure/Authentication/CurrentUserService.cs` | Add `Provider` property, add `sub` fallback for ExternalId |
| `Infrastructure/Authentication/UserProvisioner.cs` | Pass provider to `User.CreateFromExternalLogin()` |
| `Infrastructure/Persistence/Configurations/UserConfiguration.cs` | Map `AuthProvider` column |
| `Application/UseCases/Me/GetMe/GetMeHandler.cs` | Add `AuthProvider` to MeDto |
| `Application/UseCases/Me/GetMe/MeDto.cs` (or inline) | Add `AuthProvider` field |
| `WebApi/Program.cs` | Replace auth config with dual JWT scheme |
| `WebApi/appsettings.json` | Add `Google` section |

### Frontend — New Files

| File | Purpose |
|------|---------|
| `shared/auth/googleAuth.ts` | Google token storage + decode helper |

### Frontend — Modified Files

| File | Change |
|------|--------|
| `main.tsx` | Wrap with `GoogleOAuthProvider` |
| `shared/auth/msalConfig.ts` | No changes (MSAL stays as-is) |
| `shared/api/api.ts` | Update `acquireAccessToken` + `MeDto` |
| `shared/components/ProtectedRoute.tsx` | Check both MSAL and Google auth |
| `shared/hooks/useCurrentUser.ts` | Support Google user info |
| `pages/auth/LoginPage.tsx` | Add "Sign in with Google" button |
| `.env.example` | Add `VITE_GOOGLE_CLIENT_ID` |

---

## Task 1: Backend — AuthProvider Enum + User Entity

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/AuthProvider.cs`
- Modify: `backend/src/MenuNest.Domain/Entities/User.cs`

- [ ] **Step 1: Create AuthProvider enum**

```csharp
namespace MenuNest.Domain.Enums;

public enum AuthProvider
{
    Microsoft = 1,
    Google = 2,
}
```

- [ ] **Step 2: Update User entity**

Add property and rename factory. In `User.cs`:

Add the using:
```csharp
using MenuNest.Domain.Enums;
```

Add property after `JoinedAt`:
```csharp
public AuthProvider AuthProvider { get; private set; }
```

Rename the factory method from `CreateFromEntraClaim` to `CreateFromExternalLogin` and add the `authProvider` parameter:

```csharp
public static User CreateFromExternalLogin(
    string externalId, string email, string displayName, AuthProvider authProvider)
{
    if (string.IsNullOrWhiteSpace(externalId))
    {
        throw new DomainException("ExternalId is required.");
    }
    if (string.IsNullOrWhiteSpace(email))
    {
        throw new DomainException("Email is required.");
    }

    return new User
    {
        ExternalId = externalId,
        Email = email.Trim(),
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim(),
        AuthProvider = authProvider,
    };
}
```

Remove the old `CreateFromEntraClaim` method entirely.

- [ ] **Step 3: Fix any callers of the old method name**

Search for `CreateFromEntraClaim` in the codebase. It is called in `UserProvisioner.cs` — that will be updated in Task 3. For now, note it will cause a build error until Task 3.

- [ ] **Step 4: Build Domain project**

Run: `dotnet build backend/src/MenuNest.Domain/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/AuthProvider.cs backend/src/MenuNest.Domain/Entities/User.cs
git commit -m "feat(auth): add AuthProvider enum and update User entity factory"
```

---

## Task 2: Backend — EF Configuration + Migration

**Files:**
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserConfiguration.cs`

- [ ] **Step 1: Add AuthProvider column mapping**

In `UserConfiguration.cs`, add after the `JoinedAt` mapping:

```csharp
builder.Property(u => u.AuthProvider)
    .IsRequired()
    .HasDefaultValue(MenuNest.Domain.Enums.AuthProvider.Microsoft);
```

Add the using at the top:
```csharp
using MenuNest.Domain.Enums;
```

- [ ] **Step 2: Generate EF migration**

Run:
```bash
cd backend
dotnet ef migrations add AddAuthProvider --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Expected: Migration file created. The migration should add an `AuthProvider` int column with default value 1 (Microsoft) for existing rows.

- [ ] **Step 3: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(auth): add AuthProvider column with EF migration"
```

---

## Task 3: Backend — CurrentUserService + UserProvisioner

**Files:**
- Modify: `backend/src/MenuNest.Application/Abstractions/ICurrentUserService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Authentication/CurrentUserService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Authentication/UserProvisioner.cs`

- [ ] **Step 1: Add Provider to ICurrentUserService**

In `ICurrentUserService.cs`, add the using and the property:

```csharp
using MenuNest.Domain.Enums;
```

Add after `DisplayName`:
```csharp
/// <summary>
/// The authentication provider that issued the current token.
/// Determined from the JWT <c>iss</c> claim.
/// </summary>
AuthProvider? Provider { get; }
```

- [ ] **Step 2: Update CurrentUserService**

In `CurrentUserService.cs`, add the using:
```csharp
using MenuNest.Domain.Enums;
```

Add `sub` as a fallback for `ExternalId` (Google uses `sub` instead of `oid`):

Replace the `ExternalId` property:
```csharp
public string? ExternalId =>
    Principal?.FindFirstValue(ObjectIdClaim)
    ?? Principal?.FindFirstValue(ShortObjectIdClaim)
    ?? Principal?.FindFirstValue("sub");
```

Add the `Provider` property:
```csharp
public AuthProvider? Provider
{
    get
    {
        var issuer = Principal?.FindFirstValue("iss");
        if (issuer == "https://accounts.google.com")
            return AuthProvider.Google;
        if (issuer?.Contains("login.microsoftonline.com") == true
            || issuer?.Contains("sts.windows.net") == true)
            return AuthProvider.Microsoft;
        return null;
    }
}
```

- [ ] **Step 3: Update UserProvisioner**

In `UserProvisioner.cs`, add the using:
```csharp
using MenuNest.Domain.Enums;
```

In `GetOrProvisionCurrentAsync`, replace the user creation block:

```csharp
var email = _currentUser.Email ?? $"{externalId}@unknown";
var displayName = _currentUser.DisplayName ?? email;
var provider = _currentUser.Provider ?? AuthProvider.Microsoft;

var user = User.CreateFromExternalLogin(externalId, email, displayName, provider);
_db.Users.Add(user);
await _db.SaveChangesAsync(ct);
return user;
```

- [ ] **Step 4: Build Application + Infrastructure**

Run: `dotnet build backend/src/MenuNest.Infrastructure/`
Expected: Build succeeded (this also builds Application and Domain transitively)

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/Abstractions/ICurrentUserService.cs backend/src/MenuNest.Infrastructure/Authentication/CurrentUserService.cs backend/src/MenuNest.Infrastructure/Authentication/UserProvisioner.cs
git commit -m "feat(auth): add Provider to CurrentUserService, sub claim fallback, update UserProvisioner"
```

---

## Task 4: Backend — GetMeHandler + MeDto

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs`

- [ ] **Step 1: Find and update MeDto**

First check if MeDto is defined in its own file or inline in GetMeHandler. Search for `record MeDto` in `backend/src/MenuNest.Application/UseCases/Me/`.

Add `AuthProvider` field to MeDto:

```csharp
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid? FamilyId,
    string? FamilyName,
    string? FamilyInviteCode,
    string AuthProvider);
```

- [ ] **Step 2: Update GetMeHandler**

In the `Handle` method, update the return statement to include `AuthProvider`:

```csharp
return new MeDto(
    UserId: user.Id,
    Email: user.Email,
    DisplayName: user.DisplayName,
    FamilyId: user.FamilyId,
    FamilyName: user.Family?.Name,
    FamilyInviteCode: user.Family?.InviteCode.Value,
    AuthProvider: user.AuthProvider.ToString());
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/MenuNest.Application/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Me/
git commit -m "feat(auth): add AuthProvider to MeDto"
```

---

## Task 5: Backend — Multi-Scheme JWT in Program.cs

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Program.cs`
- Modify: `backend/src/MenuNest.WebApi/appsettings.json`
- Modify: `backend/src/MenuNest.WebApi/appsettings.Development.json`

- [ ] **Step 1: Update appsettings.json**

Add the Google section after AzureAd:

```json
"Google": {
  "ClientId": "000000000000-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"
}
```

- [ ] **Step 2: Update appsettings.Development.json**

Add the Google section (use your actual Google Client ID):

```json
"Google": {
  "ClientId": "YOUR_GOOGLE_CLIENT_ID.apps.googleusercontent.com"
}
```

- [ ] **Step 3: Replace auth config in Program.cs**

Remove these lines (the old Entra-only auth):
```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services
    .Configure<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme,
        options =>
        {
            options.TokenValidationParameters.ValidateIssuer = false;
        });
```

Replace with the dual-scheme setup:

```csharp
// ----------------------------------------------------------------------
// Authentication — Multi-provider JWT bearer (Microsoft + Google)
// A policy scheme reads the JWT `iss` claim and forwards to the
// matching named scheme for validation.
// ----------------------------------------------------------------------
builder.Services
    .AddAuthentication("MultiAuth")
    .AddPolicyScheme("MultiAuth", "Microsoft + Google selector", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                var token = authHeader["Bearer ".Length..];
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    if (jwt.Issuer == "https://accounts.google.com")
                        return "Google";
                }
            }
            return "Microsoft";
        };
    })
    .AddJwtBearer("Microsoft", options =>
    {
        var azureAd = builder.Configuration.GetSection("AzureAd");
        options.Authority = $"{azureAd["Instance"]}common/v2.0";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidAudience = azureAd["ClientId"],
            ValidateIssuer = false, // multi-tenant
        };
    })
    .AddJwtBearer("Google", options =>
    {
        options.Authority = "https://accounts.google.com";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidAudience = builder.Configuration["Google:ClientId"],
            ValidateIssuer = true,
            ValidIssuer = "https://accounts.google.com",
        };
    });
```

Remove the `using Microsoft.Identity.Web;` import if no longer needed (check for other usages first).

- [ ] **Step 4: Build**

Run: `dotnet build backend/src/MenuNest.WebApi/`
Expected: Build succeeded. If `Microsoft.Identity.Web` is no longer referenced, the import removal is clean. If there are other usages, keep it.

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Program.cs backend/src/MenuNest.WebApi/appsettings.json backend/src/MenuNest.WebApi/appsettings.Development.json
git commit -m "feat(auth): replace Entra-only auth with multi-scheme JWT (Microsoft + Google)"
```

---

## Task 6: Frontend — Google Auth Helper + Environment

**Files:**
- Create: `frontend/src/shared/auth/googleAuth.ts`
- Modify: `frontend/.env.example`

- [ ] **Step 1: Update .env.example**

Add after the MSAL section:

```
# Google OAuth
VITE_GOOGLE_CLIENT_ID=000000000000-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com
```

- [ ] **Step 2: Create googleAuth.ts**

```typescript
const GOOGLE_TOKEN_KEY = 'google_id_token'

export function getGoogleToken(): string | null {
  return sessionStorage.getItem(GOOGLE_TOKEN_KEY)
}

export function setGoogleToken(token: string): void {
  sessionStorage.setItem(GOOGLE_TOKEN_KEY, token)
}

export function clearGoogleToken(): void {
  sessionStorage.removeItem(GOOGLE_TOKEN_KEY)
}

export function isGoogleAuthenticated(): boolean {
  return !!getGoogleToken()
}

/**
 * Decode the payload of a JWT without verification (for display only —
 * the backend validates the token fully). Returns null if the token
 * is malformed.
 */
export function decodeGoogleIdToken(
  token: string,
): { sub: string; email: string; name: string; picture?: string } | null {
  try {
    const payload = token.split('.')[1]
    const decoded = JSON.parse(atob(payload))
    return decoded
  } catch {
    return null
  }
}
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/shared/auth/googleAuth.ts frontend/.env.example
git commit -m "feat(auth): add Google token storage helper and env config"
```

---

## Task 7: Frontend — Wrap App with GoogleOAuthProvider

**Files:**
- Modify: `frontend/src/main.tsx`

- [ ] **Step 1: Install @react-oauth/google**

Run: `cd frontend && npm install @react-oauth/google`

- [ ] **Step 2: Update main.tsx**

Add import at the top:
```typescript
import { GoogleOAuthProvider } from '@react-oauth/google'
```

In the `bootstrap()` function's `createRoot(...).render(...)`, wrap with GoogleOAuthProvider. Change:

```tsx
<StrictMode>
  <MsalProvider instance={msalInstance}>
    <ReduxProvider store={store}>
      <App />
    </ReduxProvider>
  </MsalProvider>
</StrictMode>
```

To:

```tsx
<StrictMode>
  <GoogleOAuthProvider clientId={import.meta.env.VITE_GOOGLE_CLIENT_ID ?? ''}>
    <MsalProvider instance={msalInstance}>
      <ReduxProvider store={store}>
        <App />
      </ReduxProvider>
    </MsalProvider>
  </GoogleOAuthProvider>
</StrictMode>
```

- [ ] **Step 3: Build**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add frontend/src/main.tsx frontend/package.json frontend/package-lock.json
git commit -m "feat(auth): install @react-oauth/google and wrap App with provider"
```

---

## Task 8: Frontend — Update api.ts prepareHeaders + MeDto

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 1: Update acquireAccessToken**

Add import at top of `api.ts`:
```typescript
import { getGoogleToken } from '../auth/googleAuth'
```

Replace the `acquireAccessToken` function:

```typescript
async function acquireAccessToken(): Promise<string | null> {
    // Try MSAL first (Microsoft)
    const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]
    if (account && apiScopes.length > 0) {
        try {
            const result = await msalInstance.acquireTokenSilent({scopes: apiScopes, account})
            return result.accessToken
        } catch (err) {
            if (err instanceof InteractionRequiredAuthError) {
                await msalInstance.acquireTokenRedirect({scopes: apiScopes, account})
            }
            // Fall through to Google check
        }
    }

    // Try Google token
    const googleToken = getGoogleToken()
    if (googleToken) return googleToken

    return null
}
```

- [ ] **Step 2: Update MeDto interface**

Add `authProvider` field:

```typescript
export interface MeDto {
    userId: string
    email: string
    displayName: string
    familyId: string | null
    familyName: string | null
    familyInviteCode: string | null
    authProvider: string
}
```

- [ ] **Step 3: Build**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(auth): update prepareHeaders for dual provider + add authProvider to MeDto"
```

---

## Task 9: Frontend — ProtectedRoute + useCurrentUser

**Files:**
- Modify: `frontend/src/shared/components/ProtectedRoute.tsx`
- Modify: `frontend/src/shared/hooks/useCurrentUser.ts`

- [ ] **Step 1: Update ProtectedRoute**

Add import:
```typescript
import { isGoogleAuthenticated } from '../auth/googleAuth'
```

Change the auth check. Replace:
```typescript
if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />
}
```

With:
```typescript
if (!isAuthenticated && !isGoogleAuthenticated()) {
    return <Navigate to="/login" replace state={{ from: location }} />
}
```

- [ ] **Step 2: Update useCurrentUser**

Add imports:
```typescript
import { isGoogleAuthenticated, getGoogleToken, decodeGoogleIdToken, clearGoogleToken } from '../auth/googleAuth'
```

Update the hook. The key changes:
1. Skip RTK Query only when neither provider is authenticated
2. Derive display info from Google token when MSAL is not active
3. signOut clears both providers

Replace the full hook:

```typescript
export function useCurrentUser() {
  const { instance, accounts } = useMsal()
  const isMsalAuth = useIsAuthenticated()
  const account = accounts[0] ?? null

  const isAuthenticated = isMsalAuth || isGoogleAuthenticated()

  const {
    data: me,
    isLoading: isLoadingProfile,
    isFetching: isFetchingProfile,
    error: profileError,
  } = useGetMeQuery(undefined, { skip: !isAuthenticated })

  // Decode Google token for immediate display (before /api/me responds)
  const googleToken = getGoogleToken()
  const googleUser = googleToken ? decodeGoogleIdToken(googleToken) : null

  const signOut = () => {
    clearGoogleToken()
    if (isMsalAuth) {
      instance.logoutRedirect()
    } else {
      window.location.href = '/login'
    }
  }

  return {
    isAuthenticated,
    account,
    displayName: me?.displayName ?? account?.name ?? googleUser?.name ?? '',
    email: me?.email ?? account?.username ?? googleUser?.email ?? '',
    userId: me?.userId ?? null,
    familyId: me?.familyId ?? null,
    familyName: me?.familyName ?? null,
    familyInviteCode: me?.familyInviteCode ?? null,
    authProvider: me?.authProvider ?? (isMsalAuth ? 'Microsoft' : googleUser ? 'Google' : null),
    isLoadingProfile: isAuthenticated && (isLoadingProfile || (isFetchingProfile && !me)),
    profileError,
    signOut,
  }
}
```

- [ ] **Step 3: Build**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/components/ProtectedRoute.tsx frontend/src/shared/hooks/useCurrentUser.ts
git commit -m "feat(auth): update ProtectedRoute and useCurrentUser for dual provider"
```

---

## Task 10: Frontend — LoginPage (Add Google Button)

**Files:**
- Modify: `frontend/src/pages/auth/LoginPage.tsx`

- [ ] **Step 1: Update LoginPage**

Add imports:
```typescript
import { useGoogleLogin } from '@react-oauth/google'
import { useNavigate } from 'react-router-dom'
import { setGoogleToken } from '../../shared/auth/googleAuth'
```

Also import `isGoogleAuthenticated`:
```typescript
import { isGoogleAuthenticated } from '../../shared/auth/googleAuth'
```

Inside the component, add navigate and Google login:

```typescript
const navigate = useNavigate()
```

Update the redirect check to also cover Google:
```typescript
if (inProgress === InteractionStatus.None && (isAuthenticated || isGoogleAuthenticated())) {
    return <Navigate to="/" replace />
}
```

Add Google login handler. Note: `useGoogleLogin` with `flow: 'implicit'` returns an access token, but for JWT validation on our backend we need the **ID token**. Use the `GoogleLogin` component or `useGoogleOneTapLogin` with `credential` response instead. The simplest approach is the `GoogleLogin` button component which returns a `credential` (ID token):

Replace the approach — instead of `useGoogleLogin`, import and use `GoogleLogin` component:

```typescript
import { GoogleLogin } from '@react-oauth/google'
```

Add state for Google error:
```typescript
const [googleError, setGoogleError] = useState<string | null>(null)
```

Add `useState` import if not already present.

Update the JSX. After the Microsoft button and before the footer, add:

```tsx
<div style={{ textAlign: 'center', color: 'var(--color-text-muted)', margin: '16px 0', fontSize: 14 }}>
  or
</div>

<div style={{ display: 'flex', justifyContent: 'center' }}>
  <GoogleLogin
    onSuccess={(credentialResponse) => {
      if (credentialResponse.credential) {
        setGoogleToken(credentialResponse.credential)
        navigate('/', { replace: true })
      }
    }}
    onError={() => setGoogleError('Google sign-in failed. Please try again.')}
    size="large"
    width={320}
    text="signin_with"
  />
</div>

{googleError && (
  <p className="field-error" style={{ textAlign: 'center', marginTop: 8 }}>
    {googleError}
  </p>
)}
```

Update the footer text:
```tsx
<p className="login-card__footer">
  Sign in with your Microsoft or Google account.
</p>
```

- [ ] **Step 2: Build**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/auth/LoginPage.tsx
git commit -m "feat(auth): add Google Sign-In button to LoginPage"
```

---

## Task 11: Verification — Full Build + Test

- [ ] **Step 1: Build backend**

Run: `dotnet build backend/`
Expected: 0 compilation errors

- [ ] **Step 2: Build frontend**

Run: `cd frontend && npx tsc --noEmit`
Expected: No type errors

- [ ] **Step 3: Manual test checklist**

Start both servers and test:

- [ ] Login page shows both "Sign in with Microsoft" and "Sign in with Google" buttons
- [ ] Microsoft sign-in still works (existing flow unchanged)
- [ ] Google sign-in: click → Google consent screen → redirect back → authenticated
- [ ] After Google sign-in: `/api/me` returns user with `authProvider: "Google"`
- [ ] Protected routes work with Google token
- [ ] Google user can create/join family and use all features
- [ ] Logout clears Google token and redirects to login
- [ ] Two users with same email but different providers = two separate DB rows

---

## Summary

| Task | Description | Files |
|------|------------|-------|
| 1 | AuthProvider enum + User entity | 2 modified |
| 2 | EF Configuration + Migration | 1 modified + migration |
| 3 | CurrentUserService + UserProvisioner | 3 modified |
| 4 | GetMeHandler + MeDto | 1-2 modified |
| 5 | Program.cs multi-scheme JWT + config | 3 modified |
| 6 | Google auth helper + env | 1 new, 1 modified |
| 7 | GoogleOAuthProvider wrapper | 1 modified + npm install |
| 8 | api.ts prepareHeaders + MeDto | 1 modified |
| 9 | ProtectedRoute + useCurrentUser | 2 modified |
| 10 | LoginPage Google button | 1 modified |
| 11 | Verification | — |

**Total: 1 new file, ~14 modified files, 11 tasks**
