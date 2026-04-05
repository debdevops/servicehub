# Azure Entra ID OAuth — Setup Checklist for Administrators

Use this checklist to set up ServiceHub OAuth 2.0. Estimated time: **10 minutes**.

> **Requirement:** You must have **Global Admin** or **Application Administrator** role in your Azure AD tenant.

---

## Step 1 — Register ServiceHub App

> **Location:** [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID** → **App registrations**

- [ ] Click **+ New registration**
- [ ] Name: `ServiceHub` (or your preferred name)
- [ ] Supported accounts: Choose your scenario:
  - [ ] Single-tenant: *Accounts in this organizational directory only*
  - [ ] Multi-tenant: *Accounts in any organizational directory*
- [ ] Redirect URI: Select `Web`, then enter:
  - Production: `https://YOUR_SERVICEHUB_DOMAIN/api/v1/auth/azure/callback`
  - Local dev: `http://localhost:5153/api/v1/auth/azure/callback`
- [ ] Click **Register**
- [ ] **Copy and save these values:**
  - Application (client) ID: `_______________________________`
  - Directory (tenant) ID: `_______________________________`

---

## Step 2 — Add API Permissions

> **Location:** Your App Registration → **API permissions**

### Add Microsoft Graph Permissions

- [ ] Click **+ Add a permission**
- [ ] Click **Microsoft Graph**
- [ ] Select **Delegated permissions** (radio button)
- [ ] Check these permissions:
  - [ ] `User.Read`
  - [ ] `openid`
  - [ ] `profile`
  - [ ] `email`
  - [ ] `offline_access`
- [ ] Click **Add permissions**

### Add Azure Service Management Permissions

- [ ] Click **+ Add a permission**
- [ ] **IMPORTANT:** Click on the **"Microsoft APIs"** tab at the top of the panel
- [ ] Search for: `Azure Service Management`
- [ ] Click on it (owned by Microsoft)
- [ ] Select **Delegated permissions** (radio button)
- [ ] Check:
  - [ ] `user_impersonation`
- [ ] Click **Add permissions**

### Add Azure Service Bus Permissions — NOT REQUIRED

⚠️ **Azure Service Bus is not available in the API permissions list — and you don't need to add it.**

ServiceHub requests the `servicebus.azure.com/user_impersonation` scope at runtime when users sign in. This is handled automatically and does not require explicit API permission configuration.

**You are done with Step 2!** You have configured:
- ✅ Microsoft Graph (5 permissions)
- ✅ Azure Service Management (user_impersonation)

Proceed to **Step 3 — Create Client Secret** below.

### Grant Admin Consent

- [ ] At the top of the **API permissions** page, click:
  - **✓ Grant admin consent for Default Directory** (or your tenant name)
- [ ] Confirm when prompted
- [ ] Wait 30 seconds and refresh the page
- [ ] Verify all permissions show **Status: ✓ Granted** (green checkmark)

---

## Step 3 — Create Client Secret

> **Location:** Your App Registration → **Certificates & secrets**

- [ ] Click **+ New client secret**
- [ ] Description: `ServiceHub-OAuth`
- [ ] Expires: `24 months` (set a calendar reminder to rotate before expiry)
- [ ] Click **Add**
- [ ] **COPY THE SECRET VALUE IMMEDIATELY** — it won't be shown again!
- [ ] Secret Value: `_______________________________`

---

## Step 4 — Configure ServiceHub API

Set these five environment variables on your ServiceHub API server:

```
AzureOAuth__Enabled=true
AzureOAuth__ClientId=<from Step 1, Application (client) ID>
AzureOAuth__ClientSecret=<from Step 3, Client Secret Value>
AzureOAuth__RedirectUri=https://YOUR_SERVICEHUB_DOMAIN/api/v1/auth/azure/callback
AzureOAuth__FrontendBaseUrl=https://YOUR_SERVICEHUB_DOMAIN
```

**For Azure App Service:**
1. Azure Portal → Your App Service → **Settings** → **Configuration**
2. Click **+ New application setting** for each environment variable above
3. Click **Save** (this restarts the app)

**For Docker/Local:**
Add to your `.env` file or pass via container environment variables.

**Verify:**
- Restart the ServiceHub API
- Go to ServiceHub UI → Connect page → **Azure Entra ID** tab
- You should see the **"Sign in with Microsoft"** button (not the amber warning)

---

## Step 5 — Grant Users Access to Namespaces

For each Service Bus namespace users need to access:

> **Location:** [Azure Portal](https://portal.azure.com) → Your Service Bus namespace → **Access Control (IAM)**

- [ ] Click **+ Add** → **Add role assignment**
- [ ] Role: Select one:
  - [ ] `Azure Service Bus Data Owner` (full read/write/delete access)
  - [ ] `Azure Service Bus Data Receiver` (read-only access)
  - [ ] `Azure Service Bus Data Sender` (write-only access)
- [ ] Assign to: Your user account or an **Azure AD group**
- [ ] Click **Review + assign**

> **Best practice:** Assign roles to **Azure AD groups** rather than individual users for easier management.

---

## Troubleshooting

| Issue | Solution |
|---|---|
| "Microsoft sign-in is not available" | Did you set `AzureOAuth__Enabled=true` and restart the API? Check all 5 env vars are set. |
| Can't find Azure Service Management | Search for **Azure Service Management** (not "Azure Management"). Make sure it's owned by Microsoft. |
| Can't find Azure Service Bus | Search for **Azure Service Bus**. Select the one owned by Microsoft (not third-party). |
| "Grant admin consent" button missing | It's at the **top left** of the **API permissions** page, not bottom. You need Global Admin role. |
| Users see no namespaces | They don't have RBAC access. Add them to the Service Bus namespace via Access Control (IAM). |
| Button still says "Microsoft sign-in is not available" after setup | Clear browser cookies, restart API, refresh page. Then wait 1 minute. |

---

## Final Checklist — Everything Done?

- [ ] ✅ App registered in Azure AD
- [ ] ✅ Client ID and Tenant ID saved
- [ ] ✅ 3 API permissions added (Microsoft Graph, Azure Service Management, Azure Service Bus)
- [ ] ✅ Admin consent granted (all 3 APIs show ✓ Granted)
- [ ] ✅ Client secret created and saved
- [ ] ✅ 5 environment variables set on ServiceHub API
- [ ] ✅ ServiceHub API restarted
- [ ] ✅ "Sign in with Microsoft" button visible on Connect page
- [ ] ✅ Test users added to at least one Service Bus namespace

**That's it! Users can now sign in with their Microsoft account.**

---

## Next Steps

- **User Guide:** [OAuth README — For End Users](README.md#for-end-users-what-to-expect)
- **Full Technical Details:** [OAuth README — Full Guide](README.md)
- **Support/Issues:** For questions, file a GitHub issue: [servicehub/issues](https://github.com/debdevops/servicehub/issues)
