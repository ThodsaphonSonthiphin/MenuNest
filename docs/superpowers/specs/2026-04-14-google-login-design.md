# Google Login Support — Design Spec

## Goal

Add Google as a second authentication provider alongside Microsoft Entra ID.
Users choose which provider to sign in with on the login page. Each provider
creates a separate user (no account linking). Track which provider each user
signed in from.

## Scope

| Change area | What changes |
|-------------|-------------|
| Backend auth | Replace `AddMicrosoftIdentityWebApi()` with dual JWT Bearer schemes (Microsoft + Google) using a policy scheme that auto-selects based on `iss` claim |
| Backend domain | Add `AuthProvider` enum + property on `User` entity |
| Backend provisioning | `CurrentUserService` reads provider from `iss` claim; `UserProvisioner` passes it to `User.CreateFromExternalLogin()` |
| Frontend auth | Add `@react-oauth/google` alongside MSAL; token storage + `prepareHeaders` supports both |
| Frontend login | LoginPage shows two buttons: "Sign in with Microsoft" and "Sign in with Google" |
| Database | Migration adds `AuthProvider` column (default 1 = Microsoft for existing rows) |

## Backend — Multi-Scheme JWT Validation

### Program.cs changes

Remove `AddMicrosoftIdentityWebApi()`. Replace with two named JWT Bearer
schemes + a policy scheme selector:

```csharp
// Policy scheme: auto-selects "Microsoft" or "Google" based on JWT issuer
builder.Services.AddAuthentication("MultiAuth")
    .AddPolicyScheme("MultiAuth", "Multi-provider", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ") == true)
            {
                var token = authHeader["Bearer ".Length..];
                // Read issuer from JWT payload without full validation
                var handler = new JsonWebTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJsonWebToken(token);
                    var issuer = jwt.Issuer;
                    if (issuer == "https://accounts.google.com")
                        return "Google";
                }
            }
            return "Microsoft"; // default
        };
    })
    .AddJwtBearer("Microsoft", options =>
    {
        var azureAd = builder.Configuration.GetSection("AzureAd");
        options.Authority = $"{azureAd["Instance"]}common/v2.0";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = azureAd["ClientId"],
            ValidateIssuer = false, // multi-tenant
        };
    })
    .AddJwtBearer("Google", options =>
    {
        options.Authority = "https://accounts.google.com";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = builder.Configuration["Google:ClientId"],
            ValidateIssuer = true,
            ValidIssuer = "https://accounts.google.com",
        };
    });
```

### Configuration

Add to `appsettings.json`:

```json
"Google": {
  "ClientId": "<google-client-id>.apps.googleusercontent.com"
}
```

Environment variable: `Google__ClientId` (for production).

## Backend — Domain Changes

### AuthProvider enum

```csharp
namespace MenuNest.Domain.Enums;

public enum AuthProvider
{
    Microsoft = 1,
    Google = 2,
}
```

### User entity changes

- Add property: `public AuthProvider AuthProvider { get; private set; }`
- Rename factory: `User.CreateFromEntraClaim(externalId, email, displayName)`
  → `User.CreateFromExternalLogin(externalId, email, displayName, authProvider)`
- The `AuthProvider` is set at creation time and never changes.

### EF Configuration

- Add column mapping for `AuthProvider` (int, not null).
- Migration default value = `1` (Microsoft) for existing rows.

### CurrentUserService changes

Add `AuthProvider? Provider` property:

```csharp
public AuthProvider? Provider
{
    get
    {
        var issuer = Principal?.FindFirstValue("iss");
        if (issuer == "https://accounts.google.com") return AuthProvider.Google;
        if (issuer?.Contains("login.microsoftonline.com") == true
            || issuer?.Contains("sts.windows.net") == true)
            return AuthProvider.Microsoft;
        return null;
    }
}
```

Add `sub` as a fallback for `ExternalId` (Google uses `sub` instead of `oid`):

```csharp
public string? ExternalId =>
    Principal?.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
    ?? Principal?.FindFirstValue("oid")
    ?? Principal?.FindFirstValue("sub");  // <-- Google fallback
```

### ICurrentUserService interface

Add: `AuthProvider? Provider { get; }`

### UserProvisioner changes

Pass `provider` when creating new users:

```csharp
var provider = _currentUser.Provider ?? AuthProvider.Microsoft;
var user = User.CreateFromExternalLogin(externalId, email, displayName, provider);
```

### MeDto changes

Add `authProvider` field:

```csharp
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid? FamilyId,
    string? FamilyName,
    string? FamilyInviteCode,
    string AuthProvider);  // "Microsoft" or "Google"
```

GetMeHandler maps: `AuthProvider: user.AuthProvider.ToString()`

## Frontend — Google OAuth Integration

### New dependency

```bash
npm install @react-oauth/google
```

### App wrapper

Wrap the app with `<GoogleOAuthProvider>` alongside the existing
`<MsalProvider>`:

```tsx
// main.tsx or App.tsx
import { GoogleOAuthProvider } from '@react-oauth/google'

<GoogleOAuthProvider clientId={import.meta.env.VITE_GOOGLE_CLIENT_ID}>
  <MsalProvider instance={msalInstance}>
    <App />
  </MsalProvider>
</GoogleOAuthProvider>
```

### Environment variable

Add: `VITE_GOOGLE_CLIENT_ID` to `.env` / `.env.example`

### Token storage

Create a simple auth store for the Google token (alongside MSAL which
manages its own storage). Options:

- `sessionStorage` key: `google_id_token`
- A shared React context or a small Zustand/Redux slice

When Google login succeeds, store the ID token. On logout, clear it.

### prepareHeaders update

```typescript
async function acquireAccessToken(): Promise<string | null> {
  // Try MSAL first (existing logic)
  const msalAccount = msalInstance.getActiveAccount()
    ?? msalInstance.getAllAccounts()[0]
  if (msalAccount && apiScopes.length > 0) {
    try {
      const result = await msalInstance.acquireTokenSilent({
        scopes: apiScopes,
        account: msalAccount,
      })
      return result.accessToken
    } catch { /* fall through */ }
  }

  // Try Google token
  const googleToken = sessionStorage.getItem('google_id_token')
  if (googleToken) return googleToken

  return null
}
```

### ProtectedRoute update

Check both MSAL `isAuthenticated` and Google token presence:

```typescript
const isMsalAuth = useIsAuthenticated()
const hasGoogleToken = !!sessionStorage.getItem('google_id_token')
const isAuthenticated = isMsalAuth || hasGoogleToken
```

### useCurrentUser update

Extend to read Google user info from the stored ID token (decode JWT
client-side for display name / email) when MSAL is not active.

### LoginPage update

Add a "Sign in with Google" button below the existing Microsoft button:

```tsx
import { useGoogleLogin } from '@react-oauth/google'

const googleLogin = useGoogleLogin({
  flow: 'implicit',  // gets ID token directly
  onSuccess: (response) => {
    sessionStorage.setItem('google_id_token', response.credential)
    navigate('/')
  },
  onError: () => setError('Google sign-in failed'),
})
```

The login page shows:
1. "Sign in with Microsoft" button (existing, MSAL redirect)
2. Divider "or"
3. "Sign in with Google" button (new, Google Identity Services)

### MeDto frontend interface

Add `authProvider` field:

```typescript
export interface MeDto {
  userId: string
  email: string
  displayName: string
  familyId: string | null
  familyName: string | null
  familyInviteCode: string | null
  authProvider: string  // "Microsoft" | "Google"
}
```

## Google Cloud Console Setup (manual)

Not automated — done once in the Google Cloud Console:

1. Create a project (or use existing)
2. Enable "Google Identity" API
3. Create OAuth 2.0 Client ID (Web application type)
4. Set authorized JavaScript origins: `http://localhost:5173` (dev) + production URL
5. Set authorized redirect URIs: same origins
6. Copy Client ID → `VITE_GOOGLE_CLIENT_ID` + `Google:ClientId` in backend config

## Out of Scope

- Account linking (same email, different providers = different users)
- Social login for other providers (GitHub, Facebook, etc.)
- Backend-issued custom JWT (we use provider JWT directly)
- Refresh token for Google (ID tokens are short-lived; user re-authenticates when expired)
- Migration of existing Microsoft users to Google

## Security Considerations

- Google ID tokens are validated server-side using Google's public keys
  (fetched via OIDC discovery endpoint).
- `aud` claim validation ensures tokens are for our app only.
- `email_verified` claim from Google is available but not enforced (since
  we don't do account linking).
- Google ID tokens are short-lived (~1 hour). Unlike MSAL which handles
  silent refresh automatically, Google tokens may require the user to
  re-authenticate. This is acceptable for MVP.
