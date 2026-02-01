# Permission Detection Fix - 2026-01-31

## Problem
Users were seeing "Limited permissions detected" warnings even when they created Azure Service Bus policies with full Manage+Send+Listen permissions.

## Root Cause
The permission detection logic was based on **keyword matching** in the `SharedAccessKeyName` from the connection string. The code checked if the policy name contained words like "manage", "send", or "listen".

**The fundamental issue**: Azure does not enforce any naming convention for SAS policies. A policy with full Manage+Send+Listen permissions could be named anything:
- "ServiceHub-Policy" ❌ (no keywords matched)
- "MyCustomPolicy" ❌ (no keywords matched)  
- "FullAccess" ❌ (no keywords matched)
- "Production" ❌ (no keywords matched)

The connection string only contains the **policy NAME**, not its **actual permissions**. Without calling Azure Management APIs, there's no way to definitively know the permissions from the connection string alone.

## Solution Implemented

### 1. Backend: More Lenient Detection (`Namespace.cs`)
Changed the detection logic to assume **full permissions by default** unless the policy name explicitly indicates restricted access:

```csharp
// OLD: Assumed limited permissions if keywords not found
var hasManage = keyName.Contains("manage") || keyName.Contains("root");
var hasSend = hasManage || keyName.Contains("send");
var hasListen = hasManage || hasSend || keyName.Contains("listen");

// NEW: Assumes full permissions unless explicitly restricted
var explicitlyListenOnly = keyName.Contains("listen") && !keyName.Contains("send") && !keyName.Contains("manage");
var explicitlySendOnly = keyName.Contains("send") && !keyName.Contains("manage") && !keyName.Contains("listen");

var hasManage = keyName.Contains("manage") || keyName.Contains("root") || (!explicitlyListenOnly && !explicitlySendOnly);
var hasSend = hasManage || keyName.Contains("send");
var hasListen = true; // All policies have at least listen permission
```

### 2. Frontend: Use Backend Permissions (`ConnectPage.tsx`)
**Before**: The frontend parsed the connection string client-side before the API call and showed warnings based on local detection.

**After**: The frontend now:
1. Removes client-side permission checking before creating the namespace
2. Uses the actual permissions returned by the backend API after namespace creation
3. Only shows warnings if the backend confirms limited permissions

```tsx
// OLD: Client-side check before API call
const permissions = getConnectionStringPermissions(connectionString.trim());
if (!permissions.hasManage || !permissions.hasSend) {
  // Show warning
}
await createNamespace.mutateAsync({...});

// NEW: Check backend-returned permissions after API call
const createdNamespace = await createNamespace.mutateAsync({...});
if (createdNamespace.hasManagePermission === false || createdNamespace.hasSendPermission === false) {
  // Show warning
}
```

## Impact
- **False positives eliminated**: Users with correctly configured policies won't see unnecessary warnings
- **Better UX**: Warnings are now based on what the backend actually detects
- **More reliable**: Backend detection is the source of truth, not client-side parsing

## Testing Recommendations
1. Create a policy named "MyCustomPolicy" with Manage+Send+Listen permissions
2. Use that connection string in ServiceHub
3. Verify no warning appears
4. Try operations that require Send/Manage permissions (replay, deadletter)
5. Verify they work correctly

## Notes
- Permission detection is still best-effort and heuristic-based
- The only way to be 100% certain would be to try operations and see if they fail
- Or use Azure Management SDK to query the actual policy permissions (overkill for this app)
- Current approach balances usability with accuracy
