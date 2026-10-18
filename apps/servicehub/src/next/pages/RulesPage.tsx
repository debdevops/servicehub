import { useMutation, useQueries, useQueryClient } from '@tanstack/react-query'
import { Check, Circle, Plus, Sparkles, Zap } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { NewRule, RuleCard, type RuleLinks } from '../../components/rules/AutoReplayPage'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { Pager } from '../../components/ui/Pager'
import { useComponents } from '../../hooks/useComponents'
import { useMe } from '../../hooks/useIdentity'
import { useNamespaces } from '../../hooks/useNamespaces'
import { usePagedList } from '../../hooks/usePagedList'
import { fetchRules, generateRules, type Rule } from '../../lib/api/rules'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { ruleTitle } from '../../lib/ruleName'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { cloudLabel, cloudOf, parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'
const rulesKey = (p: CloudProvider) => ['rules', p] as const

const waits = (seconds: number) => (seconds >= 60 ? `${Math.round(seconds / 60)} min` : `${seconds} s`)
const tripped = (r: Rule) => r.disabledReason === 'CircuitBreaker'

/** The state in a word, from what the server says about why a rule is off — never guessed. */
function stateOf(r: Rule): { text: string; chip: string } {
  if (r.enabled) return { text: 'On', chip: 'bg-green-50 text-green-800' }
  if (tripped(r)) return { text: 'Stopped itself', chip: 'bg-red-50 text-red-700' }
  if (r.disabledReason === 'SignatureSplit') return { text: 'Off: grouping changed', chip: 'bg-slate-100 text-slate-700' }
  return { text: 'Off', chip: 'bg-slate-100 text-slate-700' }
}

function resultOf(r: Rule): string {
  if (r.verifiedOutcomes > 0) return `${r.stayedFixed} of ${r.verifiedOutcomes} stayed fixed (${Math.round((r.stayedFixed / r.verifiedOutcomes) * 100)}%)`
  if (r.replayed > 0) return `${r.replayed} replayed, none verified yet`
  return 'Nothing replayed yet'
}

/**
 * Auto Replay rules (build-plan unit E5; design `p10-auto-replay-rules`): what ServiceHub may replay without asking, how fast, and
 * whether each rule is still working — one cloud, or every connected one. The table is the overview; Manage opens the 4.2.0 rule card
 * under its row (switch, edit, delete, test, what it replayed), moved not rewritten, so no capability is lost. A rule is never tied to
 * a namespace (`Rule` has none), so "Where" is the entity it matches. Nothing here sends a message: Replay all opens Bulk replay (F3), which
 * shows its preview first.
 */
export default function RulesPage() {
  const scope = parseScope(useParams().cloud)
  const cloud = cloudOf(scope)
  const { pathname } = useLocation()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const me = useMe()
  const components = useComponents(cloud)
  const namespaces = useNamespaces()

  // Rules are kept per cloud on the server, so "all" asks once per connected cloud.
  const providers: CloudProvider[] = cloud ? [cloud] : [...new Set((components.data?.components ?? []).map((c) => c.cloud))]
  const lists = useQueries({ queries: providers.map((p) => ({ queryKey: rulesKey(p), queryFn: () => fetchRules(p), refetchInterval: 15_000 })) })
  const loading = components.isPending || lists.some((l) => l.isPending)
  const failed = components.isError || lists.some((l) => l.isError)
  const rules = lists.flatMap((l) => l.data ?? [])

  const [open, setOpen] = useState<number | null>(null)
  const [pick, setPick] = useState<CloudProvider | ''>('')
  const [confirmGenerate, setConfirmGenerate] = useState(false)
  // A failure's page links here with `?rule=<signature>`: the form opens with that failure chosen.
  const wanted = params.get('rule') !== null
  const [creating, setCreating] = useState(wanted)
  const target: CloudProvider | null = cloud ?? (pick || providers[0] || null)
  const mayCreate = permission(me.data, 'Approver', 'create rules', { recover: true })
  const generate = useMutation({
    mutationFn: () => generateRules(target as CloudProvider),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['rules'] }),
  })
  const closeForm = () => {
    setCreating(false)
    if (params.has('rule')) setParams((c) => { const n = new URLSearchParams(c); n.delete('rule'); return n }, { replace: true })
  }

  const { items: shown, pager } = usePagedList(rules)
  const on = rules.filter((r) => r.enabled).length
  const stopped = rules.filter(tripped)
  const off = rules.length - on - stopped.length
  const now = new Date()
  const scopeName = cloudLabel[scope]
  const links = (r: Rule): RuleLinks => ({
    held: () => nextHref(nextEntry('approvals'), { cloud: r.provider }),
    message: (id) => `${pathname}?dialog=message&id=${id}`,
    replayAll: true,
    bulkParam: 'dialog',
  })
  // A cloud that cannot prove a fix stayed fixed never lets a rule act alone; say so only where a connection there exists.
  const cannotVerify = (namespaces.data ?? []).some((n) => (cloud ? n.provider === cloud : providers.includes(n.provider)) && n.capabilities?.canProveDlqAbsence === false)

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope={scope} title="Auto Replay rules" updatedAtMs={Math.max(0, ...lists.map((l) => l.dataUpdatedAt))}
        onRefresh={() => void client.invalidateQueries({ queryKey: ['rules'] })}
        lookingAt="What ServiceHub is allowed to replay without asking, how fast, and whether each rule is still working." />

      {failed ? (
        <p role="alert" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-800">The rules could not be read. Nothing is shown rather than a list that may be incomplete.</p>
      ) : loading ? (
        <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
      ) : providers.length === 0 ? (
        <p className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-6 text-sm text-[var(--color-text-muted)]">Connect a cloud first, then Auto Replay can watch it.</p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Tile icon={<Zap className="h-5 w-5 text-amber-600" aria-hidden="true" />} title="Rules" note={`On ${scopeName}`}>{rules.length}</Tile>
            <Tile icon={<Check className="h-5 w-5 text-green-700" aria-hidden="true" />} title="On" note="May replay on their own, behind every safety check">{on}</Tile>
            <Tile icon={<Zap className="h-5 w-5 text-red-600" aria-hidden="true" />} title="Stopped itself" note="Fewer of its replays stayed fixed than it allows">{stopped.length}</Tile>
            <Tile icon={<Circle className="h-5 w-5 text-[var(--color-primary-700)]" aria-hidden="true" />} title="Off" note="Switched off by a person">{off}</Tile>
          </div>

          <section aria-labelledby="rules-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            <div className="flex flex-wrap items-center justify-between gap-3 px-4 pt-4">
              <h2 id="rules-title" className="text-base font-semibold">Rules</h2>
              <div className="flex flex-wrap items-center gap-2">
                {!cloud && providers.length > 1 && (
                  <label className="flex items-center gap-2 text-sm"><span className="text-[var(--color-text-muted)]">New rule on:</span>
                    <select value={target ?? ''} onChange={(e) => setPick(e.target.value as CloudProvider)} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-2.5 py-1.5 text-sm">
                      {providers.map((p) => <option key={p} value={p}>{cloudLabel[p]}</option>)}
                    </select>
                  </label>
                )}
                <button type="button" disabled={!mayCreate.allowed || target === null} onClick={() => setConfirmGenerate(true)} title={mayCreate.reason ?? 'Make rules for your most common failures that no rule covers yet'}
                  className="flex items-center gap-1.5 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm font-semibold text-amber-900 disabled:opacity-50"><Sparkles className="h-4 w-4" aria-hidden="true" /> Auto generate rules</button>
                {!creating && <button type="button" disabled={target === null} onClick={() => setCreating(true)} className="flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-semibold text-white"><Plus className="h-4 w-4" aria-hidden="true" /> New rule</button>}
              </div>
            </div>
            <div className="px-4"><NotAllowed reason={mayCreate.reason} /></div>

            {confirmGenerate && target && (
              <section aria-label="Auto generate rules" className="m-4 rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950">
                {generate.data ? (
                  <p role="status">
                    {generate.data.length === 0
                      ? 'Nothing to make: every failure ServiceHub has seen already has a rule, or none has been seen yet.'
                      : <><b>Made {generate.data.length} {generate.data.length === 1 ? 'rule' : 'rules'} on {cloudLabel[target]}.</b> They are on, but the safety checks still hold every message until its failure has earned unattended replay.</>}
                    {' '}<button type="button" onClick={() => { generate.reset(); setConfirmGenerate(false) }} className="font-semibold underline">Done</button>
                  </p>
                ) : (
                  <>
                    <p><b>Make up to 5 rules on {cloudLabel[target]} from your most common failures?</b> ServiceHub picks failures it has recorded that no rule covers yet. Each rule starts on at 10 an hour, and still goes through every safety check.</p>
                    {generate.isError && <p role="alert" className="mt-1 text-red-700">ServiceHub couldn’t make the rules.</p>}
                    <div className="mt-3 flex gap-2">
                      <button type="button" disabled={generate.isPending} onClick={() => generate.mutate()} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-1.5 text-sm font-semibold text-white disabled:opacity-50">{generate.isPending ? 'Making…' : 'Yes, make them'}</button>
                      <button type="button" onClick={() => setConfirmGenerate(false)} className="rounded-lg border border-[var(--color-border)] bg-white px-3.5 py-1.5 text-sm font-semibold">Cancel</button>
                    </div>
                  </>
                )}
              </section>
            )}

            {creating && target && <div className="m-4"><NewRule provider={target} onDone={closeForm} /></div>}

            {rules.length === 0 ? (
              <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">No rules yet. A rule names a failure ServiceHub has already seen, and how carefully to retry it.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <caption className="sr-only">Auto Replay rules</caption>
                  <thead>
                    <tr>
                      {!cloud && <th scope="col" className={th}>Cloud</th>}
                      <th scope="col" className={th}>Rule</th><th scope="col" className={th}>Matches reason</th><th scope="col" className={th}>Where</th><th scope="col" className={th}>Pace</th>
                      <th scope="col" className={th}>Waits</th><th scope="col" className={th}>State</th><th scope="col" className={th}>Result so far</th><th scope="col" className={th}>Last replay</th><th scope="col" className={th}>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {shown.flatMap((r) => {
                      const st = stateOf(r)
                      const expanded = open === r.id
                      return [
                        <tr key={r.id} className="border-t border-[var(--color-border)]">
                          {!cloud && <td className={td}>{cloudLabel[r.provider]}</td>}
                          <td className={`${td} font-semibold`}>{ruleTitle(r.name)}</td>
                          <td className={td}>{r.reason ?? 'Any'}</td>
                          <td className={td}>{r.entityName ?? 'Any entity'}</td>
                          <td className={td}>{r.maxPerHour} an hour</td>
                          <td className={td}>{waits(r.waitSeconds)}</td>
                          <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${st.chip}`}>{st.text}</span></td>
                          <td className={td}>{resultOf(r)}</td>
                          <td className={td}>{r.lastReplayedAt ? formatWhen(r.lastReplayedAt, now) : '—'}</td>
                          <td className={td}>
                            <button type="button" aria-expanded={expanded} onClick={() => setOpen(expanded ? null : r.id)} aria-label={`${expanded ? 'Close' : 'Manage'} ${ruleTitle(r.name)}`}
                              className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">{tripped(r) ? 'Review' : 'Manage'}</button>
                          </td>
                        </tr>,
                        expanded && <tr key={`${r.id}-detail`}><td colSpan={cloud ? 9 : 10} className="bg-[var(--color-surface-muted)] px-4 py-3"><ul><RuleCard rule={r} provider={r.provider} links={links(r)} /></ul></td></tr>,
                      ]
                    })}
                  </tbody>
                </table>
              </div>
            )}
            <Pager {...pager} label="rules" />
            <p className="px-4 pb-4 text-xs text-[var(--color-text-muted)]">Creating a rule, switching one on, or raising its pace needs the Approver role, because it gives a machine the right to replay. Anyone who may replay can switch one off.</p>
          </section>

          {stopped.map((r) => (
            <p key={r.id} role="status" className="rounded-xl border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900">
              <b>{ruleTitle(r.name)} switched itself off.</b> {r.disabledDetail ?? 'Fewer of its replays stayed fixed than it allows, so it stopped.'} Nothing it replayed is lost. Look at why before turning it back on.{' '}
              <Link to={`${nextHref(nextEntry('replays'), { cloud: r.provider })}?rule=${r.id}`} className="font-semibold underline">See what it replayed</Link>
            </p>
          ))}
          {cannotVerify && (
            <p className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-3 text-sm">
              <b>A rule can only prove itself where a fix can be verified.</b> Where a cloud cannot, a rule may propose replays, and a person still approves every one.
            </p>
          )}
        </>
      )}
    </div>
  )
}

function Tile({ icon, title, note, children }: { icon: React.ReactNode; title: string; note: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <span className="rounded-full bg-[var(--color-surface-muted)] p-2.5">{icon}</span>
      <div><p className="text-sm font-semibold">{title}</p><p className="text-2xl font-bold">{children}</p><p className="text-xs text-[var(--color-text-muted)]">{note}</p></div>
    </div>
  )
}
