import { useSandboxNamespaces } from '@/hooks/useSandboxNamespaces';
import { useSandboxData } from '@/providers/SandboxDataProvider';
import { CloudProviderTabs } from '@/components/CloudProviderTabs';
import { SandboxPanel } from '@/components/SandboxPanel';
import { SandboxEmptyState } from '@/components/SandboxEmptyState';

export function NamespacesPage() {
  const { data: namespaces, isLoading } = useSandboxNamespaces();
  const { cloudProvider } = useSandboxData();

  return (
    <div className="max-w-4xl mx-auto px-6 py-10">
      <h1 className="text-2xl font-bold text-gray-900 mb-1">Namespaces</h1>
      <p className="text-gray-600 mb-6">Browse namespaces across simulated cloud providers.</p>
      <CloudProviderTabs />
      <SandboxPanel title="Namespaces">
        {isLoading ? (
          <SandboxEmptyState message="Loading namespaces…" />
        ) : !namespaces?.length ? (
          <SandboxEmptyState message={`No namespaces found for ${cloudProvider}.`} />
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-2 pr-4 font-medium">Name</th>
                <th className="py-2 pr-4 font-medium">Environment</th>
                <th className="py-2 pr-4 font-medium">Status</th>
                <th className="py-2 font-medium">Created</th>
              </tr>
            </thead>
            <tbody>
              {namespaces.map((ns) => (
                <tr key={ns.id} className="border-b border-gray-100 last:border-0">
                  <td className="py-2 pr-4 text-gray-900 font-medium">{ns.displayName ?? ns.name}</td>
                  <td className="py-2 pr-4 uppercase text-gray-600">{ns.environment ?? '—'}</td>
                  <td className="py-2 pr-4">
                    <span
                      className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                        ns.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'
                      }`}
                    >
                      {ns.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="py-2 text-gray-600">{new Date(ns.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </SandboxPanel>
    </div>
  );
}
