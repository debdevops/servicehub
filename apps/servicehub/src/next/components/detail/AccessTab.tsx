import { Check, Minus } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Pager } from '../../../components/ui/Pager'
import { usePagedList } from '../../../hooks/usePagedList'
import { useMe } from '../../../hooks/useIdentity'
import { useGrants, useSettings } from '../../../hooks/useSettings'
import { useDeclareCanSend, useSetOwnerChannel, useTestConnection } from '../../../hooks/useNamespaces'
import { toProblem } from '../../../lib/api/client'
import type { ConnectedComponent } from '../../../lib/api/components'
import type { Role } from '../../../lib/api/identity'
import type { Namespace } from '../../../lib/api/namespaces'
import { formatWhen } from '../../../lib/format'
import { abilities, grantScope, granteeWords, roleRows } from '../../lib/roles'
import { credentialWords } from '../../lib/words'
import { nextEntry, nextHref } from '../../nav/navigation'
import { Th } from '../Tip'
import { Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'
const roleTone: Record<Role, string> = { Viewer: 'bg-slate-100 text-slate-700', Operator: 'bg-slate-100 text-slate-700', Approver: 'bg-amber-50 text-amber-800', Admin: 'bg-purple-50 text-purple-800' }

/** Credentials that carry a secret ServiceHub keeps. An identity-based one stores none, so nothing is said to be stored. */
const SECRET_AUTH = new Set(['connectionString', 'awsAccessKey', 'gcpServiceAccount', 'servicePrincipal'])

/**
 * The Access tab of one connection (build-plan unit D5; design `p05d-service-bus-access`): who may do what here, what each role can
 * do, and how this connection signs in. Never shows a credential, and never says what it cannot know: whether the credential may
 * SEND is not tested (testing it would mean sending), so it is said to be unchecked rather than "Yes".
 */
export function AccessTab({ component: c, namespace: ns }: { component: ConnectedComponent; namespace: Namespace | undefined }) {
  const me = useMe()
  const governed = me.data?.governanceActive === true
  const grants = useGrants(governed)
  const test = useTestConnection()
  const declare = useDeclareCanSend()
  const setOwner = useSetOwnerChannel()
  const channels = (useSettings().data?.notifications.channels ?? []).filter((ch) => ch.enabled)
  const mine = (grants.data ?? []).filter((g) => g.namespaceId === null || g.namespaceId === c.namespaceId)
  const { items, pager } = usePagedList(mine)
  const yourRole = me.data ? (me.data.namespaceRecoverRoles?.[c.namespaceId] ?? me.data.recoverRole ?? me.data.effectiveRole) : undefined
  const now = new Date()
  const lastTest = test.data ? { at: test.data.testedAt, ok: test.data.isConnected } : ns?.lastConnectionTestAt ? { at: ns.lastConnectionTestAt, ok: ns.lastConnectionTestSucceeded === true } : null

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
      <div className="min-w-0 space-y-4">
        <section aria-labelledby="who-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
          <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
            <h2 id="who-title" className="text-base font-semibold">Who may do what here<Tip label="section:Who may do what here" /></h2>
            <Link to={nextHref(nextEntry('configuration'))} className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-bold hover:bg-[var(--color-surface-muted)]">Manage access</Link>
          </div>
          {me.data && (
            <p className="border-t border-[var(--color-border)] px-4 py-2.5 text-sm" data-testid="your-role">
              {me.data.actor.isSession ? <>You are acting as <strong>this browser session</strong></> : <>You are <strong>{me.data.actor.label || me.data.actor.identity}</strong></>}
              {yourRole ? <>, with the <strong>{yourRole}</strong> role here.</> : ', with no role here.'}
            </p>
          )}
          {me.isPending ? (
            <p role="status" className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">Loading…</p>
          ) : me.isError ? (
            <p role="alert" className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-red-800">Who you are could not be read, so roles are not shown.</p>
          ) : !governed ? (
            <p className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">No role has been granted yet, so everyone who reaches this ServiceHub acts as Admin. Granting the first role turns roles on.</p>
          ) : grants.isError ? (
            <p className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">Only an Admin can list who has access. {me.data.grantors && me.data.grantors.length > 0 ? `${me.data.grantors.join(', ')} can.` : 'The server’s administrator can.'}</p>
          ) : grants.isPending ? (
            <p role="status" className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">Loading…</p>
          ) : mine.length === 0 ? (
            <p className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">No role is granted that applies to this connection.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full border-t border-[var(--color-border)]">
                <caption className="sr-only">Roles that apply to {c.name}</caption>
                <thead><tr><Th>Who</Th><Th>Kind</Th><Th>Role</Th><Th>Applies to</Th><Th>Since</Th><Th>Granted by</Th></tr></thead>
                <tbody>
                  {items.map((g) => (
                    <tr key={g.id} className="border-t border-[var(--color-border)]">
                      <th scope="row" className={`${td} text-left font-semibold`}>{g.granteeIdentity}</th>
                      <td className={td}>{granteeWords(g.granteeKind)}</td>
                      <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${roleTone[g.role]}`}>{g.role}</span></td>
                      <td className={td}>{grantScope(g)}</td>
                      <td className={td}>{formatWhen(g.grantedAt, now)}</td>
                      <td className={td}>{g.grantedByIdentity}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {governed && !grants.isError && !grants.isPending && mine.length > 0 && <Pager {...pager} label="roles that apply here" />}
          <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">A role limits actions. It never hides anything you are already allowed to read.</p>
        </section>

        <section aria-labelledby="roles-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
          <h2 id="roles-title" className="px-4 pt-4 text-base font-semibold">What each role can do<Tip label="section:What each role can do" /></h2>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">What each role can do</caption>
              <thead><tr><Th>Role</Th>{abilities.map((a) => <Th key={a.id} label={a.label}>{a.label}</Th>)}</tr></thead>
              <tbody>
                {roleRows.map((r) => (
                  <tr key={r.role} className="border-t border-[var(--color-border)]">
                    <th scope="row" className={`${td} text-left font-semibold`}>{r.role}</th>
                    {abilities.map((a) => {
                      const v = r.can[a.id]
                      return (
                        <td key={a.id} className={td}>
                          {v === true ? <><Check className="h-4 w-4" aria-hidden="true" /><span className="sr-only">Yes</span></> : v === false ? <><Minus className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" /><span className="sr-only">No</span></> : v}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>

      <section aria-labelledby="conn-title" className="h-fit rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <h2 id="conn-title" className="mb-2 text-base font-semibold">This connection<Tip label="section:This connection" /></h2>
        <dl className="text-sm">
          <Fact label="Connected with">{ns ? credentialWords(ns.authType) : '—'}</Fact>
          <Fact label="Read (listen)">{c.reachable ? <span className="font-semibold text-green-700">Yes</span> : <span className="font-semibold text-amber-800">Not known: it cannot be reached</span>}</Fact>
          <Fact label="Send">
            {ns?.canSend === false ? <>Read only, as declared. ServiceHub will not replay or send through this connection.</>
              : ns?.canSend === true ? <>Read and send, as declared. The cloud itself was not asked: testing it would mean sending a message.</>
              : <>Not said. Treated as may send. The cloud itself was not asked: testing it would mean sending a message.</>}
          </Fact>
          <Fact label="Told">
            {channels.find((ch) => ch.id === ns?.ownerChannelId)?.label ?? 'Every enabled channel'}
          </Fact>
          <Fact label="Tested">{lastTest ? <>{formatWhen(lastTest.at, now)} · {lastTest.ok ? 'worked' : 'did not work'}</> : 'Not tested yet'}</Fact>
          <Fact label="Stored">
            {ns === undefined ? '—' : SECRET_AUTH.has(ns.authType) ? 'Encrypted on this machine, never shown again' : 'No secret is stored: it signs in as an identity'}
          </Fact>
        </dl>
        {ns && me.data && (
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <button type="button" disabled={declare.isPending}
              onClick={() => declare.mutate({ id: ns.id, canSend: ns.canSend === false })}
              className="rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-bold hover:bg-[var(--color-surface-muted)] disabled:opacity-60">
              {ns.canSend === false ? 'Allow send' : 'Make read only'}
            </button>
            <span className="text-xs text-[var(--color-text-muted)]">Admin only.</span>
          </div>
        )}
        {ns && me.data && channels.length > 0 && (
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <label htmlFor="owner-channel" className="text-xs font-semibold">Tell only</label>
            <select id="owner-channel" value={ns.ownerChannelId ?? ''} disabled={setOwner.isPending}
              onChange={(e) => setOwner.mutate({ id: ns.id, channelId: e.target.value || null })}
              className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2 py-1.5 text-xs">
              <option value="">Every enabled channel</option>
              {channels.map((ch) => <option key={ch.id} value={ch.id}>{ch.label}</option>)}
            </select>
            <span className="text-xs text-[var(--color-text-muted)]">Admin only.</span>
          </div>
        )}
        {setOwner.isError && <p role="alert" className="mt-2 text-sm text-red-800">{toProblem(setOwner.error).message}</p>}
        {declare.isError && <p role="alert" className="mt-2 text-sm text-red-800">{toProblem(declare.error).message}</p>}
        {test.isError && <p role="alert" className="mt-2 text-sm text-red-800">{toProblem(test.error).message}</p>}
        {test.data && !test.data.isConnected && <p role="alert" className="mt-2 text-sm text-amber-900">{test.data.message}</p>}
        {test.data?.isConnected && <p role="status" className="mt-2 text-sm text-green-800">The connection worked.</p>}
        <button type="button" disabled={test.isPending || !ns} onClick={() => ns && test.mutate(ns.id)}
          className="mt-3 rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-bold hover:bg-[var(--color-surface-muted)] disabled:opacity-60">
          {test.isPending ? 'Testing…' : 'Test again'}
        </button>
      </section>
    </div>
  )
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 last:border-0"><dt className="w-28 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 font-semibold">{children}</dd></div>
}
