# Azure Entra ID — OAuth 2.0 User Sign-In

> **If you see "Microsoft sign-in is not available on this instance"** on the Connect page,
> your administrator has not yet enabled OAuth. Jump to [Enable Microsoft Sign-in (Administrators)](#for-devops--sre-one-time-setup).
> You can connect right now using a **Connection String** — click "Use Connection String instead →" on the Connect page.

---

### 🚀 Quick Setup for Administrators

**Want to set up OAuth in 10 minutes?** Use the [**Setup Checklist**](QUICKSTART-CHECKLIST.md) — it's a step-by-step guide with checkboxes and copy-paste values.

[→ **Open Setup Checklist**](QUICKSTART-CHECKLIST.md)

---

## What This Is

ServiceHub supports **passwordless Azure sign-in** via OAuth 2.0 Authorization Code + PKCE (RFC 7636). When this is configured:

- Users click **Sign in with Microsoft** on the Connect page
- They authenticate on Microsoft's own login page (ServiceHub never sees passwords)
- ServiceHub receives a short-lived, scoped token tied to the user's own Azure RBAC permissions
- The user picks their Service Bus namespace from a dropdown — no hostnames to type

This is the most secure authentication method available. It satisfies Zero Trust requirements and requires zero user training.

---

## What You Will See on the Connect Page

The Azure Entra ID tab shows different content depending on how ServiceHub is configured.
**There are three distinct states:**

### State 1 — Microsoft sign-in not enabled (amber warning)

**You see this when:** The ServiceHub administrator has not yet configured an Azure App Registration.

```
┌─────────────────────────────────────────────────────────────┐
│  🔒 Microsoft sign-in is not available on this instance     │
│                                                             │
│  The administrator of this ServiceHub instance has not      │
│  enabled Microsoft sign-in yet. You can connect right now   │
│  using a Connection String instead, or ask your             │
│  administrator to enable passwordless sign-in.              │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │         Use Connection String instead →             │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ▶ Administrator: enable Microsoft sign-in  (collapsed)     │
└─────────────────────────────────────────────────────────────┘
```

**What to do:**
- Click **"Use Connection String instead →"** to connect immediately with a connection string
- Or ask your ServiceHub administrator to enable OAuth (see [Administrator Setup](#for-devops--sre-one-time-setup))

---

### State 2 — Microsoft sign-in enabled, not yet signed in

**You see this when:** OAuth is configured and you haven't signed in yet.

```
┌─────────────────────────────────────────────────────────────┐
│  ℹ️  What is Azure Entra ID?                                 │
│  Microsoft's enterprise identity platform. When you sign in │
│  with your company account, ServiceHub receives a           │
│  short-lived token scoped only to namespaces you already    │
│  have access to.                                            │
│                                                             │
│  ✅ No passwords typed here                                  │
│  ✅ No connection strings needed                             │
│  ✅ Short-lived tokens only (8 hours)                        │
│  ✅ Conforms to zero-trust                                   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  ▪▪▪▪  Sign in with Microsoft                      │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**⬆ This is the "Sign in with Microsoft" button.** Click it to be redirected to Microsoft's login page.

---

### State 3 — Signed in — pick a namespace

**You see this when:** You have successfully signed in with your Microsoft account.

```
┌─────────────────────────────────────────────────────────────┐
│  ✅ Signed in as alice@contoso.com          [Sign out]       │
│                                                             │
│  Select a namespace:                                        │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  mybus (East US) — Standard                     ▼  │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  Display name:  [                              ]            │
│  Environment:   [Dev ▼]                                     │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                    Connect                          │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**What to do:** Select the namespace you want, add a display name, choose an environment, and click **Connect**.

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

### Quick Reference — What You Need to Add

Before diving into the steps, here's what the final API permissions table should look like in Azure Portal:

| API | Permissions | Type |
|---|---|---|
| **Microsoft Graph** | `User.Read`, `openid`, `profile`, `email`, `offline_access` | Delegated |
| **Azure Service Management** | `user_impersonation` | Delegated |
| **Azure Service Bus** | `user_impersonation` | Delegated |
| | **Status: All should show ✓ Granted** | |

---

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

The goal is to add three groups of permissions so ServiceHub can:
- Read your user profile and sign you in (Microsoft Graph)
- List your Service Bus namespaces (Azure Service Management)
- Connect to Service Bus namespaces (Azure Service Bus)

**Detailed steps:**

#### 2a. Go to API Permissions page

In your App Registration (from Step 1) → left sidebar → **API permissions**

You should see a page with a table showing existing permissions. There's typically `User.Read` from Microsoft Graph already listed.

#### 2b. Add permissions button

Click **+ Add a permission** button (top left of the permissions table).

A right panel will slide out with a list of APIs.

#### 2c. Add Microsoft Graph permissions

1. In the right panel, find and click **Microsoft Graph**
2. Select **Delegated permissions** (radio button — usually already selected)
3. Search and add these permissions by checking their checkboxes:
   - ✅ `User.Read` (likely already added)
   - ✅ `openid`
   - ✅ `profile`
   - ✅ `email`
   - ✅ `offline_access`
4. Click **Add permissions** button at the bottom

You're now back on the API permissions page. You should see Microsoft Graph listed with the 5 permissions you just added.

#### 2d. Add Azure Service Management API permissions

1. Click **+ Add a permission** again
2. Search box appears — type: **Azure Service Management**
3. Click on **Azure Service Management** in the results
4. Select **Delegated permissions** (radio button)
5. Search for and check: ✅ **user_impersonation**
6. Click **Add permissions**

Back on the API permissions page, you should now see both Microsoft Graph and Azure Service Management listed.

#### 2e. Add Azure Service Bus API permissions

1. Click **+ Add a permission** one more time
2. Search box appears — type: **Azure Service Bus**
3. Click on **Azure Service Bus** in the results (if you see multiple results, pick the one owned by Microsoft)
4. Select **Delegated permissions** (radio button)
5. Search for and check: ✅ **user_impersonation**
6. Click **Add permissions**

You should now see three APIs listed: Microsoft Graph, Azure Service Management, and Azure Service Bus.

#### 2f. Grant admin consent

> **You need Global Admin role or Application Administrator role in your Azure tenant to complete this step.**

At the top of the **API permissions** page, find the button labeled:
**✓ Grant admin consent for Default Directory** (or your tenant name)

Click this button. Azure will ask for confirmation. Confirm it.

Once complete, you should see all permissions show **Status: Granted** (green checkmark) instead of "Needs admin consent".

**Summary view:**
```
┌─────────────────────────────────────────────────────────┐
│ API Permissions                                         │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ✓ Grant admin consent for Default Directory            │
│                                                         │
│  API / Permissions name        Type        Status       │
│  ─────────────────────────────────────────────────      │
│  Microsoft Graph (3)                                    │
│    • User.Read                 Delegated   ✓ Granted   │
│    • openid                    Delegated   ✓ Granted   │
│    • profile                   Delegated   ✓ Granted   │
│    + 2 more...                                          │
│                                                         │
│  Azure Service Management                               │
│    • user_impersonation        Delegated   ✓ Granted   │
│                                                         │
│  Azure Service Bus                                      │
│    • user_impersonation        Delegated   ✓ Granted   │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

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

> ⚠️ **First check:** Does your Connect page show an amber warning "Microsoft sign-in is not available on this instance"?
> If yes, OAuth has not been enabled by your administrator — see [State 1](#state-1--microsoft-sign-in-not-enabled-amber-warning) above.
> You can still connect using a Connection String.

If Microsoft sign-in **is** enabled on your instance, follow these steps:

1. Go to the ServiceHub **Connect** page → **Azure Entra ID** tab (default)
2. Click **Sign in with Microsoft** (see [State 2](#state-2--microsoft-sign-in-enabled-not-yet-signed-in) for what this looks like)
3. Microsoft's login page opens — enter your work/school email and password normally
4. You may be asked to consent to ServiceHub accessing your Service Bus namespaces on your behalf (one-time)
5. You are redirected back to ServiceHub, signed in (see [State 3](#state-3--signed-in--pick-a-namespace))
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

### Step 2 — API Permissions Setup (Common Issues)

#### "I can't find Azure Service Management in the API list"
1. Click **+ Add a permission**
2. In the search box that appears, type exactly: **Azure Service Management**
3. Look for the result owned by **Microsoft** (not a third-party app)
4. Click on it
5. Select **Delegated permissions**
6. Check ✅ **user_impersonation**
7. Click **Add permissions**

#### "I can't find Azure Service Bus in the API list"
1. Click **+ Add a permission**
2. In the search box, type: **Azure Service Bus**
3. Look for results — you want the one that says "Azure Service Bus" as the title owned by **Microsoft**
4. Click on it
5. Select **Delegated permissions**
6. Check ✅ **user_impersonation**
7. Click **Add permissions**

#### "I don't see a 'Grant admin consent' button"
The button is at the **top of the API permissions page**, not at the bottom.

1. Go back to **API permissions** in your App Registration
2. Look at the **top left** corner of the page — you should see a blue button labeled:
   - **✓ Grant admin consent for Default Directory** (or your tenant name)
3. Click it
4. Azure will ask for confirmation — confirm it
5. Wait 30 seconds, then refresh the page
6. Check that all permissions now show **Status: ✓ Granted**

> **Note:** You must have **Global Admin** or **Application Administrator** role in your Azure AD tenant to grant consent.

#### "I granted consent but the permissions still show 'Needs consent'"
1. Refresh the page (Ctrl+R / Cmd+R)
2. If permissions still show orange/yellow status, wait 1-2 minutes and refresh again
3. If they still don't show ✓ Granted after 2 minutes, try clicking **Grant admin consent** again

#### "It says permission was added, but I don't see it in the table"
The permissions table sometimes needs a refresh to show the newly added APIs.
1. Press **Refresh** button or Ctrl+R (Cmd+R on Mac)
2. Scroll to the bottom of the table — new APIs may appear there

---

### "Microsoft sign-in is not available on this instance" (amber warning, no sign-in button)
This is the most common source of confusion. It means the administrator has not configured OAuth.
**For end users:** Click "Use Connection String instead →" to connect immediately. Optionally contact your admin.
**For administrators:** Set the five `AzureOAuth__*` environment variables and restart the API ([setup guide](#step-4--configure-servicehub)).

### "Azure OAuth sign-in is not configured on this ServiceHub instance" (API error)
→ The `AzureOAuth__Enabled=true` environment variable is not set or the ClientId/ClientSecret are empty.

### "AADSTS50011: The redirect URI specified in the request does not match"
→ The `AzureOAuth__RedirectUri` value must exactly match one of the Redirect URIs registered in your App Registration.

### "No namespaces found in your subscriptions"
→ You have no Service Bus namespaces, or you don't have an RBAC role on any of them. Check Access Control (IAM) on each namespace.

### Session cookie not set / signed in but redirected to sign-in again
→ Check that the API and frontend are on the same site (same domain or CORS is configured correctly). The session cookie uses `SameSite=Lax`.
