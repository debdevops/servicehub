import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Clock, Eye, Plus, Zap } from 'lucide-react'
import type { OverlayBodyProps } from '../overlays/registry'
import { useProviderScope } from '../provider/providerScope'
import { createRule, fetchRules, fetchRuleSources, setRuleEnabled, testRule, type Rule, type RuleSource } from '../../lib/api/rules'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatAge } from '../../lib/format'
import { providerLabel } from '../../lib/providers'

const rulesKey = (p: CloudProvider) => ['rules', p] as const

/** In words: why the safety checks are holding a rule's matches. The code is the fact; this is what it means. */
function holdWords(code: string | null): string {
  if (!code) return 'The safety checks are holding them for a person.'
  if (code.startsWith('AUTONOMY')) return 'ServiceHub hasn’t earned the right to replay this failure on its own yet, so it asks first.'
  if (code === 'PRODUCTION_ELEVATION_REQUIRED') return 'Rules never run in Production namespaces.'
  if (code === 'EMERGENCY_STOP_ACTIVE') return 'Emergency stop is on.'
  if (code.startsWith('RECURRENCE_CAP')) return 'They have already been replayed and came back too often.'
  return `A safety check is holding them (${code}).`
}

const paceWords = (r: Pick<Rule, 'maxPerHour' | 'waitSeconds'>) =>
  `up to ${r.maxPerHour} an hour · waits ${r.waitSeconds >= 60 ? `${Math.round(r.waitSeconds / 60)} min` : `${r.waitSeconds} s`}`

/**
 * Auto Replay (`?panel=rules`): what ServiceHub may retry on its own in the chosen cloud, and what stops it. A rule is a
 * failure and a pace — there is no rule language. Every message a rule picks still goes through the same safety checks as a
 * person's replay, so making a rule never sends anything.
 */
export default function AutoReplayPanel(_: OverlayBodyProps) {
  const { selected } = useProviderScope()
  if (selected === null) return <p className="text-sm text-[var(--color-text-muted)]">Connect a cloud first, then Auto Replay can watch it.</p>
  return <Panel provider={selected} />
}

function Panel({ provider }: { provider: CloudProvider }) {
  // A signature's page links here with `?rule=<hash>`: the form opens with that failure already chosen.
  const [params] = useSearchParams()
  const [creating, setCreating] = useState(params.get('rule') !== null)
  const rules = useQuery({ queryKey: rulesKey(provider), queryFn: () => fetchRules(provider), refetchInterval: 15_000 })
  const cloud = providerLabel[provider]

  if (rules.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading your rules…</p>
  if (rules.isError) return <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t read the rules just now.</p>

  const list = rules.data
  const on = list.filter((r) => r.enabled).length
  const stopped = list.filter((r) => r.disabledReason === 'CircuitBreaker').length

  return (
    <div className="space-y-4">
      <p className="text-sm text-[var(--color-text-muted)]">
        What ServiceHub may retry on its own in <b className="text-[var(--color-text)]">{cloud}</b> — {list.length} {list.length === 1 ? 'rule' : 'rules'}, {on} on
        {stopped > 0 ? `, ${stopped} stopped itself` : ''}.
      </p>
      {list.length === 0 && !creating && (
        <p className="rounded-xl border border-dashed border-[var(--color-border)] px-4 py-6 text-center text-sm text-[var(--color-text-muted)]">
          No rules yet. A rule names a failure ServiceHub has already seen, and how carefully to retry it.
        </p>
      )}
      <ul className="space-y-3">
        {list.map((r) => <RuleCard key={r.id} rule={r} provider={provider} />)}
      </ul>
      {creating ? (
        <NewRule provider={provider} onDone={() => setCreating(false)} />
      ) : (
        <button type="button" onClick={() => setCreating(true)} className="flex w-full items-center justify-center gap-2 rounded-xl border border-dashed border-[#d1d5db] px-4 py-3 text-sm font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-surface-muted)]">
          <Plus className="h-4 w-4" aria-hidden="true" /> New rule
        </button>
      )}
    </div>
  )
}

function RuleCard({ rule: r, provider }: { rule: Rule; provider: CloudProvider }) {
  const client = useQueryClient()
  const [confirmOn, setConfirmOn] = useState(false)
  const toggle = useMutation({
    mutationFn: (enabled: boolean) => setRuleEnabled(r.id, enabled),
    onSuccess: () => { setConfirmOn(false); void client.invalidateQueries({ queryKey: rulesKey(provider) }) },
  })
  const tripped = r.disabledReason === 'CircuitBreaker'
  const now = new Date()

  return (
    <li className={`rounded-xl border p-4 ${tripped ? 'border-[#fecaca] bg-[#fef2f2]' : 'border-[var(--color-border)] bg-[var(--color-surface)]'}`}>
      <div className="flex items-start gap-3">
        <button
          type="button"
          role="switch"
          aria-checked={r.enabled}
          aria-label={`${r.name} is ${r.enabled ? 'on' : 'off'}`}
          disabled={toggle.isPending || tripped}
          onClick={() => toggle.mutate(!r.enabled)}
          className={`mt-0.5 h-6 w-11 shrink-0 rounded-full transition-colors disabled:opacity-60 ${r.enabled ? 'bg-[var(--color-success)]' : 'bg-[#d1d5db]'}`}
        >
          <span className={`block h-5 w-5 rounded-full bg-white shadow transition-transform ${r.enabled ? 'translate-x-[22px]' : 'translate-x-0.5'}`} />
        </button>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="text-[15px] font-bold">{r.name}</h3>
            {tripped ? (
              <span className="ml-auto rounded-full bg-[var(--color-error-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#b91c1c]">Stopped itself</span>
            ) : r.verifiedOutcomes > 0 ? (
              <span className="ml-auto rounded-full bg-[var(--color-success-light)] px-2.5 py-0.5 text-[11px] font-bold text-[#047857]">{r.stayedFixed} of {r.verifiedOutcomes} stayed fixed</span>
            ) : (
              <span className="ml-auto text-[11px] text-[var(--color-text-muted)]">no verified outcomes yet</span>
            )}
          </div>
          <p className="text-[12.5px] text-[var(--color-text-muted)]">
            {[r.reason, r.entityName && <span key="e" className="font-mono">{r.entityName}</span>].filter(Boolean).map((p, i) => <span key={i}>{i > 0 ? ' · ' : ''}{p}</span>)}
          </p>
          <p className="mt-2 flex flex-wrap items-center gap-x-3 text-[12px] text-[var(--color-text-muted)]">
            <span className="flex items-center gap-1"><Clock className="h-3 w-3" aria-hidden="true" /> {paceWords(r)}</span>
            {r.replayed > 0 && r.lastReplayedAt && <span>last replayed {formatAge(r.lastReplayedAt, now)} ago · {r.replayed} {r.replayed === 1 ? 'message' : 'messages'}</span>}
            {r.replayed === 0 && !tripped && <span>hasn’t replayed anything yet</span>}
          </p>
        </div>
      </div>

      {r.enabled && r.askedCount > 0 && (
        <p className="mt-3 flex items-start gap-2 rounded-lg border border-[#fde68a] bg-[var(--color-warning-light)] px-3 py-2 text-[12.5px] text-[#78350f]">
          <Eye className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span><b>{r.askedCount} matching {r.askedCount === 1 ? 'message is' : 'messages are'} waiting for a person.</b> {holdWords(r.lastAskedReason)}</span>
        </p>
      )}

      {tripped && (
        <div className="mt-3 rounded-lg border border-[#fecaca] bg-white px-3 py-2.5 text-[12.5px] text-[#7f1d1d]">
          <p className="flex items-start gap-2"><Zap className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" /><span><b>Stopped itself{r.updatedAt ? ` ${formatAge(r.updatedAt, now)} ago` : ''}.</b> {r.disabledDetail}</span></p>
          <div className="mt-2 flex gap-2">
            {confirmOn ? (
              <button type="button" onClick={() => toggle.mutate(true)} className="rounded-lg bg-[var(--color-primary-600)] px-3 py-1.5 text-[12px] font-semibold text-white">Yes, turn it back on</button>
            ) : (
              <button type="button" onClick={() => setConfirmOn(true)} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-[12px] font-semibold text-[var(--color-text)]">Turn back on…</button>
            )}
          </div>
          {confirmOn && <p className="mt-1.5 text-[11.5px]">It stopped for a reason. Turn it back on only after the failure it retries has been fixed.</p>}
        </div>
      )}
      {r.disabledReason === 'Person' && <p className="mt-2 text-[12px] text-[var(--color-text-muted)]">Turned off by a person.</p>}
    </li>
  )
}

function NewRule({ provider, onDone }: { provider: CloudProvider; onDone: () => void }) {
  const client = useQueryClient()
  const sources = useQuery({ queryKey: ['rule-sources', provider], queryFn: () => fetchRuleSources(provider) })
  const [params] = useSearchParams()
  const wanted = params.get('rule')
  const [source, setSource] = useState<RuleSource | null>(null)
  const [name, setName] = useState('')
  const [maxPerHour, setMax] = useState(10)
  const [wait, setWait] = useState(2)
  const [backOff, setBackOff] = useState(true)

  const pick = (hash: string) => {
    const s = sources.data?.find((x) => x.signatureHash === hash) ?? null
    setSource(s)
    if (s) setName(`${s.reason} in ${s.entityName}`)
  }

  useEffect(() => {
    if (wanted && !source && sources.data) pick(wanted)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [wanted, sources.data])

  const condition = source ? { provider, reason: source.reason, entityName: source.entityName, signatureHash: source.signatureHash } : null
  const test = useQuery({
    queryKey: ['rule-test', condition],
    queryFn: () => testRule(condition!),
    enabled: condition !== null,
  })

  const create = useMutation({
    mutationFn: () => createRule({ ...condition!, name, maxPerHour, waitSeconds: wait * 60, backOff }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: rulesKey(provider) }); onDone() },
  })

  return (
    <form
      aria-label="New rule"
      onSubmit={(e) => { e.preventDefault(); if (condition && name.trim()) create.mutate() }}
      className="space-y-3 rounded-xl border-2 border-[var(--color-primary-200)] p-4"
    >
      <p className="text-[15px] font-bold">New rule <span className="text-[12.5px] font-normal text-[var(--color-text-muted)]">— start from a failure you’ve already seen</span></p>

      <label className="block text-[13px] font-semibold">
        Based on
        <select value={source?.signatureHash ?? ''} onChange={(e) => pick(e.target.value)} className="mt-1 block w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm font-normal">
          <option value="">{sources.isPending ? 'Reading recent failures…' : 'Choose a failure…'}</option>
          {sources.data?.map((s) => <option key={s.signatureHash} value={s.signatureHash}>{s.reason} · {s.entityName} · {s.messages} {s.messages === 1 ? 'message' : 'messages'}</option>)}
        </select>
      </label>
      {sources.data?.length === 0 && <p className="text-[12.5px] text-[var(--color-text-muted)]">No failures have been recorded in this cloud yet, so there is nothing to base a rule on. ServiceHub records failures where it can look on its own.</p>}

      {source && (
        <>
          <label className="block text-[13px] font-semibold">
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} className="mt-1 block w-full rounded-lg border border-[var(--color-border)] px-3 py-2 text-sm font-normal" />
          </label>
          <p className="text-[13px]"><b>Replay a message when</b> its failure is <span className="rounded-full bg-[var(--color-error-light)] px-2 py-0.5 text-[11px] font-bold text-[#b91c1c]">{source.reason}</span> and its queue is <span className="font-mono text-[12px]">{source.entityName}</span>.</p>
          <div className="grid grid-cols-3 gap-3 text-[13px] font-semibold">
            <label>At most<div className="mt-1 flex items-center gap-1"><input type="number" min={1} max={1000} value={maxPerHour} onChange={(e) => setMax(Number(e.target.value) || 1)} className="w-full rounded-lg border border-[var(--color-border)] px-2 py-2 text-sm font-normal" /><span className="whitespace-nowrap text-[12px] font-normal">an hour</span></div></label>
            <label>Wait first<div className="mt-1 flex items-center gap-1"><input type="number" min={0} max={1440} value={wait} onChange={(e) => setWait(Number(e.target.value) || 0)} className="w-full rounded-lg border border-[var(--color-border)] px-2 py-2 text-sm font-normal" /><span className="whitespace-nowrap text-[12px] font-normal">minutes</span></div></label>
            <label className="flex flex-col">Back off<span className="mt-1 flex items-center gap-2 py-2 text-[12.5px] font-normal"><input type="checkbox" checked={backOff} onChange={(e) => setBackOff(e.target.checked)} /> Longer each time</span></label>
          </div>

          {test.isPending && <p role="status" className="text-[12.5px] text-[var(--color-text-muted)]">Testing against the last 7 days…</p>}
          {test.data && (
            <p className="flex items-start gap-2 rounded-lg border border-[#a7f3d0] bg-[#ecfdf5] px-3 py-2.5 text-[12.5px]">
              <Eye className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[#047857]" aria-hidden="true" />
              <span>
                <b>Tested on the last {test.data.days} days:</b> it would have matched <b>{test.data.matched} {test.data.matched === 1 ? 'message' : 'messages'}</b>
                {test.data.stillWaiting === 0
                  ? '; none of them are still waiting in the queue.'
                  : `; ${test.data.stillWaiting} ${test.data.stillWaiting === 1 ? 'is' : 'are'} still waiting — ${test.data.wouldRun} would pass the safety checks today${test.data.heldBack > 0 ? ` and ${test.data.heldBack} would be held back` : ''}.`}
                {test.data.holds.length > 0 && <span className="mt-1 block text-[12px]">{test.data.holds.map((h) => holdWords(h.reasonCode)).join(' ')}</span>}
              </span>
            </p>
          )}
        </>
      )}

      <p className="text-[12px] text-[var(--color-text-muted)]">Rules never run in Production namespaces, go through the same safety checks as you, and <b>stop themselves</b> if fewer than half of their replays stay fixed.</p>
      {create.isError && <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn’t create the rule. Check the name and try again.</p>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onDone} className="rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Cancel</button>
        <button type="submit" disabled={!condition || !name.trim() || create.isPending} className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">Create and turn on</button>
      </div>
    </form>
  )
}
