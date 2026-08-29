import { useState } from 'react';
import { ShieldCheck, AlertCircle, RefreshCw, Info, Plus, Trash2 } from 'lucide-react';
import {
  useGovernanceGrants,
  useGrantGovernanceRole,
  useRevokeGovernanceGrant,
} from '@servicehub/ui-shared/hooks/useGovernanceGrants';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { EmptyState } from '@/components/EmptyState';
import { GOVERNANCE_ROLE_EXPLANATIONS } from '@servicehub/ui-shared/lib/api/governance';
import type { GranteeKind, GovernanceRole, PillarKind, GovernanceGrant } from '@servicehub/ui-shared/lib/api/governance';

const GRANTEE_KINDS: readonly GranteeKind[] = ['User', 'ApiKey'];
const ROLES: readonly GovernanceRole[] = ['Viewer', 'Operator', 'Approver', 'Admin'];
const PILLARS: readonly PillarKind[] = ['Recover', 'Investigate', 'Correlate', 'Prevent'];

const ROLE_COLORS: Record<GovernanceRole, string> = {
  Viewer: 'bg-gray-100 text-gray-600',
  Operator: 'bg-blue-100 text-blue-700',
  Approver: 'bg-purple-100 text-purple-700',
  Admin: 'bg-red-100 text-red-700',
};

function RoleBadge({ role }: { role: GovernanceRole }) {
  return (
    <span title={GOVERNANCE_ROLE_EXPLANATIONS[role]} className={`px-2 py-0.5 text-xs font-medium rounded-full ${ROLE_COLORS[role]}`}>
      {role}
    </span>
  );
}

/** Inline form for creating a new Governance grant — collapsed by default behind "New grant". */
function NewGrantForm({ onClose }: { onClose: () => void }) {
  const { data: namespaces } = useNamespaces();
  const grant = useGrantGovernanceRole();

  const [granteeIdentity, setGranteeIdentity] = useState('');
  const [granteeKind, setGranteeKind] = useState<GranteeKind>('User');
  const [role, setRole] = useState<GovernanceRole>('Viewer');
  const [namespaceId, setNamespaceId] = useState('');
  const [pillarKind, setPillarKind] = useState<'' | PillarKind>('');

  const canSubmit = granteeIdentity.trim().length > 0 && !grant.isPending;

  return (
    <div className="mt-3 p-4 rounded-lg bg-gray-50 border border-gray-200">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-semibold text-gray-500 mb-1">Grantee identity</label>
          <input
            type="text"
            value={granteeIdentity}
            onChange={e => setGranteeIdentity(e.target.value)}
            placeholder="entra:oid, ApiKey:name, or an OwnerId"
            className="w-full text-sm border border-gray-200 rounded-lg px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-teal-500"
          />
        </div>
        <div>
          <label className="block text-xs font-semibold text-gray-500 mb-1">Grantee kind</label>
          <select
            value={granteeKind}
            onChange={e => setGranteeKind(e.target.value as GranteeKind)}
            className="w-full text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            {GRANTEE_KINDS.map(k => (
              <option key={k} value={k}>{k}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-semibold text-gray-500 mb-1">Role</label>
          <select
            value={role}
            onChange={e => setRole(e.target.value as GovernanceRole)}
            title={GOVERNANCE_ROLE_EXPLANATIONS[role]}
            className="w-full text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            {ROLES.map(r => (
              <option key={r} value={r}>{r}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-semibold text-gray-500 mb-1">Pillar (optional — blank = all)</label>
          <select
            value={pillarKind}
            onChange={e => setPillarKind(e.target.value as '' | PillarKind)}
            className="w-full text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            <option value="">All pillars</option>
            {PILLARS.map(p => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>
        <div className="col-span-2">
          <label className="block text-xs font-semibold text-gray-500 mb-1">Namespace (optional — blank = fleet-wide)</label>
          <select
            value={namespaceId}
            onChange={e => setNamespaceId(e.target.value)}
            className="w-full text-sm border border-gray-200 rounded-lg px-3 py-1.5 bg-white focus:outline-none focus:ring-2 focus:ring-teal-500"
          >
            <option value="">Fleet-wide</option>
            {(namespaces ?? []).map(ns => (
              <option key={ns.id} value={ns.id}>{ns.displayName || ns.name}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="flex items-center gap-2 mt-3">
        <button
          onClick={() =>
            grant.mutate(
              {
                granteeIdentity: granteeIdentity.trim(),
                granteeKind,
                role,
                namespaceId: namespaceId || null,
                pillarKind: pillarKind || null,
              },
              { onSuccess: onClose },
            )
          }
          disabled={!canSubmit}
          className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-teal-600 hover:bg-teal-700 rounded-lg disabled:opacity-50"
        >
          <Plus className="w-3.5 h-3.5" /> Create grant
        </button>
        <button onClick={onClose} className="text-xs text-gray-500 hover:text-gray-700">
          Cancel
        </button>
      </div>
    </div>
  );
}

function GrantRow({ grant }: { grant: GovernanceGrant }) {
  const revoke = useRevokeGovernanceGrant();
  const isRevoked = grant.revokedAt !== null;

  return (
    <tr className={isRevoked ? 'opacity-50' : ''}>
      <td className="px-4 py-3 text-gray-800 font-mono text-xs break-all">{grant.granteeIdentity}</td>
      <td className="px-4 py-3 text-xs text-gray-600">{grant.granteeKind}</td>
      <td className="px-4 py-3"><RoleBadge role={grant.role} /></td>
      <td className="px-4 py-3 text-xs text-gray-600">{grant.namespaceId ?? 'Fleet-wide'}</td>
      <td className="px-4 py-3 text-xs text-gray-600">{grant.pillarKind ?? 'All pillars'}</td>
      <td className="px-4 py-3 text-gray-500 whitespace-nowrap font-mono text-xs">
        {new Date(grant.grantedAt).toLocaleString(undefined, { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' })}
      </td>
      <td className="px-4 py-3">
        {isRevoked ? (
          <span className="text-xs text-gray-400">Revoked</span>
        ) : (
          <button
            onClick={() => revoke.mutate(grant.id)}
            disabled={revoke.isPending}
            className="flex items-center gap-1 text-xs font-medium text-red-600 hover:text-red-700 disabled:opacity-50"
          >
            <Trash2 className="w-3.5 h-3.5" /> Revoke
          </button>
        )}
      </td>
    </tr>
  );
}

/**
 * `/governance` — Governance/RBAC grant management (M3 of the persistence wave, roadmap item 10's
 * enforcement layer): who holds which role, scoped to which namespace and pillar. This is the
 * admin surface over the grants `GovernanceAuthorizationFilter` reads at request time — creating
 * or revoking a grant here takes effect on the next request, no restart required. Requires the
 * `admin` API-key scope and, once Governance is active for this owner, the `Admin` Governance role
 * itself.
 */
export default function GovernanceGrantsPage() {
  const { isDemoMode } = useDemoContext();
  const [showNewGrantForm, setShowNewGrantForm] = useState(false);

  const { data: grants, isLoading, isError, refetch, isFetching } = useGovernanceGrants();

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <ShieldCheck className="w-5 h-5 text-teal-600" />
              Governance
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Who holds which role, scoped to which namespace and pillar. A grant with no namespace
              is fleet-wide; a grant with no pillar covers all four.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setShowNewGrantForm(v => !v)}
              disabled={isDemoMode}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-teal-600 hover:bg-teal-700 rounded-lg shadow-sm transition-all disabled:opacity-50"
            >
              <Plus className="w-4 h-4" /> New grant
            </button>
            <button
              onClick={() => refetch()}
              disabled={isFetching || isDemoMode}
              className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all disabled:opacity-50"
            >
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </div>
        </div>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode — Governance grants have no fixture data, so this view is always empty here.
          </div>
        )}

        {showNewGrantForm && <NewGrantForm onClose={() => setShowNewGrantForm(false)} />}
      </div>

      <div className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-teal-600" />
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load Governance grants</p>
              <p className="text-xs text-gray-400 mt-1">Requires the admin API-key scope and Governance Admin role.</p>
              <button onClick={() => refetch()} className="mt-3 px-4 py-2 text-sm text-teal-600 hover:text-teal-700 border border-teal-300 rounded-lg hover:bg-teal-50">
                Try Again
              </button>
            </div>
          </div>
        ) : (grants ?? []).length === 0 ? (
          <EmptyState
            icon={ShieldCheck}
            heading="No grants configured"
            subtext="Governance is not yet activated for this owner — every caller has unrestricted access until the first grant is created."
          />
        ) : (
          <table className="w-full text-sm" aria-label="Governance grants">
            <thead className="bg-gray-50 border-b border-gray-200 sticky top-0 z-10">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Grantee</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-20">Kind</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-24">Role</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500">Namespace</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-28">Pillar</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-40">Granted At</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-semibold text-gray-500 w-20">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {(grants ?? []).map(g => <GrantRow key={g.id} grant={g} />)}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
