import { useCallback, useMemo, useState } from 'react';
import {
  Users, RefreshCw, Plus, Trash2, ShieldCheck, KeyRound, User, CheckCircle2, XCircle, Lock, AlertTriangle,
  ChevronRight, Layers, UserCog, X, Search,
} from 'lucide-react';
import {
  useGovernanceGrants, useGrantGovernanceRole, useRevokeGovernanceGrant,
} from '@servicehub/ui-shared/hooks/useGovernanceGrants';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useFocusTrap } from '@servicehub/ui-shared/hooks/useFocusTrap';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import {
  GOVERNANCE_ROLE_EXPLANATIONS, type GovernanceGrant, type GovernanceRole, type GranteeKind, type PillarKind,
} from '@servicehub/ui-shared/lib/api/governance';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import {
  Callout, Card, DetailPanel, EmptyBlock, ErrorBlock, FilterSelect, LoadingBlock, PageHeader, Pill, SearchInput,
  StatCard, Tabs, buttonClass, formatDateTime, plural,
} from '@/components/autonomy/ui';
import {
  GOVERNED_ACTIONS, ROLE_ORDER, ROLE_TONE, describeGrantee, evaluateGrantee, groupGrantees, roleMeets, type Grantee,
} from '@/components/autonomy/governanceModel';

const GRANTEE_KINDS: readonly GranteeKind[] = ['User', 'ApiKey'];
const PILLARS: readonly PillarKind[] = ['Recover', 'Investigate', 'Correlate', 'Prevent'];

type Tab = 'people' | 'grants' | 'roles';

function RoleBadge({ role }: { role: GovernanceRole }) {
  return <Pill tone={ROLE_TONE[role]} title={GOVERNANCE_ROLE_EXPLANATIONS[role]}>{role}</Pill>;
}

function initials(identity: string): string {
  const core = identity.includes(':') ? identity.split(':').slice(1).join(':') : identity;
  const parts = core.split(/[\s@._-]+/).filter(Boolean);
  return ((parts[0]?.[0] ?? '?') + (parts[1]?.[0] ?? '')).toUpperCase();
}

function GranteeAvatar({ grantee }: { grantee: Pick<Grantee, 'identity' | 'kind'> }) {
  return grantee.kind === 'ApiKey' ? (
    <div className="w-8 h-8 rounded-full bg-amber-50 text-amber-700 ring-1 ring-amber-200 flex items-center justify-center shrink-0" aria-hidden="true">
      <KeyRound className="w-4 h-4" />
    </div>
  ) : (
    <div className="w-8 h-8 rounded-full bg-primary-50 text-primary-700 ring-1 ring-primary-200 flex items-center justify-center text-xs font-bold shrink-0" aria-hidden="true">
      {initials(grantee.identity)}
    </div>
  );
}

/** Modal form for a new grant. Warns when the first grant would activate Governance. */
function NewGrantDialog({
  onClose, initialIdentity, activatesGovernance,
}: { onClose: () => void; initialIdentity?: string; activatesGovernance: boolean }) {
  const { data: namespaces } = useNamespaces();
  const grant = useGrantGovernanceRole();
  const dialogRef = useFocusTrap<HTMLDivElement>(true);

  const [granteeIdentity, setGranteeIdentity] = useState(initialIdentity ?? '');
  const [granteeKind, setGranteeKind] = useState<GranteeKind>('User');
  const [role, setRole] = useState<GovernanceRole>('Viewer');
  const [namespaceId, setNamespaceId] = useState('');
  const [pillarKind, setPillarKind] = useState<'' | PillarKind>('');

  const canSubmit = granteeIdentity.trim().length > 0 && !grant.isPending;
  const field = 'w-full text-sm border border-gray-200 rounded-lg px-3 py-2 bg-white focus:outline-none focus:ring-2 focus:ring-primary-500';

  return (
    <>
      <div className="fixed inset-0 bg-black/30 z-40" onClick={onClose} aria-hidden="true" />
      <div ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby="new-grant-title" className="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg">
          <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
            <h2 id="new-grant-title" className="font-semibold text-gray-900">New grant</h2>
            <button type="button" onClick={onClose} aria-label="Close" className="p-1 rounded hover:bg-gray-100">
              <X className="w-4 h-4 text-gray-500" />
            </button>
          </div>
          <div className="p-5 space-y-3">
            {activatesGovernance && (
              <Callout tone="amber" icon={AlertTriangle} title="This first grant turns Governance on">
                From then on, only identities with a grant can act. Grant yourself Admin too, or you may lose access to this page.
              </Callout>
            )}
            <div className="grid grid-cols-1 @xl:grid-cols-2 gap-3">
              <div className="@xl:col-span-2">
                <label htmlFor="grantee-identity" className="block text-xs font-semibold text-gray-600 mb-1">Who</label>
                <input
                  id="grantee-identity"
                  type="text"
                  value={granteeIdentity}
                  onChange={e => setGranteeIdentity(e.target.value)}
                  placeholder="entra:oid, ApiKey:name, or an OwnerId"
                  className={field}
                />
              </div>
              <div>
                <label htmlFor="grantee-kind" className="block text-xs font-semibold text-gray-600 mb-1">Identity type</label>
                <select id="grantee-kind" value={granteeKind} onChange={e => setGranteeKind(e.target.value as GranteeKind)} className={field}>
                  {GRANTEE_KINDS.map(k => <option key={k} value={k}>{k === 'ApiKey' ? 'API key' : 'User'}</option>)}
                </select>
              </div>
              <div>
                <label htmlFor="grant-role" className="block text-xs font-semibold text-gray-600 mb-1">Role</label>
                <select id="grant-role" value={role} onChange={e => setRole(e.target.value as GovernanceRole)} className={field}>
                  {ROLE_ORDER.map(r => <option key={r} value={r}>{r}</option>)}
                </select>
              </div>
              <p className="@xl:col-span-2 text-xs text-gray-500 -mt-1">{GOVERNANCE_ROLE_EXPLANATIONS[role]}</p>
              <div>
                <label htmlFor="grant-namespace" className="block text-xs font-semibold text-gray-600 mb-1">Where</label>
                <select id="grant-namespace" value={namespaceId} onChange={e => setNamespaceId(e.target.value)} className={field}>
                  <option value="">Fleet-wide (every namespace)</option>
                  {(namespaces ?? []).map(ns => <option key={ns.id} value={ns.id}>{ns.displayName || ns.name}</option>)}
                </select>
              </div>
              <div>
                <label htmlFor="grant-pillar" className="block text-xs font-semibold text-gray-600 mb-1">Pillar</label>
                <select id="grant-pillar" value={pillarKind} onChange={e => setPillarKind(e.target.value as '' | PillarKind)} className={field}>
                  <option value="">All pillars</option>
                  {PILLARS.map(p => <option key={p} value={p}>{p}</option>)}
                </select>
              </div>
            </div>
            <p className="text-xs text-gray-500 flex items-start gap-1.5">
              <Lock className="w-3.5 h-3.5 shrink-0 mt-0.5" />
              A grant lets someone take part in decisions. It never grants autonomy, and never bypasses the Eligibility Gate or the production floor.
            </p>
          </div>
          <div className="px-5 py-3 border-t border-gray-100 flex justify-end gap-2">
            <button type="button" onClick={onClose} className={buttonClass.secondary}>Cancel</button>
            <button
              type="button"
              onClick={() =>
                grant.mutate(
                  { granteeIdentity: granteeIdentity.trim(), granteeKind, role, namespaceId: namespaceId || null, pillarKind: pillarKind || null },
                  { onSuccess: onClose },
                )
              }
              disabled={!canSubmit}
              className={buttonClass.primary}
            >
              <Plus className="w-4 h-4" /> Create grant
            </button>
          </div>
        </div>
      </div>
    </>
  );
}

/**
 * `/governance` — who may do what, where, and for which pillar. People & keys (grants grouped by
 * identity), the raw grants, and the role → action matrix exactly as the backend enforces it.
 *
 * "Governance does not grant autonomy": a role authorizes a person to take part in the autonomy
 * lifecycle (approve, replay, decide proposals, manage grants); autonomy itself is earned per
 * failure signature from verified evidence, and no role can set it. Listing and changing grants
 * requires the admin API-key scope and, once Governance is active, the Admin role — a non-admin
 * sees their own role and the permission matrix instead of an error.
 */
export default function GovernanceGrantsPage() {
  const { isDemoMode } = useDemoContext();
  const { data: me } = useMe();
  const { data: namespaces } = useNamespaces();
  const { data: grants, isLoading, isError, error, refetch, isFetching } = useGovernanceGrants();
  const revoke = useRevokeGovernanceGrant();

  const [tab, setTab] = useState<Tab>('people');
  const [selectedIdentity, setSelectedIdentity] = useState<string | null>(null);
  const [newGrant, setNewGrant] = useState<{ identity?: string } | null>(null);
  const [pendingRevoke, setPendingRevoke] = useState<GovernanceGrant | null>(null);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState<'' | GovernanceRole>('');

  const status = (error as { response?: { status?: number } } | null)?.response?.status;
  const forbidden = isError && (status === 403 || status === 401);
  const activeGrants = useMemo(() => (grants ?? []).filter(g => !g.revokedAt), [grants]);
  const grantees = useMemo(() => groupGrantees(grants ?? []), [grants]);
  const governanceInactive = !isLoading && !isError && !isDemoMode && (grants ?? []).length === 0;

  const namespaceName = useCallback(
    (id: string | null) => {
      if (!id) return 'Fleet-wide';
      const ns = namespaces?.find(n => n.id === id);
      return ns ? ns.displayName || ns.name : id;
    },
    [namespaces],
  );

  const q = search.trim().toLowerCase();
  const visibleGrantees = grantees.filter(g => (!roleFilter || g.highestRole === roleFilter) && (!q || g.identity.toLowerCase().includes(q)));
  const visibleGrants = (grants ?? []).filter(
    g => (!roleFilter || g.role === roleFilter) && (!q || g.granteeIdentity.toLowerCase().includes(q) || namespaceName(g.namespaceId).toLowerCase().includes(q)),
  );
  const selectedGrantee = grantees.find(g => g.identity === selectedIdentity) ?? null;
  const fleetAdmins = grantees.filter(g => g.grants.some(gr => gr.role === 'Admin' && gr.namespaceId == null)).length;
  const myRole = me?.governanceRole ?? null;

  const closeDetail = useCallback(() => setSelectedIdentity(null), []);

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <PageHeader
        icon={Users}
        tone="red"
        title="Governance"
        subtitle="Who may approve, operate and manage decisions in Autonomous ServiceHub — scoped by namespace and pillar."
        actions={
          <>
            <button type="button" onClick={() => setNewGrant({})} disabled={isDemoMode || forbidden} className={buttonClass.primary}>
              <Plus className="w-4 h-4" /> New grant
            </button>
            <button type="button" onClick={() => refetch()} disabled={isFetching || isDemoMode} className={buttonClass.secondary}>
              <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} /> Refresh
            </button>
          </>
        }
      >
        {isDemoMode && (
          <Callout tone="amber" className="mt-3">
            Demo Mode — Governance grants have no fixture data, so this view is always empty here.
          </Callout>
        )}
      </PageHeader>

      <div className="flex-1 overflow-auto p-4 sm:p-6">
        <div className="@container max-w-7xl mx-auto space-y-5">
          <div className="grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(0,22rem)] gap-3">
            <div className="grid grid-cols-2 @3xl:grid-cols-4 gap-3">
              <StatCard
                icon={UserCog}
                tone="red"
                label="Your fleet-wide role"
                value={<span className="text-lg">{myRole ?? 'None'}</span>}
                hint={myRole ? GOVERNANCE_ROLE_EXPLANATIONS[myRole as GovernanceRole]?.split('.')[0] : isDemoMode ? 'Demo Mode' : 'Only namespace-scoped grants, if any'}
              />
              <StatCard icon={Users} tone="blue" label="People & keys" value={forbidden || isError ? '—' : grantees.length} hint="With an active grant" />
              <StatCard icon={ShieldCheck} tone="green" label="Active grants" value={forbidden || isError ? '—' : activeGrants.length} hint={forbidden ? 'Admin only' : `${fleetAdmins} fleet-wide Admin${fleetAdmins === 1 ? '' : 's'}`} />
              <StatCard
                icon={Layers}
                tone="violet"
                label="Namespace-scoped"
                value={forbidden || isError ? '—' : activeGrants.filter(g => g.namespaceId != null).length}
                hint="Grants limited to one namespace"
              />
            </div>
            <Callout tone="blue" icon={Lock} title="Governance does not grant autonomy.">
              It gives people permission to take part in the autonomy lifecycle — approving, replaying, deciding proposals. Autonomy is earned from
              verified evidence, and no role can set it.
            </Callout>
          </div>

          {governanceInactive && (
            <Callout tone="amber" icon={AlertTriangle} title="Governance is not active">
              No grant exists yet, so every authenticated caller currently has unrestricted access. Create the first grant — including an Admin grant
              for yourself — to turn it on.
            </Callout>
          )}

          <Tabs<Tab>
            label="Governance sections"
            active={tab}
            onChange={setTab}
            tabs={[
              { id: 'people', label: 'People & access', count: forbidden || isError ? undefined : grantees.length },
              { id: 'grants', label: 'Grants', count: forbidden || isError ? undefined : activeGrants.length },
              { id: 'roles', label: 'Roles & permissions' },
            ]}
          />

          {tab === 'roles' ? (
            <RolesMatrix />
          ) : isLoading ? (
            <LoadingBlock label="Loading grants…" />
          ) : forbidden ? (
            <Card>
              <EmptyBlock
                icon={Lock}
                title="Only a Governance Admin can see and manage grants"
                action={<button type="button" onClick={() => setTab('roles')} className={buttonClass.secondary}>See what each role can do</button>}
              >
                Your fleet-wide role is <span className="font-semibold">{myRole ?? 'none'}</span>. Ask an Admin if you need a different role or scope.
              </EmptyBlock>
            </Card>
          ) : isError ? (
            <Card>
              <ErrorBlock title="Failed to load Governance grants" detail="Requires the admin API-key scope and Governance Admin role." onRetry={() => refetch()} />
            </Card>
          ) : (grants ?? []).length === 0 ? (
            <Card>
              <EmptyBlock
                icon={ShieldCheck}
                title="No grants configured"
                action={!isDemoMode ? <button type="button" onClick={() => setNewGrant({})} className={buttonClass.primary}><Plus className="w-4 h-4" /> Create first grant</button> : undefined}
              >
                Governance is not yet activated for this owner — every caller has unrestricted access until the first grant is created.
              </EmptyBlock>
            </Card>
          ) : (
            <div className="grid grid-cols-1 @6xl:grid-cols-[minmax(0,1fr)_400px] gap-5 items-start">
              <Card bodyClassName="p-0">
                <div className="p-3 border-b border-gray-100 flex flex-wrap gap-2">
                  <SearchInput value={search} onChange={setSearch} placeholder={tab === 'people' ? 'Search people and keys…' : 'Search grantee or namespace…'} label="Search grants" />
                  <FilterSelect
                    label="Filter by role"
                    value={roleFilter}
                    onChange={setRoleFilter}
                    options={[{ value: '', label: 'All roles' }, ...ROLE_ORDER.map(r => ({ value: r, label: r }))]}
                  />
                </div>
                {tab === 'people' ? (
                  visibleGrantees.length === 0 ? (
                    <EmptyBlock icon={Search} title="No one matches" />
                  ) : (
                    <div className="overflow-x-auto">
                      <table className="w-full text-sm" aria-label="People and access">
                        <thead className="bg-gray-50 text-xs text-gray-500">
                          <tr>
                            <th scope="col" className="px-4 py-2.5 text-left font-semibold">Who</th>
                            <th scope="col" className="px-4 py-2.5 text-left font-semibold">Highest role</th>
                            <th scope="col" className="px-4 py-2.5 text-left font-semibold">Where</th>
                            <th scope="col" className="px-4 py-2.5 text-left font-semibold">Pillars</th>
                            <th scope="col" className="w-8"><span className="sr-only">Open</span></th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                          {visibleGrantees.map(g => (
                            <tr
                              key={g.identity}
                              tabIndex={0}
                              aria-selected={g.identity === selectedIdentity}
                              onClick={() => setSelectedIdentity(g.identity)}
                              onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSelectedIdentity(g.identity); } }}
                              className={`cursor-pointer focus:outline-none focus:bg-primary-50 ${g.identity === selectedIdentity ? 'bg-primary-50' : 'hover:bg-gray-50'}`}
                            >
                              <td className="px-4 py-3">
                                <div className="flex items-center gap-2.5">
                                  <GranteeAvatar grantee={g} />
                                  <div className="min-w-0">
                                    <div className="text-xs font-mono text-gray-800 break-all">{g.identity}</div>
                                    <div className="text-[11px] text-gray-500">{describeGrantee(g.identity) ?? (g.kind === 'ApiKey' ? 'API key' : 'User')} · {plural(g.grants.length, 'grant')}</div>
                                  </div>
                                </div>
                              </td>
                              <td className="px-4 py-3"><RoleBadge role={g.highestRole} /></td>
                              <td className="px-4 py-3 text-xs text-gray-600">
                                {g.fleetWide ? 'Fleet-wide' : g.namespaceIds.map(namespaceName).join(', ')}
                              </td>
                              <td className="px-4 py-3 text-xs text-gray-600">{g.pillars ? g.pillars.join(', ') : 'All pillars'}</td>
                              <td className="pr-3 text-gray-400"><ChevronRight className="w-4 h-4" /></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )
                ) : visibleGrants.length === 0 ? (
                  <EmptyBlock icon={Search} title="No grants match" />
                ) : (
                  <div className="overflow-x-auto">
                    <table className="w-full text-sm" aria-label="Governance grants">
                      <thead className="bg-gray-50 text-xs text-gray-500">
                        <tr>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Grantee</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Role</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Namespace</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Pillar</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold">Granted</th>
                          <th scope="col" className="px-4 py-2.5 text-left font-semibold"><span className="sr-only">Action</span></th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {visibleGrants.map(g => (
                          <tr key={g.id} className={g.revokedAt ? 'opacity-50' : ''}>
                            <td className="px-4 py-3">
                              <div className="text-xs font-mono text-gray-800 break-all">{g.granteeIdentity}</div>
                              <div className="text-[11px] text-gray-500">{g.granteeKind === 'ApiKey' ? 'API key' : 'User'}</div>
                            </td>
                            <td className="px-4 py-3"><RoleBadge role={g.role} /></td>
                            <td className="px-4 py-3 text-xs text-gray-600">{namespaceName(g.namespaceId)}</td>
                            <td className="px-4 py-3 text-xs text-gray-600">{g.pillarKind ?? 'All pillars'}</td>
                            <td className="px-4 py-3 text-xs text-gray-500 whitespace-nowrap" title={`Granted by ${g.grantedByIdentity}`}>{formatDateTime(g.grantedAt)}</td>
                            <td className="px-4 py-3">
                              {g.revokedAt ? (
                                <span className="text-xs text-gray-400">Revoked</span>
                              ) : (
                                <button type="button" onClick={() => setPendingRevoke(g)} disabled={revoke.isPending} className="inline-flex items-center gap-1 text-xs font-medium text-red-600 hover:text-red-700 disabled:opacity-50">
                                  <Trash2 className="w-3.5 h-3.5" /> Revoke
                                </button>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>

              {tab === 'people' && selectedGrantee ? (
                <GranteeDetail
                  grantee={selectedGrantee}
                  namespaceName={namespaceName}
                  onClose={closeDetail}
                  onRevoke={setPendingRevoke}
                  onAddGrant={() => setNewGrant({ identity: selectedGrantee.identity })}
                />
              ) : (
                <Card title="Least privilege, by design" icon={ShieldCheck} tone="green">
                  <ul className="space-y-2 text-xs text-gray-600">
                    <li>Scope grants to one namespace and one pillar where you can — a grant with no namespace applies everywhere.</li>
                    <li>A person with their own grants never inherits the owner-level grant; their access is exactly what they're granted.</li>
                    <li>Every grant and revocation is recorded in the Audit Trail.</li>
                  </ul>
                </Card>
              )}
            </div>
          )}
        </div>
      </div>

      {newGrant && (
        <NewGrantDialog
          onClose={() => setNewGrant(null)}
          initialIdentity={newGrant.identity}
          activatesGovernance={(grants ?? []).length === 0}
        />
      )}

      <ConfirmDialog
        isOpen={pendingRevoke !== null}
        title="Revoke this grant?"
        message={
          pendingRevoke
            ? `${pendingRevoke.granteeIdentity} will lose ${pendingRevoke.role} (${namespaceName(pendingRevoke.namespaceId)}, ${pendingRevoke.pillarKind ?? 'all pillars'}) on their next request. If this is your own Admin grant, you may lose access to this page.`
            : ''
        }
        confirmLabel="Revoke grant"
        variant="danger"
        isConfirming={revoke.isPending}
        onConfirm={() => {
          if (pendingRevoke) revoke.mutate(pendingRevoke.id, { onSettled: () => setPendingRevoke(null) });
        }}
        onCancel={() => setPendingRevoke(null)}
      />
    </div>
  );
}

function GranteeDetail({
  grantee, namespaceName, onClose, onRevoke, onAddGrant,
}: {
  grantee: Grantee;
  namespaceName: (id: string | null) => string;
  onClose: () => void;
  onRevoke: (grant: GovernanceGrant) => void;
  onAddGrant: () => void;
}) {
  const evaluated = evaluateGrantee(grantee.grants);
  const can = evaluated.filter(e => e.allowed);
  const cannot = evaluated.filter(e => !e.allowed);

  return (
    <DetailPanel
      title={<span className="font-mono break-all">{grantee.identity}</span>}
      badge={<RoleBadge role={grantee.highestRole} />}
      subtitle={`${describeGrantee(grantee.identity) ?? (grantee.kind === 'ApiKey' ? 'API key' : 'User')} · ${plural(grantee.grants.length, 'active grant')}`}
      onClose={onClose}
      footer={
        <button type="button" onClick={onAddGrant} className={`${buttonClass.secondary} w-full`}>
          <Plus className="w-4 h-4" /> Add a grant for this identity
        </button>
      }
    >
      <section>
        <h3 className="text-xs font-semibold text-gray-500 mb-1.5">Can</h3>
        {can.length === 0 ? (
          <p className="text-xs text-gray-500">Read-only — no governed action.</p>
        ) : (
          <ul className="space-y-1.5">
            {can.map(({ action, where }) => (
              <li key={action.id} className="flex items-start gap-2 text-xs">
                <CheckCircle2 className="w-4 h-4 text-green-600 shrink-0" />
                <span className="text-gray-800">{action.label} <span className="text-gray-400">— {where}</span></span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section>
        <h3 className="text-xs font-semibold text-gray-500 mb-1.5">Cannot</h3>
        <ul className="space-y-1.5">
          {cannot.map(({ action }) => (
            <li key={action.id} className="flex items-start gap-2 text-xs">
              <XCircle className={`w-4 h-4 shrink-0 ${action.minRole ? 'text-gray-300' : 'text-red-500'}`} />
              <span className={action.minRole ? 'text-gray-500' : 'text-gray-800'}>
                {action.label}
                {!action.minRole && <span className="text-gray-400"> — no role can</span>}
              </span>
            </li>
          ))}
        </ul>
      </section>
      <section>
        <h3 className="text-xs font-semibold text-gray-500 mb-1.5">Grants</h3>
        <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
          {grantee.grants.map(g => (
            <li key={g.id} className="px-3 py-2 flex items-center justify-between gap-2 text-xs">
              <div className="min-w-0">
                <div className="flex items-center gap-1.5"><RoleBadge role={g.role} /> <span className="text-gray-700">{namespaceName(g.namespaceId)}</span></div>
                <div className="text-gray-400 mt-0.5">{g.pillarKind ?? 'All pillars'} · since {formatDateTime(g.grantedAt)}</div>
              </div>
              <button type="button" onClick={() => onRevoke(g)} className="inline-flex items-center gap-1 text-xs font-medium text-red-600 hover:text-red-700">
                <Trash2 className="w-3.5 h-3.5" /> Revoke
              </button>
            </li>
          ))}
        </ul>
      </section>
    </DetailPanel>
  );
}

function RolesMatrix() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 @xl:grid-cols-2 @5xl:grid-cols-4 gap-3">
        {ROLE_ORDER.map(role => (
          <div key={role} className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
            <RoleBadge role={role} />
            <p className="text-xs text-gray-600 mt-2">{GOVERNANCE_ROLE_EXPLANATIONS[role]}</p>
          </div>
        ))}
      </div>
      <Card title="What each role can do" icon={ShieldCheck} tone="green" bodyClassName="p-0">
        <div className="overflow-x-auto">
          <table className="w-full text-sm" aria-label="Role permissions">
            <thead className="bg-gray-50 text-xs text-gray-500">
              <tr>
                <th scope="col" className="px-4 py-2.5 text-left font-semibold">Action</th>
                {ROLE_ORDER.map(r => <th key={r} scope="col" className="px-3 py-2.5 text-center font-semibold">{r}</th>)}
                <th scope="col" className="px-4 py-2.5 text-left font-semibold">Scope</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {GOVERNED_ACTIONS.map(action => (
                <tr key={action.id} className={action.minRole ? '' : 'bg-red-50/40'}>
                  <td className="px-4 py-2.5">
                    <div className="text-sm text-gray-800">{action.label}</div>
                    <div className="text-[11px] text-gray-400">{action.enforcedBy}</div>
                  </td>
                  {ROLE_ORDER.map(r => (
                    <td key={r} className="px-3 py-2.5 text-center">
                      {action.minRole && roleMeets(r, action.minRole) ? (
                        <CheckCircle2 className="w-4 h-4 text-green-600 inline" aria-label="Allowed" />
                      ) : (
                        <span className="text-gray-300" aria-label="Not allowed">—</span>
                      )}
                    </td>
                  ))}
                  <td className="px-4 py-2.5 text-xs text-gray-600">
                    {!action.minRole
                      ? <span className="text-red-700 font-medium">Nobody, by design</span>
                      : action.scope === 'fleet'
                        ? 'Fleet-wide grant'
                        : action.pillar === 'any'
                          ? "Per namespace, proposal's pillar"
                          : `Per namespace, ${action.pillar} pillar`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="px-4 py-2.5 text-[11px] text-gray-500 border-t border-gray-100">
          Reading evidence, proposals and autonomy needs only an API-key read scope. Viewer is the explicit read-only grant. The server is always
          the authority; this table mirrors its checks.
        </p>
      </Card>
      <Callout tone="gray" icon={User} title="Why Approver ranks above Operator">
        Roles are cumulative. An Approver can do everything an Operator can, plus decide Playbook proposals and approve another person's
        production elevation.
      </Callout>
    </div>
  );
}
