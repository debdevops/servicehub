import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Eye, EyeOff, Trash2, Github } from 'lucide-react';
import { useNamespaces, useCreateNamespace, useDeleteNamespace } from '@/hooks/useNamespaces';
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
  const [showPassword, setShowPassword] = useState(false);
  const [displayName, setDisplayName] = useState('');
  const [connectionString, setConnectionString] = useState('');
  const [environment, setEnvironment] = useState<EnvironmentType>('Dev');
  
  // Delete confirmation dialog state
  const [deleteConfirm, setDeleteConfirm] = useState<{ isOpen: boolean; id: string; name: string }>({
    isOpen: false,
    id: '',
    name: '',
  });

  const { data: namespaces, isLoading } = useNamespaces();
  const createNamespace = useCreateNamespace();
  const deleteNamespace = useDeleteNamespace();

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

  const CloudHero = () => (
    <svg
      viewBox="0 0 520 220"
      className="absolute right-6 bottom-0 h-[190px] w-auto opacity-90"
      aria-hidden="true"
    >
      <defs>
        <linearGradient id="sh_cloud" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#ffffff" stopOpacity="0.95" />
          <stop offset="1" stopColor="#e0f2fe" stopOpacity="0.90" />
        </linearGradient>
        <linearGradient id="sh_gear" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#38bdf8" stopOpacity="0.95" />
          <stop offset="1" stopColor="#0ea5e9" stopOpacity="0.95" />
        </linearGradient>
      </defs>

      {/* Clouds */}
      <path
        d="M150 150c-26 0-47-18-52-41-3 1-7 1-10 1-22 0-40-16-43-36-1-6 0-12 1-17 6-26 31-44 59-39 10-20 32-33 57-33 33 0 60 23 63 52 25 1 45 19 45 42 0 24-22 44-49 44H150z"
        fill="url(#sh_cloud)"
      />
      <path
        d="M300 170c-24 0-44-16-48-37-3 1-6 1-9 1-20 0-36-14-39-33-1-5 0-11 1-15 6-23 28-38 53-34 9-17 28-29 49-29 29 0 54 20 56 47 22 1 39 17 39 38 0 22-19 40-43 40H300z"
        fill="url(#sh_cloud)"
        opacity="0.95"
      />

      {/* Simple "gear" circles (hint of 3D without purple) */}
      <circle cx="420" cy="110" r="42" fill="url(#sh_gear)" opacity="0.95" />
      <circle cx="420" cy="110" r="20" fill="#ffffff" opacity="0.85" />
      <circle cx="470" cy="140" r="28" fill="url(#sh_gear)" opacity="0.85" />
      <circle cx="470" cy="140" r="12" fill="#ffffff" opacity="0.85" />
    </svg>
  );

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
    <div className="flex-1 overflow-auto p-10">
      <div className="max-w-6xl mx-auto">
        {/* Hero Section */}
        <div className="text-center mb-10">
          <h1 className="text-4xl font-bold text-gray-900 leading-tight">
            Debug Azure Service Bus at 2 AM. <span className="text-primary-600">In seconds.</span>
          </h1>
          <p className="text-lg text-gray-600 mt-4 max-w-3xl mx-auto">
            ServiceHub is a free, open-source web tool to browse messages, investigate DLQs,
            replay failed messages, and trace message journeys. No installation. Just paste a connection string.
          </p>
          {/* Feature Pills */}
          <div className="flex items-center justify-center gap-3 mt-6">
            <span className="px-4 py-2 rounded-full bg-sky-50 text-sky-700 border border-sky-200 text-sm font-medium">
              🔍 Browse Messages
            </span>
            <span className="px-4 py-2 rounded-full bg-red-50 text-red-700 border border-red-200 text-sm font-medium">
              ⚡ Replay DLQs
            </span>
            <span className="px-4 py-2 rounded-full bg-violet-50 text-violet-700 border border-violet-200 text-sm font-medium">
              🔗 Trace Correlations
            </span>
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8 items-start">
          {/* Left: Hero + Form */}
          <div>
            <div className="mb-8">
              <h2 className="text-3xl font-semibold text-gray-900 leading-tight">
                Connect to <span className="text-primary-600">Azure Service Bus</span>
              </h2>
              <p className="text-base text-gray-600 mt-2">
                Connect using a Shared Access Policy connection string. A Listen-only policy is sufficient
                for DLQ investigation, message browsing, and replay operations.
              </p>
            </div>

            <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-8">
              <div className="flex items-center gap-3 mb-6">
                <div className="w-12 h-12 bg-primary-50 border border-primary-100 rounded-xl flex items-center justify-center">
                  <span className="text-2xl">☁️</span>
                </div>
                <div>
                  <h2 className="text-lg font-semibold text-gray-900">Connect to Service Bus</h2>
                  <p className="text-sm text-gray-500 mt-1">
                    Enter your connection configuration and save it for later.
                  </p>
                </div>
              </div>

              <form onSubmit={handleConnect}>
                <div className="mb-4">
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
                    className="w-full px-4 py-2.5 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                  />
                  <p className="text-xs text-gray-500 mt-1">Friendly name for your Service Bus namespace</p>
                </div>

                <div className="mb-4">
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
                      className="w-full px-4 py-2.5 pr-12 rounded-lg text-sm font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword(!showPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                    >
                      {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                    </button>
                  </div>
                  <div className="mt-2 space-y-2">
                    <div className="bg-blue-50 border border-blue-200 rounded-lg p-3">
                      <p className="text-xs text-blue-800 font-medium mb-1">
                        Create a dedicated SAS policy for ServiceHub
                      </p>
                      <ol className="text-xs text-blue-700 space-y-0.5 list-decimal list-inside">
                        <li>Azure Portal → your Service Bus namespace</li>
                        <li>Shared access policies → + Add policy</li>
                        <li>Name it <code className="bg-blue-100 px-1 rounded">servicehub</code></li>
                        <li>Save → copy Primary Connection String → paste above</li>
                      </ol>
                      <div className="mt-2 space-y-1">
                        <p className="text-xs text-blue-700">
                          <strong>Listen only</strong> — Browse messages, inspect DLQ, view metrics (read-only)
                        </p>
                        <p className="text-xs text-blue-700">
                          <strong>Manage</strong> — Full access: send messages, generate test data, dead-letter, replay (Dev/UAT only)
                        </p>
                      </div>
                      <p className="text-xs text-blue-600 mt-1.5">
                        ⚠️ The Quick Actions button (FAB) requires a policy with <strong>Manage</strong> permission. Production namespaces always hide the FAB for safety.
                      </p>
                    </div>
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
                    className="w-full px-4 py-2.5 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                  >
                    <option value="Dev">DEV — Development</option>
                    <option value="Uat">UAT — User Acceptance Testing</option>
                    <option value="Prod">PROD — Production</option>
                  </select>
                  <p className="text-xs text-gray-500 mt-1">
                    Production namespaces have safety guards — Quick Actions (FAB), message sending, dead-lettering, and replay are all disabled.
                  </p>
                </div>

                <button
                  type="submit"
                  disabled={createNamespace.isPending}
                  className="w-full px-4 py-3 rounded-lg font-medium transition-colors flex items-center justify-center gap-2 text-white bg-primary-500 hover:bg-primary-600 disabled:bg-primary-300"
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
            </div>
          </div>

          {/* Right: Illustration + Saved Connections */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="relative h-52 bg-gradient-to-r from-primary-50 to-blue-50 border-b border-gray-100">
              <CloudHero />
            </div>

            <div className="p-6">
              <h2 className="text-lg font-semibold text-gray-900 mb-4">
                Saved Connections
                <HelpTooltip {...tooltips.connect.savedConnections} position="bottom" className="ml-1.5" />
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
                    <h3 className="font-medium text-gray-900 mb-1">No saved connections</h3>
                    <p className="text-sm text-gray-500">Connect to your first namespace to get started</p>
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Try Demo Button */}
        <div className="text-center mt-8">
          <button
            onClick={() => navigate('/messages?demo=true')}
            className="inline-flex items-center gap-2 px-6 py-3 text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 border-2 border-gray-300 hover:border-gray-400 rounded-xl transition-colors shadow-sm"
          >
            🎮 Explore with Sample Data — No credentials required
          </button>
        </div>

        {/* How it works */}
        <div className="mt-12 mb-8">
          <h3 className="text-xl font-semibold text-gray-900 text-center mb-6">How it works</h3>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="text-center p-6 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="w-10 h-10 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-3">
                <span className="text-lg font-bold text-primary-600">1</span>
              </div>
              <p className="text-sm text-gray-700 font-medium">Paste your connection string</p>
              <p className="text-xs text-gray-500 mt-1">Listen permission is enough</p>
            </div>
            <div className="text-center p-6 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="w-10 h-10 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-3">
                <span className="text-lg font-bold text-primary-600">2</span>
              </div>
              <p className="text-sm text-gray-700 font-medium">Browse queues, messages, and dead-letter content</p>
              <p className="text-xs text-gray-500 mt-1">In real time</p>
            </div>
            <div className="text-center p-6 bg-white rounded-xl border border-gray-200 shadow-sm">
              <div className="w-10 h-10 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-3">
                <span className="text-lg font-bold text-primary-600">3</span>
              </div>
              <p className="text-sm text-gray-700 font-medium">Investigate failures, replay messages, trace journeys</p>
              <p className="text-xs text-gray-500 mt-1">All from your browser</p>
            </div>
          </div>
        </div>

        {/* View on GitHub */}
        <div className="text-center mb-8">
          <a
            href="https://github.com/debdevops/servicehub"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-2 text-sm text-gray-500 hover:text-gray-700 transition-colors"
          >
            <Github className="w-4 h-4" />
            View on GitHub
          </a>
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
