import { useState, useEffect } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Eye, EyeOff, Trash2, Github, Play, Star, Shield, ArrowRight, Lock, CheckCircle2, ExternalLink, Loader2, LogOut } from 'lucide-react';
import { useNamespaces, useCreateNamespace, useDeleteNamespace, useEntraIdStatus } from '@/hooks/useNamespaces';
import { useAzureAuthStatus, useAzureNamespaces, useAzureSignIn, useAzureSignOut } from '@/hooks/useAzureAuth';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { HelpTooltip } from '@/components/help';
import { tooltips } from '@/lib/helpContent';
import type { EnvironmentType } from '@/lib/api/types';
import toast from 'react-hot-toast';

/**
 * Connection Setup Page
 * 
 * Onboarding-style screen for connecting to Azure Service Bus namespaces.
 */
export function ConnectPage() {
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState<'connectionstring' | 'entra'>('entra');
  const [showPassword, setShowPassword] = useState(false);
  const [displayName, setDisplayName] = useState('');
  const [connectionString, setConnectionString] = useState('');
  const [entraHostname, setEntraHostname] = useState('');
  const [environment, setEnvironment] = useState<EnvironmentType>('Dev');
  
  // Delete confirmation dialog state
  const [deleteConfirm, setDeleteConfirm] = useState<{ isOpen: boolean; id: string; name: string }>({
    isOpen: false,
    id: '',
    name: '',
  });

  const { data: namespaces, isLoading } = useNamespaces();
  const { data: entraIdStatus } = useEntraIdStatus();
  const createNamespace = useCreateNamespace();
  const deleteNamespace = useDeleteNamespace();

  // Handle OAuth callback redirect (Azure returns ?tab=entra&auth=success|error&msg=...)
  const [searchParams, setSearchParams] = useSearchParams();
  useEffect(() => {
    const auth = searchParams.get('auth');
    if (auth === 'success') {
      toast.success('Signed in with Azure successfully!');
      setActiveTab('entra');
      setSearchParams({}, { replace: true });
    } else if (auth === 'error') {
      const msg = searchParams.get('msg') ?? 'Azure sign-in failed';
      toast.error(msg);
      setSearchParams({}, { replace: true });
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Azure OAuth 2.0 (user-delegated) flow
  const { data: azureAuthStatus, isLoading: azureStatusLoading } = useAzureAuthStatus();
  const { data: azureNamespaces, isLoading: azureNamespacesLoading } = useAzureNamespaces(
    azureAuthStatus?.isSignedIn ?? false
  );
  const azureSignIn = useAzureSignIn();
  const azureSignOut = useAzureSignOut();
  const [selectedFqns, setSelectedFqns] = useState('');
  const [oauthDisplayName, setOauthDisplayName] = useState('');
  const [oauthEnvironment, setOauthEnvironment] = useState<EnvironmentType>('Dev');

  const extractNamespaceFromConnectionString = (connString: string): string | null => {
    try {
      // Extract namespace from connection string Endpoint
      // Format: Endpoint=sb://namespace-name.servicebus.windows.net/;...
      const endpointMatch = connString.match(/Endpoint=sb:\/\/([^.]+)\.servicebus\./i);
      if (endpointMatch && endpointMatch[1]) {
        return endpointMatch[1];
      }
      return null;
    } catch {
      return null;
    }
  };

  const handleConnect = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!displayName.trim() || !connectionString.trim()) {
      return;
    }

    // SECURITY WARNING: RootManageSharedAccessKey has full namespace access.
    // Warn the user but do not block — it still works for debugging.
    if (connectionString.includes('RootManageSharedAccessKey')) {
      toast(
        '⚠️ You are using RootManageSharedAccessKey which has full namespace access. ' +
        'Consider creating a dedicated "servicehub" policy with Listen-only permission for better security. ' +
        'Connecting anyway...',
        {
          duration: 8000,
          style: {
            background: '#fef3c7',
            color: '#92400e',
            border: '1px solid #fbbf24',
          },
          icon: '⚠️',
        }
      );
      // Do NOT return — allow the connection to proceed
    }

    // Extract namespace from connection string
    const namespaceName = extractNamespaceFromConnectionString(connectionString.trim());
    
    if (!namespaceName) {
      toast.error('Could not extract namespace from connection string. Please verify the format.', {
        duration: 5000,
      });
      return;
    }

    try {
      const createdNamespace = await createNamespace.mutateAsync({
        name: namespaceName,
        connectionString: connectionString.trim(),
        authType: 'ConnectionString',
        displayName: displayName.trim(),
        environment,
      });
      
      // Check actual permissions returned by the backend
      // The backend attempts to detect permissions from the SAS policy name, but this is not always accurate
      // since Azure doesn't enforce naming conventions. We show a warning if permissions appear limited.
      if (createdNamespace.hasManagePermission === false && createdNamespace.hasSendPermission === false) {
        // Listen-only — read-only inspection mode
        toast.success(
          '✓ Connected with Listen-only access. Perfect for DLQ inspection and message browsing. ' +
          'Quick Actions (FAB) require a Manage policy for send, generate, and dead-letter operations.',
          { duration: 8000 }
        );
      } else if (createdNamespace.hasManagePermission === false) {
        // Has Listen + Send but not Manage
        toast(
          '✓ Connected with Send + Listen access. Replay and send operations are available. ' +
          'Some Quick Actions may require Manage permission.',
          {
            duration: 6000,
            style: { background: '#f0fdf4', color: '#166534', border: '1px solid #86efac' },
          }
        );
      }
      // If all permissions detected or permissions unclear, show no additional message — the success toast from the mutation is enough
      
      // Reset form
      setDisplayName('');
      setConnectionString('');
      setShowPassword(false);
      setEnvironment('Dev');
      setEntraHostname('');
    } catch {
      // Error handled by mutation hook
    }
  };

  const handleEntraConnect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayName.trim() || !entraHostname.trim()) return;

    // Use DefaultAzureCredential for local-dev mode (no app registration credentials),
    // otherwise use ServicePrincipal (full App Registration configured).
    const authType = entraIdStatus?.isDefaultCredentialMode ? 'DefaultAzureCredential' : 'ServicePrincipal';

    try {
      await createNamespace.mutateAsync({
        name: entraHostname.trim(),
        authType,
        displayName: displayName.trim(),
        environment,
      });

      setDisplayName('');
      setEntraHostname('');
      setEnvironment('Dev');
    } catch {
      // Error handled by mutation hook
    }
  };

  const handleOAuthConnect = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFqns || !oauthDisplayName.trim()) return;
    try {
      // Find the full namespace info so we can pass subscriptionId + resourceGroup.
      // These are used by the backend to retrieve the SAS connection string via ARM listKeys,
      // avoiding the need for the https://servicebus.azure.com enterprise app in the tenant.
      const nsInfo = azureNamespaces?.find(n => n.fullyQualifiedHostname === selectedFqns);
      await createNamespace.mutateAsync({
        name: selectedFqns,
        authType: 'UserDelegated',
        displayName: oauthDisplayName.trim(),
        environment: oauthEnvironment,
        subscriptionId: nsInfo?.subscriptionId,
        resourceGroup: nsInfo?.resourceGroup,
      });
      setOauthDisplayName('');
      setSelectedFqns('');
      setOauthEnvironment('Dev');
      toast.success('Namespace connected via Azure Entra ID');
    } catch {
      // Error handled by mutation hook
    }
  };

  const openDeleteConfirm = (id: string, name: string) => {
    setDeleteConfirm({ isOpen: true, id, name });
  };

  const handleDeleteConfirm = async () => {
    await deleteNamespace.mutateAsync(deleteConfirm.id);
    setDeleteConfirm({ isOpen: false, id: '', name: '' });
  };

  const handleDeleteCancel = () => {
    setDeleteConfirm({ isOpen: false, id: '', name: '' });
  };


  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-screen">
        <div className="text-center">
          <div className="w-12 h-12 border-4 border-primary-200 border-t-primary-600 rounded-full animate-spin mx-auto mb-4"></div>
          <p className="text-gray-600">Loading connections...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-auto bg-gradient-to-b from-white to-gray-50 p-6 md:p-8">
      <div className="max-w-5xl mx-auto">

        {/* ══════════════════════════════════════════════════════════════
            HEADER — compact tagline
        ══════════════════════════════════════════════════════════════ */}
        <div className="mb-6">
          <div className="flex items-center gap-2 mb-2">
            <span className="w-1.5 h-1.5 rounded-full bg-sky-500 animate-pulse" />
            <span className="text-xs font-semibold text-sky-700">Free · Open Source · No installation</span>
          </div>
          <h1 className="text-2xl font-bold text-gray-900 leading-tight">
            Debug Azure Service Bus{' '}
            <span className="text-primary-600">in seconds.</span>
          </h1>
          <p className="text-sm text-gray-500 mt-1">
            Browse messages, pinpoint DLQ failures, replay dead-lettered events — all from your browser.
          </p>
        </div>

        {/* ══════════════════════════════════════════════════════════════
            CONNECT FORM + SAVED CONNECTIONS  (primary action — above fold)
        ══════════════════════════════════════════════════════════════ */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start mb-6">
          {/* Left: Connect Form */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-6">
            <div className="flex items-center gap-3 mb-4">
              <div className="w-9 h-9 bg-primary-50 border border-primary-100 rounded-lg flex items-center justify-center">
                <span className="text-base">☁️</span>
              </div>
              <div>
                <h2 className="text-base font-semibold text-gray-900">Connect to Service Bus</h2>
                <p className="text-xs text-gray-500">Choose your authentication method</p>
              </div>
            </div>

            {/* Tab switcher */}
            <div className="flex gap-1 mb-5 bg-gray-100 p-1 rounded-lg">
              <button
                type="button"
                onClick={() => setActiveTab('connectionstring')}
                className={`flex-1 px-3 py-2 text-xs font-semibold rounded-md transition-colors ${
                  activeTab === 'connectionstring'
                    ? 'bg-white text-gray-900 shadow-sm'
                    : 'text-gray-500 hover:text-gray-700'
                }`}
              >
                Connection String
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('entra')}
                className={`flex-1 px-3 py-2 text-xs font-semibold rounded-md transition-colors ${
                  activeTab === 'entra'
                    ? 'bg-white text-gray-900 shadow-sm'
                    : 'text-gray-500 hover:text-gray-700'
                }`}
              >
              Azure Entra ID
              </button>
            </div>

            {activeTab === 'connectionstring' ? (
              <>
                {/* Trust & Security panel */}
                <div className="mb-4 rounded-lg bg-green-50 border border-green-100 p-3">
                  <div className="flex items-center gap-2 mb-2">
                    <Shield className="w-4 h-4 text-green-600 flex-shrink-0" />
                    <span className="text-xs font-semibold text-green-800">Your data stays yours</span>
                  </div>
                  <div className="space-y-1">
                    {[
                      { label: 'Connection string', value: 'AES-256-GCM encrypted before saving — never returned to browser' },
                      { label: 'Message content', value: 'Never logged or stored by ServiceHub' },
                      { label: 'Your encryption key', value: 'Lives only in your own Azure App Service config' },
                    ].map(({ label, value }) => (
                      <div key={label} className="flex items-start gap-2 text-xs">
                        <span className="text-green-500 mt-0.5 flex-shrink-0">✓</span>
                        <span><span className="font-medium text-green-800">{label}:</span>{' '}
                          <span className="text-green-700">{value}</span>
                        </span>
                      </div>
                    ))}
                  </div>
                  <div className="flex items-center gap-3 mt-2 pt-2 border-t border-green-100">
                    <a
                      href="https://github.com/debdevops/servicehub/blob/main/services/api/src/ServiceHub.Infrastructure/Security/ConnectionStringProtector.cs"
                      target="_blank" rel="noopener noreferrer"
                      className="text-xs text-green-700 hover:text-green-900 underline underline-offset-2"
                    >
                      Verify encryption code →
                    </a>
                    <Link
                      to="/security"
                      className="text-xs text-green-700 hover:text-green-900 underline underline-offset-2"
                    >
                      Security overview →
                    </Link>
                  </div>
                </div>

                <form onSubmit={handleConnect}>
                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Display Name <span className="text-red-500">*</span>
                      <HelpTooltip {...tooltips.connect.displayName} position="right" className="ml-1" />
                    </label>
                    <input
                      type="text"
                      value={displayName}
                      onChange={(e) => setDisplayName(e.target.value)}
                      placeholder="e.g., Production Service Bus"
                      required
                      className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                    />
                  </div>

                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Connection String <span className="text-red-500">*</span>
                      <HelpTooltip {...tooltips.connect.connectionString} position="right" className="ml-1" />
                    </label>
                    <div className="relative">
                      <input
                        type={showPassword ? 'text' : 'password'}
                        value={connectionString}
                        onChange={(e) => setConnectionString(e.target.value)}
                        placeholder="Endpoint=sb://...;SharedAccessKey=..."
                        required
                        className="w-full px-3 py-2 pr-10 rounded-lg text-sm font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                      />
                      <button
                        type="button"
                        onClick={() => setShowPassword(!showPassword)}
                        className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                      >
                        {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                      </button>
                    </div>
                    <div className="flex items-center gap-1 mt-1.5 text-xs text-green-700">
                      <Shield className="w-3 h-3 text-green-600 shrink-0" />
                      AES-GCM encrypted at rest — never stored in plaintext.
                    </div>
                  </div>

                  <div className="mb-4">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Environment <span className="text-red-500">*</span>
                      <HelpTooltip {...tooltips.connect.environment} position="right" className="ml-1" />
                    </label>
                    <select
                      value={environment}
                      onChange={(e) => setEnvironment(e.target.value as EnvironmentType)}
                      className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                    >
                      <option value="Dev">DEV — Development</option>
                      <option value="Uat">UAT — User Acceptance Testing</option>
                      <option value="Prod">PROD — Production</option>
                    </select>
                    <p className="text-xs text-gray-500 mt-1">Production disables Quick Actions for safety.</p>
                  </div>

                  <button
                    type="submit"
                    disabled={createNamespace.isPending}
                    className="w-full px-4 py-2.5 rounded-lg font-medium transition-colors flex items-center justify-center gap-2 text-white bg-primary-500 hover:bg-primary-600 disabled:bg-primary-300"
                  >
                    {createNamespace.isPending ? (
                      <>
                        <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                        Connecting...
                      </>
                    ) : (
                      <>Connect</>
                    )}
                  </button>
                </form>

                {/* SAS instructions — always visible */}
                <div className="mt-3 rounded-r-lg border-l-2 border-blue-300 bg-blue-50 pl-3 pr-2 py-2">
                  <p className="text-xs font-semibold text-blue-800 mb-1">
                    💡 A Listen-only key is all you need — and it's the safest option
                  </p>
                  <p className="text-xs text-blue-700 mb-1">
                    A Listen-only policy can <strong>only read</strong> messages. It cannot delete, send, or
                    modify anything. Even if this key were ever exposed, your data remains safe.
                  </p>
                  <ol className="text-xs text-blue-700 space-y-0.5 list-decimal list-inside">
                    <li>Azure Portal → your Service Bus namespace</li>
                    <li>Shared access policies → + Add policy</li>
                    <li>Name it <code className="bg-blue-100 px-1 rounded">servicehub</code>, tick <strong>Listen only</strong></li>
                    <li>Save → copy Primary Connection String → paste above</li>
                  </ol>
                </div>
              </>
            ) : (
              <>
                {/* ── Azure Entra ID tab ── */}
                {azureStatusLoading ? (
                  <div className="flex items-center justify-center py-8">
                    <Loader2 className="w-5 h-5 animate-spin text-primary-500" />
                  </div>

                ) : azureAuthStatus?.isConfigured ? (
                  /* ════ OAUTH MODE (Azure OAuth 2.0 configured) ════ */
                  azureAuthStatus.isSignedIn ? (
                    /* ── Signed in: namespace picker ── */
                    <>
                      {/* Signed-in banner */}
                      <div className="mb-4 rounded-lg bg-green-50 border border-green-200 p-3">
                        <div className="flex items-center justify-between gap-2">
                          <div className="flex items-center gap-2">
                            <CheckCircle2 className="w-4 h-4 text-green-600 flex-shrink-0" />
                            <div>
                              <p className="text-xs font-semibold text-green-800">Signed in with Azure</p>
                              <p className="text-xs text-green-700 truncate max-w-[200px]">
                                {azureAuthStatus.userPrincipalName}
                              </p>
                            </div>
                          </div>
                          <button
                            type="button"
                            onClick={() => azureSignOut.mutate()}
                            disabled={azureSignOut.isPending}
                            className="flex items-center gap-1 text-xs text-green-700 hover:text-green-900 underline"
                          >
                            <LogOut className="w-3 h-3" />
                            Sign out
                          </button>
                        </div>
                      </div>

                      <form onSubmit={handleOAuthConnect}>
                        <div className="mb-3">
                          <label className="block text-sm font-medium text-gray-700 mb-1">
                            Display Name <span className="text-red-500">*</span>
                          </label>
                          <input
                            type="text"
                            value={oauthDisplayName}
                            onChange={(e) => setOauthDisplayName(e.target.value)}
                            placeholder="e.g., Production Service Bus"
                            required
                            className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                          />
                        </div>

                        <div className="mb-3">
                          <div className="flex items-center justify-between mb-1">
                            <label className="block text-sm font-medium text-gray-700">
                              Select Namespace <span className="text-red-500">*</span>
                            </label>
                            {azureNamespacesLoading && (
                              <span className="flex items-center gap-1 text-xs text-gray-400">
                                <Loader2 className="w-3 h-3 animate-spin" /> Loading…
                              </span>
                            )}
                          </div>
                          <select
                            value={selectedFqns}
                            onChange={(e) => setSelectedFqns(e.target.value)}
                            required
                            disabled={azureNamespacesLoading}
                            className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300 disabled:bg-gray-50"
                          >
                            <option value="">— Choose a namespace —</option>
                            {(azureNamespaces ?? []).map((ns) => (
                              <option key={ns.fullyQualifiedHostname} value={ns.fullyQualifiedHostname}>
                                {ns.name} ({ns.location} · {ns.sku})
                              </option>
                            ))}
                          </select>
                          {!azureNamespacesLoading && (azureNamespaces?.length ?? 0) === 0 && (
                            <p className="text-xs text-amber-700 mt-1">
                              No Service Bus namespaces found in your subscriptions.
                              Ensure you have at least one namespace and the "Azure Service Bus Data Reader/Owner" role.
                            </p>
                          )}
                        </div>

                        <div className="mb-4">
                          <label className="block text-sm font-medium text-gray-700 mb-1">
                            Environment <span className="text-red-500">*</span>
                          </label>
                          <select
                            value={oauthEnvironment}
                            onChange={(e) => setOauthEnvironment(e.target.value as EnvironmentType)}
                            className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                          >
                            <option value="Dev">DEV — Development</option>
                            <option value="Uat">UAT — User Acceptance Testing</option>
                            <option value="Prod">PROD — Production</option>
                          </select>
                          <p className="text-xs text-gray-500 mt-1">Production disables Quick Actions for safety.</p>
                        </div>

                        <button
                          type="submit"
                          disabled={!selectedFqns || !oauthDisplayName.trim() || createNamespace.isPending}
                          className="w-full px-4 py-2.5 rounded-lg font-medium transition-colors flex items-center justify-center gap-2 text-white bg-primary-500 hover:bg-primary-600 disabled:bg-primary-300 disabled:cursor-not-allowed"
                        >
                          {createNamespace.isPending ? (
                            <><Loader2 className="w-4 h-4 animate-spin" /> Connecting…</>
                          ) : (
                            <>Connect</>
                          )}
                        </button>
                      </form>

                      {/* Security note */}
                      <div className="mt-3 rounded-r-lg border-l-2 border-blue-300 bg-blue-50 pl-3 pr-2 py-2">
                        <p className="text-xs font-semibold text-blue-800 mb-1">
                          🔒 Zero-secret connection — how it works
                        </p>
                        <p className="text-xs text-blue-700">
                          No connection strings or SAS keys are ever typed or stored. ServiceHub connects using
                          your own Azure identity. Your RBAC roles on the namespace determine what you can see —
                          nothing more.
                        </p>
                      </div>
                    </>
                  ) : (
                    /* ── Not signed in: sign-in prompt ── */
                    <>
                      {/* What is Azure Entra ID */}
                      <div className="mb-4 rounded-lg bg-blue-50 border border-blue-100 p-4">
                        <div className="flex items-start gap-3">
                          <div className="w-8 h-8 rounded-lg bg-[#0078d4] flex items-center justify-center flex-shrink-0">
                            <Shield className="w-4 h-4 text-white" />
                          </div>
                          <div>
                            <p className="text-xs font-semibold text-blue-900 mb-1">What is Azure Entra ID (formerly Azure AD)?</p>
                            <p className="text-xs text-blue-800 mb-2">
                              Microsoft's enterprise identity platform. When you sign in with your company Microsoft account,
                              ServiceHub receives a short-lived token scoped only to the namespaces you already
                              have permission to access — no extra configuration needed. Your password is never shared.
                            </p>
                            <div className="flex flex-wrap gap-2">
                              <a href="https://learn.microsoft.com/en-us/entra/fundamentals/whatis" target="_blank" rel="noopener noreferrer"
                                className="inline-flex items-center gap-1 text-xs text-blue-700 underline hover:text-blue-900">
                                What is Microsoft Entra ID? <ExternalLink className="w-3 h-3" />
                              </a>
                              <a href="https://learn.microsoft.com/en-us/azure/service-bus-messaging/authenticate-application" target="_blank" rel="noopener noreferrer"
                                className="inline-flex items-center gap-1 text-xs text-blue-700 underline hover:text-blue-900">
                                RBAC for Service Bus <ExternalLink className="w-3 h-3" />
                              </a>
                            </div>
                          </div>
                        </div>
                      </div>

                      {/* Security guarantees */}
                      <div className="mb-4 rounded-lg bg-green-50 border border-green-100 p-3 space-y-1.5">
                        {[
                          { label: 'No passwords typed here', detail: "You authenticate directly on Microsoft's login page" },
                          { label: 'No connection strings needed', detail: 'Your Azure RBAC roles control access automatically' },
                          { label: 'Short-lived tokens only', detail: 'Sessions expire in 8 hours; no refresh token stored client-side' },
                          { label: 'Conforms to zero-trust', detail: 'ServiceHub only receives a scoped, delegated token' },
                        ].map(({ label, detail }) => (
                          <div key={label} className="flex items-start gap-2 text-xs">
                            <CheckCircle2 className="w-3.5 h-3.5 text-green-600 flex-shrink-0 mt-0.5" />
                            <span><span className="font-semibold text-green-800">{label}:</span>{' '}
                              <span className="text-green-700">{detail}</span>
                            </span>
                          </div>
                        ))}
                      </div>

                      {/* Sign-in button (Microsoft-branded) */}
                      <button
                        type="button"
                        onClick={() => azureSignIn.mutate()}
                        disabled={azureSignIn.isPending}
                        className="w-full flex items-center justify-center gap-3 px-4 py-3 rounded-lg border border-gray-300 bg-white hover:bg-gray-50 transition-colors disabled:opacity-60 mb-3 shadow-sm"
                      >
                        {/* Microsoft logo SVG */}
                        <svg width="18" height="18" viewBox="0 0 21 21" xmlns="http://www.w3.org/2000/svg">
                          <rect x="1" y="1" width="9" height="9" fill="#f25022"/>
                          <rect x="11" y="1" width="9" height="9" fill="#7fba00"/>
                          <rect x="1" y="11" width="9" height="9" fill="#00a4ef"/>
                          <rect x="11" y="11" width="9" height="9" fill="#ffb900"/>
                        </svg>
                        <span className="text-sm font-semibold text-gray-700">
                          {azureSignIn.isPending ? 'Redirecting to Microsoft…' : 'Sign in with Microsoft'}
                        </span>
                        {azureSignIn.isPending && <Loader2 className="w-4 h-4 animate-spin text-gray-400" />}
                      </button>

                      {/* DevOps/SRE setup note */}
                      <div className="rounded-r-lg border-l-2 border-blue-300 bg-blue-50 pl-3 pr-2 py-2">
                        <p className="text-xs font-semibold text-blue-800 mb-1">
                          ⚙️ Administrator: one-time Azure setup required
                        </p>
                        <p className="text-xs text-blue-700 mb-1">
                          To enable this sign-in button your administrator must register ServiceHub as an
                          Azure app and configure these environment variables:
                        </p>
                        <code className="block text-[10px] bg-blue-100 rounded p-1.5 text-blue-900 font-mono mb-1">
                          AzureOAuth__ClientId=&lt;App Registration Client ID&gt;<br/>
                          AzureOAuth__ClientSecret=&lt;Client Secret&gt;<br/>
                          AzureOAuth__RedirectUri=https://servicehub.example.com/api/v1/auth/azure/callback<br/>
                          AzureOAuth__FrontendBaseUrl=https://servicehub.example.com<br/>
                          AzureOAuth__Enabled=true
                        </code>
                        <a href="https://github.com/debdevops/servicehub/blob/main/azure-entra-id/oauth/README.md"
                          target="_blank" rel="noopener noreferrer"
                          className="inline-flex items-center gap-1 text-xs text-blue-700 underline hover:text-blue-900">
                          Full setup guide for DevOps/SRE <ExternalLink className="w-3 h-3" />
                        </a>
                      </div>
                    </>
                  )

                ) : entraIdStatus?.isAvailable ? (
                  /* ════ CLASSIC ENTRA ID (service principal / managed identity) ════ */
                  <>
                    {/* Classic Entra ID availability banner */}
                    <div className="mb-4 rounded-lg bg-green-50 border border-green-100 p-3">
                      <div className="flex items-center gap-2">
                        <CheckCircle2 className="w-4 h-4 text-green-600 flex-shrink-0" />
                        <span className="text-xs font-semibold text-green-800">
                          {entraIdStatus.isDefaultCredentialMode
                            ? 'Azure Entra ID available (DefaultAzureCredential mode)'
                            : 'Azure Entra ID available via App Registration'}
                        </span>
                      </div>
                      <p className="text-xs text-green-700 mt-1">
                        {entraIdStatus.isDefaultCredentialMode
                          ? 'Connect using az login or Managed Identity — no connection string required.'
                          : 'Connect without a connection string. Your Azure AD admin must grant ServiceHub the "Azure Service Bus Data Owner" role on your namespace.'}
                      </p>
                    </div>

                    <form onSubmit={handleEntraConnect}>
                      <div className="mb-3">
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Display Name <span className="text-red-500">*</span>
                        </label>
                        <input
                          type="text"
                          value={displayName}
                          onChange={(e) => setDisplayName(e.target.value)}
                          placeholder="e.g., Production Service Bus"
                          required
                          className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                        />
                      </div>

                      <div className="mb-3">
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Namespace Hostname <span className="text-red-500">*</span>
                        </label>
                        <input
                          type="text"
                          value={entraHostname}
                          onChange={(e) => setEntraHostname(e.target.value)}
                          placeholder="yournamespace.servicebus.windows.net"
                          required
                          className="w-full px-3 py-2 rounded-lg text-sm font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                        />
                      </div>

                      <div className="mb-4">
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Environment <span className="text-red-500">*</span>
                        </label>
                        <select
                          value={environment}
                          onChange={(e) => setEnvironment(e.target.value as EnvironmentType)}
                          className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                        >
                          <option value="Dev">DEV — Development</option>
                          <option value="Uat">UAT — User Acceptance Testing</option>
                          <option value="Prod">PROD — Production</option>
                        </select>
                      </div>

                      <button
                        type="submit"
                        disabled={createNamespace.isPending}
                        className="w-full px-4 py-2.5 rounded-lg font-medium transition-colors flex items-center justify-center gap-2 text-white bg-primary-500 hover:bg-primary-600 disabled:bg-primary-300"
                      >
                        {createNamespace.isPending
                          ? <><Loader2 className="w-4 h-4 animate-spin" /> Connecting…</>
                          : <>Connect with Azure Entra ID</>}
                      </button>
                    </form>

                    <div className="mt-3 rounded-r-lg border-l-2 border-blue-300 bg-blue-50 pl-3 pr-2 py-2">
                      {entraIdStatus.isDefaultCredentialMode ? (
                        <>
                          <p className="text-xs font-semibold text-blue-800 mb-1">💡 Prerequisites</p>
                          <ol className="text-xs text-blue-700 space-y-0.5 list-decimal list-inside">
                            <li>Run <code className="bg-blue-100 px-1 rounded">az login</code> in your terminal</li>
                            <li>Azure Portal → your namespace → Access Control (IAM)</li>
                            <li>Add role → "Azure Service Bus Data Owner" → your account</li>
                          </ol>
                        </>
                      ) : (
                        <>
                          <p className="text-xs font-semibold text-blue-800 mb-1">💡 Prerequisites</p>
                          <ol className="text-xs text-blue-700 space-y-0.5 list-decimal list-inside">
                            <li>Azure Portal → your namespace → Access Control (IAM)</li>
                            <li>Add role → "Azure Service Bus Data Owner" → ServiceHub app registration</li>
                          </ol>
                        </>
                      )}
                    </div>
                  </>

                ) : (
                  /* ════ NOTHING CONFIGURED ════ */
                  <div className="space-y-3">
                    {/* End-user explanation */}
                    <div className="rounded-lg bg-amber-50 border border-amber-200 p-4">
                      <div className="flex items-center gap-2 mb-2">
                        <Lock className="w-4 h-4 text-amber-700 flex-shrink-0" />
                        <span className="text-xs font-semibold text-amber-800">
                          Microsoft sign-in is not available on this instance
                        </span>
                      </div>
                      <p className="text-xs text-amber-700 mb-3">
                        The administrator of this ServiceHub instance has not enabled Microsoft sign-in yet.
                        You can connect right now using a <strong>Connection String</strong> instead,
                        or ask your administrator to enable passwordless sign-in.
                      </p>
                      <button
                        type="button"
                        onClick={() => setActiveTab('connectionstring')}
                        className="w-full px-3 py-2 rounded-lg text-xs font-semibold border border-amber-400 bg-amber-100 text-amber-900 hover:bg-amber-200 transition-colors"
                      >
                        Use Connection String instead →
                      </button>
                    </div>

                    {/* Administrator setup note — collapsed/secondary */}
                    <details className="rounded-lg border border-gray-200 bg-gray-50">
                      <summary className="px-3 py-2 text-xs font-semibold text-gray-600 cursor-pointer select-none list-none flex items-center gap-1.5">
                        <span className="text-gray-400">⚙</span> Administrator: enable Microsoft sign-in
                      </summary>
                      <div className="px-3 pb-3 pt-1 space-y-2">
                        <p className="text-xs text-gray-600">
                          Set these environment variables on the ServiceHub API to enable the
                          "Sign in with Microsoft" button for all users:
                        </p>
                        <code className="block text-[10px] bg-white rounded border border-gray-200 p-2 text-gray-800 font-mono whitespace-pre">
{`AzureOAuth__Enabled=true
AzureOAuth__ClientId=<App Registration Client ID>
AzureOAuth__ClientSecret=<Client Secret>
AzureOAuth__RedirectUri=https://your-servicehub-url/api/v1/auth/azure/callback
AzureOAuth__FrontendBaseUrl=https://your-servicehub-url`}
                        </code>
                        <div className="space-y-1 pt-1">
                          <a
                            href="https://github.com/debdevops/servicehub/blob/main/azure-entra-id/oauth/README.md"
                            target="_blank" rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-xs text-blue-600 underline hover:text-blue-800 block"
                          >
                            Full OAuth 2.0 setup guide <ExternalLink className="w-3 h-3" />
                          </a>
                          <a
                            href="https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app"
                            target="_blank" rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-xs text-blue-600 underline hover:text-blue-800 block"
                          >
                            Register an app in Azure Portal <ExternalLink className="w-3 h-3" />
                          </a>
                        </div>
                      </div>
                    </details>
                  </div>
                )}
              </>
            )}
          </div>

          {/* Right: Saved Connections + Demo callout */}
          <div className="space-y-4">
            <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
              <h2 className="text-sm font-semibold text-gray-900 mb-3 flex items-center gap-1.5">
                Saved Connections
                <HelpTooltip {...tooltips.connect.savedConnections} position="bottom" className="ml-1" />
              </h2>

              <div className="space-y-3">
                {namespaces && namespaces.length > 0 ? (
                  namespaces.map((ns) => (
                    <div
                      key={ns.id}
                      className="flex items-center justify-between p-4 rounded-lg transition-colors cursor-pointer bg-gray-50 border border-gray-200 hover:bg-gray-100 hover:border-gray-300"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`w-2.5 h-2.5 rounded-full ${
                            ns.isActive ? 'bg-green-500' : 'bg-gray-300'
                          }`}
                        />
                        <div>
                          <div className="flex items-center gap-2">
                            <h3 className="font-medium text-gray-900">{ns.displayName || ns.name}</h3>
                            <span className={`px-1.5 py-0.5 text-[10px] font-semibold rounded uppercase ${
                              ns.environment === 'Prod' ? 'bg-red-100 text-red-700' :
                              ns.environment === 'Uat' ? 'bg-amber-100 text-amber-700' :
                              'bg-green-100 text-green-700'
                            }`}>
                              {ns.environment || 'Dev'}
                            </span>
                            {ns.authType && ns.authType !== 'ConnectionString' && (
                              <span className="px-1.5 py-0.5 text-[10px] font-semibold rounded bg-blue-100 text-blue-700">
                                Entra ID
                              </span>
                            )}
                          </div>
                          <p className="text-xs text-gray-500">
                            {ns.name}
                            {ns.lastUsedAt && ` • Last used: ${new Date(ns.lastUsedAt).toLocaleDateString()}`}
                          </p>
                        </div>
                      </div>

                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => navigate(`/messages?namespace=${ns.id}`)}
                          className="px-3 py-1.5 text-white text-sm font-medium rounded-lg transition-colors bg-primary-500 hover:bg-primary-600"
                          aria-label={`Open ${ns.displayName || ns.name} namespace`}
                        >
                          Open
                        </button>
                        <button
                          onClick={() => openDeleteConfirm(ns.id, ns.displayName || ns.name)}
                          className="p-1.5 hover:bg-red-100 text-red-600 rounded-lg transition-colors"
                          type="button"
                          aria-label={`Delete ${ns.displayName || ns.name} connection`}
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  ))
                ) : (
                  <div className="text-center py-8">
                    <div className="w-12 h-12 bg-gray-100 border border-gray-200 rounded-full flex items-center justify-center mx-auto mb-3">
                      <span className="text-2xl">📭</span>
                    </div>
                    <h3 className="font-medium text-gray-900 mb-1">No saved connections yet</h3>
                    <p className="text-sm text-gray-500">Add your first Service Bus to get started</p>
                  </div>
                )}
              </div>
            </div>

            {/* Demo callout — compact secondary card */}
            <div className="bg-gradient-to-r from-slate-800 to-primary-900 rounded-xl border border-slate-700 p-4 flex items-center gap-4">
              <div className="w-9 h-9 bg-amber-400/20 border border-amber-400/30 rounded-lg flex items-center justify-center shrink-0">
                <Play className="w-4 h-4 text-amber-300 fill-current" />
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-xs font-semibold text-white">No Service Bus? Try the live demo</p>
                <p className="text-[11px] text-slate-400 mt-0.5">50 production-realistic messages, DLQ scenarios, AI root-cause analysis — no credentials needed.</p>
              </div>
              <button
                onClick={() => navigate('/messages?demo=true')}
                className="shrink-0 px-3 py-1.5 bg-amber-400 hover:bg-amber-300 text-slate-900 font-semibold text-xs rounded-lg transition-colors flex items-center gap-1.5"
              >
                Launch
                <ArrowRight className="w-3 h-3" />
              </button>
            </div>

            {/* Self-host callout */}
            <div className="bg-white rounded-xl border border-blue-100 p-4">
              <div className="flex items-start gap-3">
                <div className="w-8 h-8 bg-blue-50 rounded-lg flex items-center justify-center flex-shrink-0">
                  <Shield className="w-4 h-4 text-blue-600" />
                </div>
                <div>
                  <p className="text-xs font-semibold text-gray-900 mb-0.5">
                    Prefer zero-trust? Run it yourself.
                  </p>
                  <p className="text-xs text-gray-500 mb-2">
                    Deploy ServiceHub inside your own Azure subscription in under 10 minutes.
                    Your data never leaves your infrastructure.
                  </p>
                  <a
                    href="https://github.com/debdevops/servicehub/blob/main/self-hosting/README.md"
                    target="_blank" rel="noopener noreferrer"
                    className="inline-flex items-center gap-1 text-xs font-medium text-blue-600 hover:text-blue-800"
                  >
                    Self-hosting guide <ArrowRight className="w-3 h-3" />
                  </a>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* ══════════════════════════════════════════════════════════════
            HOW IT WORKS
        ══════════════════════════════════════════════════════════════ */}
        <div className="mb-10">
          <h3 className="text-lg font-semibold text-gray-900 text-center mb-2">How it works</h3>
          <p className="text-sm text-gray-500 text-center mb-6">From zero to full message visibility in under 60 seconds</p>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <div className="text-center p-5 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="w-9 h-9 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-3">
                <span className="text-sm font-bold text-primary-600">1</span>
              </div>
              <p className="text-sm font-semibold text-gray-900">60 seconds to your first message view</p>
              <p className="text-xs text-gray-500 mt-1.5">Paste a Listen-only connection string — no admin rights, no Azure Portal clutter, no SDK to install</p>
            </div>
            <div className="text-center p-5 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="w-9 h-9 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-3">
                <span className="text-sm font-bold text-primary-600">2</span>
              </div>
              <p className="text-sm font-semibold text-gray-900">Find the failing message in seconds, not hours</p>
              <p className="text-xs text-gray-500 mt-1.5">Filter by status, search by content, jump to the DLQ, and let AI pinpoint the root cause automatically</p>
            </div>
            <div className="text-center p-5 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="w-9 h-9 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-3">
                <span className="text-sm font-bold text-primary-600">3</span>
              </div>
              <p className="text-sm font-semibold text-gray-900">Fix and replay without switching tools</p>
              <p className="text-xs text-gray-500 mt-1.5">Bulk-replay dead-lettered messages, set auto-replay rules, and trace correlation chains — all in one browser tab</p>
            </div>
          </div>
        </div>

        {/* ══════════════════════════════════════════════════════════════
            WHY SERVICEHUB — Use-case specific wins
        ══════════════════════════════════════════════════════════════ */}
        <div className="mb-12">
          <h3 className="text-xl font-semibold text-gray-900 text-center mb-2">Built for these exact scenarios</h3>
          <p className="text-sm text-gray-500 text-center mb-8">Real problems that ServiceHub solves in minutes, not hours</p>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="p-6 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-red-50 border border-red-200 text-red-700 text-xs font-semibold mb-4">
                <span className="w-1.5 h-1.5 rounded-full bg-red-500" /> Dead-Letter Flood
              </div>
              <h4 className="font-semibold text-gray-900 mb-2">847 messages failing? Find the pattern in 30 seconds.</h4>
              <p className="text-xs text-gray-600">
                Group by error, spot the duplicate root cause (null ref, version mismatch, timeout), apply a fix, 1-click bulk replay.
                Azure Portal: 30 min. ServiceHub: 2 min.
              </p>
            </div>

            <div className="p-6 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-amber-50 border border-amber-200 text-amber-700 text-xs font-semibold mb-4">
                <span className="w-1.5 h-1.5 rounded-full bg-amber-500" /> Retry Loop
              </div>
              <h4 className="font-semibold text-gray-900 mb-2">Message stuck reprocessing infinitely? Diagnose in 20 seconds.</h4>
              <p className="text-xs text-gray-600">
                View retry count, delivery history, peek the exact error, check for circuit-breaker miss or config bug.
                Dead-letter before it cascades.
              </p>
            </div>

            <div className="p-6 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-blue-50 border border-blue-200 text-blue-700 text-xs font-semibold mb-4">
                <span className="w-1.5 h-1.5 rounded-full bg-blue-500" /> Correlation Tracing
              </div>
              <h4 className="font-semibold text-gray-900 mb-2">Payment failed → trace all downstream effects in one view.</h4>
              <p className="text-xs text-gray-600">
                Jump from OrderCreated → PaymentProcessed → InventoryReserved. See what order #12847 touched. Replay the chain together.
              </p>
            </div>
          </div>
        </div>

        {/* ══════════════════════════════════════════════════════════════
            COMPARISON + BOTTOM CTA
        ══════════════════════════════════════════════════════════════ */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-8 mb-12">
          {/* vs Azure Portal */}
          <div className="bg-gradient-to-br from-gray-50 to-white rounded-xl border border-gray-200 p-6">
            <h4 className="text-sm font-bold text-gray-800 mb-4 flex items-center gap-2">
              <span className="text-base">⚡</span>
              ServiceHub vs Azure Portal
            </h4>
            <div className="space-y-3 text-sm">
              {[
                { task: 'Find a failed message', portal: '8–15 min', hub: '< 30 sec', hubClass: 'text-green-700 font-semibold' },
                { task: 'Replay DLQ batch', portal: 'Not possible', hub: '1 click', hubClass: 'text-green-700 font-semibold' },
                { task: 'AI root-cause analysis', portal: 'Not available', hub: 'Automatic', hubClass: 'text-green-700 font-semibold' },
                { task: 'Share message link', portal: 'Not possible', hub: 'Deep link', hubClass: 'text-green-700 font-semibold' },
                { task: 'Works on Mac/Linux', portal: '✓', hub: '✓', hubClass: 'text-gray-700' },
              ].map((row) => (
                <div key={row.task} className="flex items-center text-xs">
                  <span className="flex-1 text-gray-700">{row.task}</span>
                  <span className="w-24 text-center text-gray-400">{row.portal}</span>
                  <span className={`w-24 text-center ${row.hubClass}`}>{row.hub}</span>
                </div>
              ))}
              <div className="flex items-center text-[10px] text-gray-400 pt-1 border-t border-gray-100">
                <span className="flex-1" />
                <span className="w-24 text-center">Azure Portal</span>
                <span className="w-24 text-center text-primary-600 font-semibold">ServiceHub</span>
              </div>
            </div>
          </div>

          {/* Star CTA */}
          <div className="bg-gradient-to-br from-slate-900 to-slate-800 rounded-xl border border-slate-700 p-6 flex flex-col justify-between">
            <div>
              <div className="flex items-center gap-2 mb-3">
                <Star className="w-5 h-5 text-amber-400 fill-current" />
                <span className="text-sm font-bold text-white">Open Source</span>
              </div>
              <p className="text-slate-300 text-sm leading-relaxed mb-4">
                ServiceHub is free, open-source, and built by engineers for engineers.
                If it saved your 2 AM, consider starring the repo — it takes 3 seconds and
                helps other developers find it.
              </p>
              <div className="flex items-center gap-2 text-xs text-slate-500 mb-5">
                <Shield className="w-3.5 h-3.5" />
                Connection strings encrypted · No telemetry on message content · Self-hostable
              </div>
            </div>
            <a
              href="https://github.com/debdevops/servicehub"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-4 py-2.5 bg-white/10 hover:bg-white/20 border border-white/20 rounded-lg text-white text-sm font-medium transition-colors"
            >
              <Github className="w-4 h-4" />
              View on GitHub · ⭐ Star
            </a>
          </div>
        </div>

      </div>

      {/* Delete Confirmation Dialog */}
      <ConfirmDialog
        isOpen={deleteConfirm.isOpen}
        title="Delete Connection"
        message={`Are you sure you want to delete the connection "${deleteConfirm.name}"?\n\nThis will remove the saved connection but will not affect your Azure Service Bus namespace.`}
        variant="danger"
        confirmLabel="Delete"
        onConfirm={handleDeleteConfirm}
        onCancel={handleDeleteCancel}
      />
    </div>
  );
}
