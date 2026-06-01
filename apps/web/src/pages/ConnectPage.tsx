import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Eye, EyeOff, Trash2, Github, Play, Star, Shield, ArrowRight, AlertTriangle } from 'lucide-react';
import { useNamespaces, useCreateNamespace, useDeleteNamespace } from '@/hooks/useNamespaces';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { HelpTooltip } from '@/components/help';
import { tooltips } from '@/lib/helpContent';
import type { EnvironmentType, CloudProviderType } from '@/lib/api/types';
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
  const [environment, setEnvironment] = useState<EnvironmentType>('dev');

  // Cloud provider selection
  const [cloudProvider, setCloudProvider] = useState<CloudProviderType>('azure');

  // AWS-specific fields
  const [awsAccessKeyId, setAwsAccessKeyId] = useState('');
  const [awsSecretKey, setAwsSecretKey] = useState('');
  const [awsRegion, setAwsRegion] = useState('us-east-1');
  const [awsQueuePrefix, setAwsQueuePrefix] = useState('');

  // GCP-specific fields
  const [gcpProjectId, setGcpProjectId] = useState('');
  const [gcpServiceAccountJson, setGcpServiceAccountJson] = useState('');
  
  // v3.1.0 HKDF upgrade notice
  const [showHkdfNotice, setShowHkdfNotice] = useState(
    () => localStorage.getItem('servicehub_v310_hkdf_notice_dismissed') !== 'true'
  );

  const dismissHkdfNotice = () => {
    localStorage.setItem('servicehub_v310_hkdf_notice_dismissed', 'true');
    setShowHkdfNotice(false);
  };

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

    if (!displayName.trim()) {
      return;
    }

    if (cloudProvider === 'aws') {
      // AWS connection: build name from region endpoint and connection string from access key
      if (!awsAccessKeyId.trim() || !awsSecretKey.trim() || !awsRegion.trim()) {
        toast.error('AWS Access Key ID, Secret Access Key, and Region are required.');
        return;
      }
      const namespaceName = `sqs.${awsRegion}.amazonaws.com`;
      // Store credentials as AKID:SecretKey (colon-separated, no URL prefix).
      // Region is stored separately in awsRegion — never embed it in the
      // connection string to avoid URL-parser log-scraper leaks.
      const awsConnectionString = `${awsAccessKeyId.trim()}:${awsSecretKey.trim()}`;

      try {
        await createNamespace.mutateAsync({
          name: namespaceName,
          connectionString: awsConnectionString,
          displayName: displayName.trim(),
          environment,
          cloudProvider: 'aws',
          awsRegion: awsRegion.trim(),
        });
        setDisplayName('');
        setAwsAccessKeyId('');
        setAwsSecretKey('');
        setAwsQueuePrefix('');
      } catch {
        // Error handled by mutation hook
      }
      return;
    }

    if (cloudProvider === 'gcp') {
      // GCP connection: project ID as name, service account JSON as connection string
      if (!gcpProjectId.trim() || !gcpServiceAccountJson.trim()) {
        toast.error('GCP Project ID and Service Account JSON are required.');
        return;
      }
      try {
        await createNamespace.mutateAsync({
          name: gcpProjectId.trim(),
          connectionString: gcpServiceAccountJson.trim(),
          displayName: displayName.trim(),
          environment,
          cloudProvider: 'gcp',
          gcpProjectId: gcpProjectId.trim(),
        });
        setDisplayName('');
        setGcpProjectId('');
        setGcpServiceAccountJson('');
      } catch {
        // Error handled by mutation hook
      }
      return;
    }

    // Azure path (default)
    if (!connectionString.trim()) {
      return;
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
        cloudProvider: 'azure',
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
      setEnvironment('dev');
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
            v3.1.0 UPGRADE NOTICE
        ══════════════════════════════════════════════════════════════ */}
        {showHkdfNotice && (
          <div className="mb-4 rounded-lg bg-amber-50 border border-amber-200 p-3 flex items-start justify-between gap-3">
            <p className="text-xs text-amber-800">
              <span className="font-semibold">ServiceHub v3.1.0</span> upgrades encryption key derivation (HKDF).
              {' '}If you have existing saved connections, they must be re-added —
              delete them and add them again with your connection string.
            </p>
            <button
              type="button"
              onClick={dismissHkdfNotice}
              className="shrink-0 text-xs font-medium text-amber-700 hover:text-amber-900 whitespace-nowrap"
            >
              I understand, don&apos;t show again
            </button>
          </div>
        )}

        {/* ══════════════════════════════════════════════════════════════
            MULTI-INSTANCE STORAGE NOTICE
        ══════════════════════════════════════════════════════════════ */}
        <div className="mb-4 rounded-lg bg-sky-50 border border-sky-200 p-3 flex items-start gap-2.5">
          <AlertTriangle className="w-4 h-4 text-sky-600 shrink-0 mt-0.5" />
          <p className="text-xs text-sky-800">
            <span className="font-semibold">Single-instance storage:</span> Namespace connections are stored locally on the server running ServiceHub.
            {' '}If you are running <strong>multiple instances</strong> (e.g., Azure App Service with scale-out), each instance has its own connection list.
            {' '}Use <strong>sticky sessions</strong> or ensure all instances share the same storage path to avoid inconsistent connection lists across page refreshes.
          </p>
        </div>

        {/* ══════════════════════════════════════════════════════════════
            CONNECT FORM + SAVED CONNECTIONS  (primary action — above fold)
        ══════════════════════════════════════════════════════════════ */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 items-start mb-6">
          {/* Left: Connect Form */}
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-6">
            <div className="flex items-center gap-3 mb-5">
              <div className="w-9 h-9 bg-primary-50 border border-primary-100 rounded-lg flex items-center justify-center">
                <span className="text-base">☁️</span>
              </div>
              <div>
                <h2 className="text-base font-semibold text-gray-900">Connect to Cloud Messaging</h2>
                <p className="text-xs text-gray-500">
                  Azure Service Bus · AWS SQS · GCP Pub/Sub
                </p>
              </div>
            </div>

            {/* ── Cloud provider selector ───────────────────────────────────────── */}
            <div className="mb-4">
              <p className="text-xs font-medium text-gray-700 mb-2">Cloud provider</p>
              <div className="grid grid-cols-3 gap-2">
                {/* Azure */}
                <button
                  type="button"
                  onClick={() => setCloudProvider('azure')}
                  className={`flex flex-col items-center gap-1 p-2.5 rounded-lg border text-xs font-medium transition-all ${
                    cloudProvider === 'azure'
                      ? 'bg-blue-50 border-blue-400 text-blue-700 ring-2 ring-blue-300'
                      : 'bg-white border-gray-200 text-gray-600 hover:bg-blue-50 hover:border-blue-200'
                  }`}
                  title="Azure Service Bus"
                >
                  <span className="text-lg leading-none">𝓐</span>
                  <span>Azure</span>
                  {cloudProvider === 'azure' && (
                    <span className="text-[10px] bg-blue-100 text-blue-600 px-1.5 py-0.5 rounded-full">Selected</span>
                  )}
                </button>

                {/* AWS */}
                <button
                  type="button"
                  onClick={() => setCloudProvider('aws')}
                  className={`flex flex-col items-center gap-1 p-2.5 rounded-lg border text-xs font-medium transition-all ${
                    cloudProvider === 'aws'
                      ? 'bg-orange-50 border-orange-400 text-orange-700 ring-2 ring-orange-300'
                      : 'bg-white border-gray-200 text-gray-600 hover:bg-orange-50 hover:border-orange-200'
                  }`}
                  title="Amazon Web Services SQS"
                >
                  <span className="text-lg leading-none">⬡</span>
                  <span>AWS</span>
                  {cloudProvider === 'aws' && (
                    <span className="text-[10px] bg-orange-100 text-orange-600 px-1.5 py-0.5 rounded-full">Selected</span>
                  )}
                </button>

                {/* GCP */}
                <button
                  type="button"
                  onClick={() => setCloudProvider('gcp')}
                  className={`flex flex-col items-center gap-1 p-2.5 rounded-lg border text-xs font-medium transition-all ${
                    cloudProvider === 'gcp'
                      ? 'bg-green-50 border-green-400 text-green-700 ring-2 ring-green-300'
                      : 'bg-white border-gray-200 text-gray-600 hover:bg-green-50 hover:border-green-200'
                  }`}
                  title="Google Cloud Pub/Sub"
                >
                  <span className="text-lg leading-none">◈</span>
                  <span>GCP</span>
                  {cloudProvider === 'gcp' && (
                    <span className="text-[10px] bg-green-100 text-green-600 px-1.5 py-0.5 rounded-full">Selected</span>
                  )}
                </button>
              </div>

              {/* Provider coming-soon notices for non-Azure */}
              {cloudProvider === 'aws' && (
                <div className="mt-2 p-2 rounded bg-orange-50 border border-orange-200 text-xs text-orange-700">
                  <strong>AWS SQS — Phase 2 (coming soon).</strong> You can save the connection details now; full message browsing will be enabled when the AWS provider ships.
                </div>
              )}
              {cloudProvider === 'gcp' && (
                <div className="mt-2 p-2 rounded bg-green-50 border border-green-200 text-xs text-green-700">
                  <strong>GCP Pub/Sub — Phase 2 (coming soon).</strong> You can save the connection details now; full message browsing will be enabled when the GCP provider ships.
                </div>
              )}
            </div>

            {/* Trust & Security panel (Azure only — GCP/AWS have different security models) */}
            {cloudProvider === 'azure' && (
              <div className="mb-4 rounded-lg bg-green-50 border border-green-100 p-3">
                <div className="flex items-center gap-2 mb-2">
                  <Shield className="w-4 h-4 text-green-600 flex-shrink-0" />
                  <span className="text-xs font-semibold text-green-800">Your data stays yours</span>
                </div>
                <div className="space-y-1">
                  {[
                    { label: 'Connection string', value: 'AES-256-GCM encrypted before saving — never returned to browser' },
                    { label: 'Message content', value: 'Never logged or stored by ServiceHub' },
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
            )}

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
                  placeholder={
                    cloudProvider === 'aws' ? 'e.g., Production SQS (us-east-1)' :
                    cloudProvider === 'gcp' ? 'e.g., Production Pub/Sub' :
                    'e.g., Production Service Bus'
                  }
                  required
                  className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-primary-400 focus:border-primary-300"
                />
              </div>

              {/* ── Azure form ───────────────────────────────────────────────────── */}
              {cloudProvider === 'azure' && (
                <>
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
                        required={cloudProvider === 'azure'}
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
                </>
              )}

              {/* ── AWS form ─────────────────────────────────────────────────────── */}
              {cloudProvider === 'aws' && (
                <>
                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      AWS Access Key ID <span className="text-red-500">*</span>
                      <HelpTooltip
                        text="Your AWS IAM Access Key ID. Create a dedicated IAM user with sqs:ReceiveMessage and sqs:GetQueueAttributes permissions."
                        position="right"
                        className="ml-1"
                      />
                    </label>
                    <input
                      type="text"
                      value={awsAccessKeyId}
                      onChange={(e) => setAwsAccessKeyId(e.target.value)}
                      placeholder="AKIAIOSFODNN7EXAMPLE"
                      required={cloudProvider === 'aws'}
                      className="w-full px-3 py-2 rounded-lg text-sm font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-orange-400 focus:border-orange-300"
                    />
                  </div>

                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      AWS Secret Access Key <span className="text-red-500">*</span>
                    </label>
                    <div className="relative">
                      <input
                        type={showPassword ? 'text' : 'password'}
                        value={awsSecretKey}
                        onChange={(e) => setAwsSecretKey(e.target.value)}
                        placeholder="wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
                        required={cloudProvider === 'aws'}
                        className="w-full px-3 py-2 pr-10 rounded-lg text-sm font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-orange-400 focus:border-orange-300"
                      />
                      <button
                        type="button"
                        onClick={() => setShowPassword(!showPassword)}
                        className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                      >
                        {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                      </button>
                    </div>
                  </div>

                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      AWS Region <span className="text-red-500">*</span>
                      <HelpTooltip
                        text="The AWS region where your SQS queues are located."
                        position="right"
                        className="ml-1"
                      />
                    </label>
                    <select
                      value={awsRegion}
                      onChange={(e) => setAwsRegion(e.target.value)}
                      className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-orange-400 focus:border-orange-300"
                    >
                      <option value="us-east-1">us-east-1 — US East (N. Virginia)</option>
                      <option value="us-east-2">us-east-2 — US East (Ohio)</option>
                      <option value="us-west-1">us-west-1 — US West (N. California)</option>
                      <option value="us-west-2">us-west-2 — US West (Oregon)</option>
                      <option value="eu-west-1">eu-west-1 — Europe (Ireland)</option>
                      <option value="eu-west-2">eu-west-2 — Europe (London)</option>
                      <option value="eu-central-1">eu-central-1 — Europe (Frankfurt)</option>
                      <option value="ap-southeast-1">ap-southeast-1 — Asia Pacific (Singapore)</option>
                      <option value="ap-southeast-2">ap-southeast-2 — Asia Pacific (Sydney)</option>
                      <option value="ap-northeast-1">ap-northeast-1 — Asia Pacific (Tokyo)</option>
                      <option value="sa-east-1">sa-east-1 — South America (São Paulo)</option>
                    </select>
                  </div>

                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Queue/Topic URL prefix
                      <HelpTooltip
                        text="Optional. Filter to queues matching this prefix, e.g. 'orders-' or leave blank to see all queues."
                        position="right"
                        className="ml-1"
                      />
                    </label>
                    <input
                      type="text"
                      value={awsQueuePrefix}
                      onChange={(e) => setAwsQueuePrefix(e.target.value)}
                      placeholder="e.g., orders- (optional)"
                      className="w-full px-3 py-2 rounded-lg text-sm bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-orange-400 focus:border-orange-300"
                    />
                  </div>

                  {/* IAM permissions guidance */}
                  <details className="mb-3 rounded-lg border border-orange-200 bg-orange-50 text-xs">
                    <summary className="cursor-pointer px-3 py-2 font-medium text-orange-800 select-none">
                      Required IAM permissions for this IAM user
                    </summary>
                    <div className="px-3 pb-3 pt-1 text-orange-900 space-y-1">
                      <p className="font-semibold mt-1">SQS permissions:</p>
                      <ul className="list-disc list-inside space-y-0.5">
                        <li><code>sqs:ReceiveMessage</code></li>
                        <li><code>sqs:GetQueueAttributes</code></li>
                        <li><code>sqs:GetQueueUrl</code></li>
                        <li><code>sqs:ListQueues</code></li>
                        <li><code>sqs:SendMessage</code> <span className="text-orange-700">(for replay / send operations)</span></li>
                        <li><code>sqs:DeleteMessage</code> <span className="text-orange-700">(for replay: moves message from DLQ)</span></li>
                      </ul>
                      <p className="font-semibold mt-2">SNS permissions (if using SNS fan-out):</p>
                      <ul className="list-disc list-inside space-y-0.5">
                        <li><code>sns:ListTopics</code></li>
                        <li><code>sns:ListSubscriptions</code></li>
                        <li><code>sns:Publish</code> <span className="text-orange-700">(for send operations)</span></li>
                      </ul>
                      <p className="mt-2 text-orange-700">
                        Tip: Use a dedicated IAM user with least-privilege policies scoped to your queue ARNs.
                      </p>
                    </div>
                  </details>
                </>
              )}

              {/* ── GCP form ─────────────────────────────────────────────────────── */}
              {cloudProvider === 'gcp' && (
                <>
                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      GCP Project ID <span className="text-red-500">*</span>
                      <HelpTooltip
                        text="Your Google Cloud project identifier (e.g., my-project-123). Found in the GCP Console header."
                        position="right"
                        className="ml-1"
                      />
                    </label>
                    <input
                      type="text"
                      value={gcpProjectId}
                      onChange={(e) => setGcpProjectId(e.target.value)}
                      placeholder="my-project-123"
                      required={cloudProvider === 'gcp'}
                      className="w-full px-3 py-2 rounded-lg text-sm font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-green-400 focus:border-green-300"
                    />
                  </div>

                  <div className="mb-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      Service Account JSON <span className="text-red-500">*</span>
                      <HelpTooltip
                        text="Paste the full service account JSON key file. The account needs roles/pubsub.subscriber on the project."
                        position="right"
                        className="ml-1"
                      />
                    </label>
                    <textarea
                      value={gcpServiceAccountJson}
                      onChange={(e) => setGcpServiceAccountJson(e.target.value)}
                      placeholder={'{\n  "type": "service_account",\n  "project_id": "...",\n  ...\n}'}
                      rows={5}
                      required={cloudProvider === 'gcp'}
                      className="w-full px-3 py-2 rounded-lg text-xs font-mono bg-white border border-gray-200 focus:outline-none focus:ring-2 focus:ring-green-400 focus:border-green-300 resize-y"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Credentials are AES-GCM encrypted before storage and never returned to the browser.
                    </p>
                  </div>

                  {/* GCP IAM guidance */}
                  <details className="mb-3 rounded-lg border border-green-200 bg-green-50 text-xs">
                    <summary className="cursor-pointer px-3 py-2 font-medium text-green-800 select-none">
                      Required GCP roles for this service account
                    </summary>
                    <div className="px-3 pb-3 pt-1 text-green-900 space-y-1">
                      <p className="font-semibold mt-1">Minimum roles (read-only browsing):</p>
                      <ul className="list-disc list-inside space-y-0.5">
                        <li><code>roles/pubsub.subscriber</code> — pull &amp; acknowledge messages</li>
                        <li><code>roles/pubsub.viewer</code> — list topics and subscriptions</li>
                      </ul>
                      <p className="font-semibold mt-2">Additional roles (send / replay):</p>
                      <ul className="list-disc list-inside space-y-0.5">
                        <li><code>roles/pubsub.publisher</code> — publish messages to topics</li>
                      </ul>
                      <p className="mt-2 text-green-700">
                        Tip: Grant these roles on the project or at the topic/subscription resource level for least privilege.
                        Generate a key at <strong>IAM &amp; Admin → Service Accounts → Keys → Add Key → JSON</strong>.
                      </p>
                    </div>
                  </details>
                </>
              )}

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
                  <option value="dev">DEV — Development</option>
                  <option value="uat">UAT — User Acceptance Testing</option>
                  <option value="prod">PROD — Production</option>
                </select>
                <p className="text-xs text-gray-500 mt-1">
                  {environment === 'prod' ? (
                    <>
                      <span className="text-amber-600 font-semibold">⚠️ Production namespace:</span>
                      {' '}Quick Actions (replay, send, generate) are disabled for safety. Validate your workflow in DEV and UAT first.
                    </>
                  ) : environment === 'uat' ? (
                    <>
                      <span className="text-amber-700 font-medium">UAT namespace:</span>
                      {' '}Validate replay rules and DLQ behaviour here before connecting to PROD.
                    </>
                  ) : (
                    <>
                      <span className="text-green-700 font-medium">Recommended: start with a DEV namespace.</span>
                      {' '}Test DLQ inspection, replay rules, and message operations safely before moving to UAT or PROD.
                    </>
                  )}
                </p>
              </div>

              <button
                type="submit"
                disabled={createNamespace.isPending}
                className={`w-full px-4 py-2.5 rounded-lg font-medium transition-colors flex items-center justify-center gap-2 text-white disabled:opacity-50 ${
                  cloudProvider === 'aws'
                    ? 'bg-orange-500 hover:bg-orange-600 disabled:bg-orange-300'
                    : cloudProvider === 'gcp'
                    ? 'bg-green-600 hover:bg-green-700 disabled:bg-green-300'
                    : 'bg-primary-500 hover:bg-primary-600 disabled:bg-primary-300'
                }`}
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

            {/* SAS instructions — Azure only */}
            {cloudProvider === 'azure' && (
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
                              ns.environment === 'prod' ? 'bg-red-100 text-red-700' :
                              ns.environment === 'uat' ? 'bg-amber-100 text-amber-700' :
                              'bg-green-100 text-green-700'
                            }`}>
                              {ns.environment || 'dev'}
                            </span>
                            <span className={`px-1.5 py-0.5 text-[10px] font-semibold rounded uppercase ${
                              ns.cloudProvider === 'aws' ? 'bg-orange-100 text-orange-700' :
                              ns.cloudProvider === 'gcp' ? 'bg-emerald-100 text-emerald-700' :
                              'bg-blue-100 text-blue-700'
                            }`}>
                              {ns.cloudProvider === 'aws' ? 'AWS' : ns.cloudProvider === 'gcp' ? 'GCP' : 'Azure'}
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
                    <h3 className="font-medium text-gray-900 mb-1">No saved connections yet</h3>
                    <p className="text-sm text-gray-500">Add your first Service Bus to get started</p>
                  </div>
                )}
              </div>
            </div>

            {/* Demo callout — three cloud demos */}
            <div className="rounded-xl border border-slate-700 bg-gradient-to-r from-slate-800 to-primary-900 p-4">
              <p className="text-xs font-semibold text-white mb-3 flex items-center gap-2">
                <Play className="w-3.5 h-3.5 text-amber-300 fill-current" />
                No credentials? Try a live demo first
              </p>
              <div className="grid grid-cols-3 gap-2">
                {[
                  { label: 'Azure', sub: 'Service Bus · Contoso', url: '/messages?demo=azure', color: 'bg-blue-600 hover:bg-blue-700' },
                  { label: 'AWS', sub: 'SQS · AcmeRetail', url: '/messages?demo=aws', color: 'bg-orange-500 hover:bg-orange-600' },
                  { label: 'GCP', sub: 'Pub/Sub · MedStream', url: '/messages?demo=gcp', color: 'bg-green-600 hover:bg-green-700' },
                ].map(({ label, sub, url, color }) => (
                  <button
                    key={label}
                    onClick={() => navigate(url)}
                    className={`${color} text-white text-xs font-semibold rounded-lg px-3 py-2 text-left transition-colors`}
                  >
                    <div>{label} Demo</div>
                    <div className="text-white/70 font-normal text-[10px] mt-0.5">{sub}</div>
                  </button>
                ))}
              </div>
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
