# ServiceHub — Azure Permissions Guide

ServiceHub requires appropriate permissions to access your Azure Service Bus resources. This guide explains the permissions needed for different features.

---

## Recommended Setup: Shared Access Policy

**For full ServiceHub functionality, create a dedicated Shared Access Policy with:**
- ✅ **Manage** permission
- ✅ **Send** permission  
- ✅ **Listen** permission

**How to create:**
1. Go to Azure Portal → Your Service Bus Namespace
2. Navigate to **Shared Access Policies**
3. Click **+ Add**
4. Name: `ServiceHub-FullAccess` (or your preferred name)
5. Check: ✅ **Manage**, ✅ **Send**, ✅ **Listen**
6. Click **Create**
7. Copy the **Primary Connection String**

⚠️ **Do NOT use RootManageSharedAccessKey** — Always create a dedicated policy for ServiceHub.

---

## Alternative: Limited Permissions (Read-Only)

If you only need to browse messages without replay or testing capabilities:

**Required Role (using Azure RBAC):**
- `Azure Service Bus Data Receiver`

**Or create a Shared Access Policy with:**
- ✅ **Listen** permission only

**Permissions Granted:**
- ✅ Peek/browse messages from queues and subscriptions
- ✅ View message metadata, properties, and bodies
- ✅ View queue and topic metrics
- ❌ Cannot replay messages from DLQ
- ❌ Cannot create test DLQ messages

---

## Feature-Specific Requirements

### 🔍 Read-Only Investigation

**Permissions Required:**
- Listen (peek messages)

**What You Can Do:**
- Browse active and dead-letter queue messages
- View message details and properties
- Search and filter messages
- View queue/topic metrics

### 🔄 Replay Messages from DLQ

**Permissions Required:**
- Listen (read from DLQ)
- Send (write to active queue)

**What You Can Do:**
- All read-only features
- Move messages from DLQ back to main queue

### 🧪 Create Test DLQ Messages

**Permissions Required:**
- Listen (read from queue)
- Send (move messages to DLQ)

**What You Can Do:**
- All read-only features
- Manually dead-letter messages for testing

### 🛠️ Full Management

**Permissions Required:**
- Manage (full control)

**What You Can Do:**
- All features above
- Future management operations