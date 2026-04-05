# Azure Entra ID — OAuth 2.0 User Sign-In

## What This Is

ServiceHub supports **passwordless Azure sign-in** via OAuth 2.0 Authorization Code + PKCE (RFC 7636). When this is configured:

- Users click **Sign in with Microsoft** on the Connect page
- They authenticate on Microsoft's own login page (ServiceHub never sees passwords)
- ServiceHub receives a short-lived, scoped token tied to the user's own Azure RBAC permissions
- The user picks their Service Bus namespace from a dropdown — no hostnames to type

This is the most secure authentication method available. It satisfies Zero Trust requirements and requires zero user training.

---

## How It Works (Security Architecture)

```
User browser ──► ServiceHub UI ──► GET /api/v1/auth/azure/sign-in
                                        │
                                        ▼ returns authorization URL
User browser ──► Microsoft login.microsoftonline.com (PKCE challenge)
                                        │
                                        ▼ user authenticates on Microsoft's page
Microsoft ──► GET /api/v1/auth/azure/callback?code=AUTH_CODE&state=CSRF_STATE
                                        │
                                        ▼ code exchange (server-side)
ServiceHub ──► POST login.microsoftonline.com/token (code + code_verifier)
                                        │
                                        ▼ access token + refresh token
ServiceHub ──► creates in-memory session, sets HttpOnly cookie
                                        │
                                        ▼ redirects browser back to /connect?auth=success
User ──► picks namespace from dropdown (loaded via ARM API with delegated token)
User ──► clicks Connect → ServiceHub uses token to connect to Service Bus
```

### Security properties

| Property | Detail |
|---|---|
| PKCE (S256) | Code verifier never leaves server. Authorization code interception is prevented. |
| CSRF state | 32-byte cryptographic random state, one-time use, 10-minute TTL. |
| Session cookie | HttpOnly, SameSite=Lax, Secure (in production), 8-hour lifetime. |
| Scopes | `management.azure.com/user_impersonation` (namespace listing) + `servicebus.azure.com/user_impersonation` (connection). |
| No stored secrets | Tokens are in-memory only. Released after 8 hours. Revocable by user. |
| User's RBAC applies | ServiceHub can only access namespaces the signed-in user can already access. |

---

## For DevOps / SRE: One-Time Setup

### Step 1 — Register ServiceHub in Azure Portal

1. Go to [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID** → **App registrations**
2. Click **+ New registration**
3. Fill in:
   - **Name**: `ServiceHub` (or your preferred name)
   - **Supported account types**: *Accounts in this organizational directory only* (single-tenant)
     or *Accounts in any organizational directory* (multi-tenant — for shared deployments)
   - **Redirect URI**: `Web` → `https://YOUR_SERVICEHUB_URL/api/v1/auth/azure/callback`
     - For local dev: `http://localhost:5153/api/v1/auth/azure/callback`
4. Click **Register**
5. Note down the **Application (client) ID** and **Directory (tenant) ID**

### Step 2 — Add API Permissions

1. In your App Registration → **API permissions** → **+ Add a permission**
2. **Microsoft Graph**:
   - Delegated → `User.Read` (already added by default)
   - Delegated → `openid`, `profile`, `email`, `offline_access`
3. **Azure Service Management**:
   - Delegated → `user_impersonation`
4. **Azure Service Bus** (search "Azure Service Bus"):
   - Delegated → `user_impersonation`
5. Click **Grant admin consent** (if you have Global Admin)

### Step 3 — Create a Client Secret

1. App Registration → **Certificates & secrets** → **+ New client secret**
2. Description: `ServiceHub-OAuth`
3. Expires: 24 months (set a reminder to rotate)
4. Copy the **Value** immediately — it won't be shown again

### Step 4 — Configure ServiceHub

Set these environment variables on your ServiceHub API (Azure App Service / container / local):

```bash
AzureOAuth__Enabled=true
AzureOAuth__ClientId=<Application (client) ID from Step 1>
AzureOAuth__ClientSecret=<Secret Value from Step 3>
AzureOAuth__RedirectUri=https://YOUR_SERVICEHUB_URL/api/v1/auth/azure/callback
AzureOAuth__FrontendBaseUrl=https://YOUR_SERVICEHUB_URL
```

For **local development** (no HTTPS required):
```bash
AzureOAuth__Enabled=true
AzureOAuth__ClientId=<your client ID>
AzureOAuth__ClientSecret=<your secret>
AzureOAuth__RedirectUri=http://localhost:5153/api/v1/auth/azure/callback
AzureOAuth__FrontendBaseUrl=http://localhost:3000
```

Or in `appsettings.Local.json` (git-ignored):
```json
{
  "AzureOAuth": {
    "Enabled": true,
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "RedirectUri": "http://localhost:5153/api/v1/auth/azure/callback",
    "FrontendBaseUrl": "http://localhost:3000"
  }
}
```

### Step 5 — Grant Users Access to Namespaces

Users who sign in via ServiceHub can only access Service Bus namespaces where they have an RBAC role assignment.

For each namespace a user needs access to:
1. Azure Portal → Service Bus namespace → **Access Control (IAM)**
2. **+ Add role assignment**
3. Role: `Azure Service Bus Data Owner` (full access) or `Azure Service Bus Data Receiver` (read-only)
4. Assign to: the user's account (or an Entra ID group)

> **Tip**: Assign roles to Entra ID groups rather than individual users for easier management.

---

## For End Users: What to Expect

1. Go to the ServiceHub **Connect** page → **Azure Entra ID** tab (default)
2. Click **Sign in with Microsoft**
3. Microsoft's login page opens — enter your work/school email and password normally
4. You may be asked to consent to ServiceHub accessing your Service Bus namespaces on your behalf (one-time)
5. You are redirected back to ServiceHub, signed in
6. A dropdown shows all Service Bus namespaces you have access to across all your Azure subscriptions
7. Select a namespace, give it a display name, click **Connect**

Your sign-in session lasts 8 hours. After that, click **Sign in with Microsoft** again.

---

## Frequently Asked Questions

**Q: Does ServiceHub store my password?**
No. You authenticate on Microsoft's login page. ServiceHub only receives a short-lived access token.

**Q: What can ServiceHub see about my account?**
ServiceHub reads your email/UPN (for display purposes) and lists Service Bus namespaces in your subscriptions. Nothing else.

**Q: Can ServiceHub access my namespaces after I sign out?**
No. The session is deleted immediately on sign-out, and the access token is in-memory only — not persisted anywhere.

**Q: What if I don't have access to a namespace?**
It won't appear in the dropdown. Ask your Azure administrator to grant you the `Azure Service Bus Data Owner` or `Data Receiver` role on that namespace.

**Q: Is this compliant with our security policy?**
OAuth 2.0 Authorization Code + PKCE is the industry standard for user-delegated authentication. It satisfies Zero Trust Architecture principles and is used by Microsoft's own products.

---

## API Reference

| Endpoint | Description |
|---|---|
| `GET /api/v1/auth/azure/status` | Check if user is signed in and OAuth is configured |
| `GET /api/v1/auth/azure/sign-in` | Get the Azure authorization URL |
| `GET /api/v1/auth/azure/callback` | OAuth callback (called by Azure — do not call directly) |
| `GET /api/v1/auth/azure/namespaces` | List accessible Service Bus namespaces |
| `DELETE /api/v1/auth/azure/session` | Sign out |

---

## Troubleshooting

### "Azure OAuth sign-in is not configured on this ServiceHub instance"
→ The `AzureOAuth__Enabled=true` environment variable is not set or the ClientId/ClientSecret are empty.

### "AADSTS50011: The redirect URI specified in the request does not match"
→ The `AzureOAuth__RedirectUri` value must exactly match one of the Redirect URIs registered in your App Registration.

### "No namespaces found in your subscriptions"
→ You have no Service Bus namespaces, or you don't have an RBAC role on any of them. Check Access Control (IAM) on each namespace.

### Session cookie not set / signed in but redirected to sign-in again
→ Check that the API and frontend are on the same site (same domain or CORS is configured correctly). The session cookie uses `SameSite=Lax`.
