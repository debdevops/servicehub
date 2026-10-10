import { useEffect, useState } from 'react'
import { useMe } from '../../hooks/useIdentity'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Clock, Eye, FlaskConical, Pencil, Play, Plus, RefreshCw, ShieldCheck, Sparkles, Trash2, Zap, X } from 'lucide-react'
import { bulkSelection } from '../../lib/bulkSelection'
import { createRule, deleteRule, fetchRuleMatches, fetchRules, fetchRulesHeld, fetchRuleSources, generateRules, setRuleEnabled, testRule, updateRule, type Rule, type RuleSource } from '../../lib/api/rules'
import type { CloudProvider } from '../../lib/api/namespaces'
import { formatAgo, formatWhen } from '../../lib/format'
import { fetchReplays, type ReplayListItem } from '../../lib/api/replay'
import { Collapsible } from '../ui/Collapsible'
import { providerLabel } from '../../lib/providers'
import { useCloudIsManual } from '../../hooks/useManualApproval'
import { RetryLink } from '../ui/RetryLink'
import { Skeleton } from '../ui/Skeleton'
import { distinctHeldWords, heldWords } from '../../lib/heldWords'
import { waitingHref } from '../../lib/urlState'
import { Select } from '../ui/Select'

import { ruleTitle } from '../../lib/ruleName'
import PageHelpLink from '../help/PageHelpLink'

const rulesKey = (p: CloudProvider) => ['rules', p] as const

function holdWords(code: string | null, manual = false): string {
  if (!code) return "The safety checks are holding them for a person."
  if (code.startsWith('AUTONOMY')) return manual ? 'This cloud can’t confirm a replay fixed it, so each replay is your decision.' : "ServiceHub hasn't earned the right to replay this failure on its own yet, so it asks first."
  if (code === 'PRODUCTION_ELEVATION_REQUIRED') return "Rules never run in Production namespaces."
  if (code === 'EMERGENCY_STOP_ACTIVE') return "Emergency stop is on."
  if (code.startsWith('RECURRENCE_CAP')) return "ServiceHub has already tried these several times, replayed or held for a person, so it won’t try again on its own."
  return `A safety check is holding them (${code}).`
}

const paceWords = (r: Pick<Rule, 'maxPerHour' | 'waitSeconds'>) =>
  `up to ${r.maxPerHour} an hour · waits ${r.waitSeconds >= 60 ? `${Math.round(r.waitSeconds / 60)} min` : `${r.waitSeconds} s`}`

export function AutoReplayPage({ provider, onClose }: { provider: CloudProvider; onClose: () => void }) {
  const cloud = providerLabel[provider]
  const [creating, setCreating] = useState(false)
  const [confirmGenerate, setConfirmGenerate] = useState(false)
  const client = useQueryClient()
  const mayCreate = permission(useMe().data, 'Approver', 'create rules', { recover: true })
  const generate = useMutation({
    mutationFn: () => generateRules(provider),
    onSuccess: () => void client.invalidateQueries({ queryKey: rulesKey(provider) }),
  })
  const rules = useQuery({ queryKey: rulesKey(provider), queryFn: () => fetchRules(provider), refetchInterval: 15_000 })
  const held = useQuery({ queryKey: [...rulesKey(provider), 'held'], queryFn: () => fetchRulesHeld(provider), refetchInterval: 15_000 })
  const [params, setParams] = useSearchParams()
  const wantedRule = params.get('rule')

  // A link from Failure Signatures (`&rule=<signature>`) opens the form once — not again on every later URL change.
  useEffect(() => {
    if (wantedRule !== null) setCreating(true)
  }, [wantedRule])
  const closeForm = () => {
    setCreating(false)
    if (params.has('rule')) setParams((c) => { const n = new URLSearchParams(c); n.delete('rule'); return n }, { replace: true })
  }

  // Loading and failing keep the page's title and its way out, so a bad moment never leaves an untitled page you cannot close (6.1).
  if (rules.isPending || rules.isError) {
    return (
      <div className="space-y-5">
        <div className="flex items-start justify-between gap-4">
          <h1 className="flex items-center gap-2 text-2xl font-semibold text-[var(--color-text)]">{cloud} — Auto Replay <PageHelpLink page="auto-replay" /></h1>
          <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]">
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </div>
        {rules.isPending
          ? <Skeleton label="Reading your rules…" rows={4} />
          : <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn't read the rules just now. <RetryLink onRetry={() => void rules.refetch()} /></p>}
      </div>
    )
  }

  const list = rules.data
  const on = list.filter((r) => r.enabled).length
  const stopped = list.filter((r) => r.disabledReason === 'CircuitBreaker').length

  const replayedTotal = list.reduce((n, r) => n + r.replayed, 0)
  const waiting = held.data?.distinct ?? 0

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3 sm:gap-4">
        <div className="min-w-0">
          <h1 className="flex items-center gap-2 text-2xl font-semibold text-[var(--color-text)]">{cloud} — Auto Replay <PageHelpLink page="auto-replay" /></h1>
          <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">
            What ServiceHub may retry on its own — {list.length} {list.length === 1 ? 'rule' : 'rules'}, {on} on
            {stopped > 0 ? `, ${stopped} stopped itself` : ''}
          </p>
          <NotAllowed reason={mayCreate.reason} />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => void rules.refetch()}
            aria-label="Refresh rules"
            className="rounded-lg border border-[var(--color-border)] p-2 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"
          >
            <RefreshCw className={`h-4 w-4 ${rules.isFetching ? 'animate-spin' : ''}`} aria-hidden="true" />
          </button>
          <button
            type="button"
            onClick={() => setConfirmGenerate(true)}
            disabled={!mayCreate.allowed}
            title={mayCreate.reason ?? 'Make rules for your most common failures that no rule covers yet'}
            className="flex items-center gap-2 rounded-lg border border-[#fde68a] bg-[var(--color-warning-light)] px-3 py-2 text-sm font-medium text-[#78350f] disabled:opacity-50"
          >
            <Sparkles className="h-4 w-4" aria-hidden="true" /> Auto Generate Rules
          </button>
          {!creating && (
            <button type="button" onClick={() => setCreating(true)} className="flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-semibold text-white">
              <Plus className="h-4 w-4" aria-hidden="true" /> Create rule
            </button>
          )}
          <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]">
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </div>
      </div>

      <section aria-label="How Auto Replay works" className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-3">
        <Collapsible title="How Auto Replay works" defaultOpen={list.length === 0}>
          <ol className="grid gap-3 text-[13px] sm:grid-cols-4">
            <li><b>1. You name a failure.</b> A rule is a failure ServiceHub has already seen, plus how carefully to retry it. There is no rule language.</li>
            <li><b>2. Safety checks decide.</b> Every message goes through the same checks a person would. If one holds it, the message waits for you.</li>
            <li><b>3. It replays, gently.</b> At most the hourly limit, after the wait you chose — and never in Production namespaces.</li>
            <li><b>4. It proves it worked.</b> Each replay is watched. If fewer than half stay fixed, the rule <b>stops itself</b>.</li>
          </ol>
          <p className="mt-3 flex items-start gap-2 text-[12.5px] text-[var(--color-text-muted)]">
            <ShieldCheck className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            Rules apply to <b className="text-[var(--color-text)]">all {cloud} namespaces</b>, whichever namespace or environment is chosen elsewhere in the app.
          </p>
        </Collapsible>
      </section>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Tile value={on} label="rules on" />
        <Tile value={replayedTotal} label="messages replayed by rules" />
        <Tile value={held.data ? distinctHeldWords(held.data) : '…'} label="waiting for a person" tone={waiting > 0 ? 'amber' : 'plain'} />
        <Tile value={stopped} label="stopped themselves" tone={stopped > 0 ? 'red' : 'plain'} />
      </div>

      {confirmGenerate && (
        <section aria-label="Auto Generate Rules" className="rounded-xl border border-[#fde68a] bg-[var(--color-warning-light)] p-4 text-[13px] text-[#78350f]">
          {generate.data ? (
            <p role="status">
              {generate.data.length === 0
                ? 'Nothing to make: every failure ServiceHub has seen already has a rule, or none has been seen yet.'
                : <><b>Made {generate.data.length} {generate.data.length === 1 ? 'rule' : 'rules'}.</b> They are on, but the safety checks still hold every message until its failure has earned unattended replay. Test any of them below.</>}
              {' '}<button type="button" onClick={() => { generate.reset(); setConfirmGenerate(false) }} className="font-semibold underline">Done</button>
            </p>
          ) : (
            <>
              <p><b>Make up to 5 rules from your most common failures?</b> ServiceHub picks failures it has recorded that no rule covers yet. Each rule starts on at 10 an hour, and still goes through every safety check.</p>
              {generate.isError && <p role="alert" className="mt-1 text-[var(--color-error)]">ServiceHub couldn't make the rules.</p>}
              <div className="mt-3 flex gap-2">
                <button type="button" disabled={generate.isPending} onClick={() => generate.mutate()} className="rounded-lg bg-[var(--color-primary-600)] px-3.5 py-1.5 text-[12.5px] font-semibold text-white disabled:opacity-50">{generate.isPending ? 'Making…' : 'Yes, make them'}</button>
                <button type="button" onClick={() => setConfirmGenerate(false)} className="rounded-lg border border-[var(--color-border)] bg-white px-3.5 py-1.5 text-[12.5px] font-semibold">Cancel</button>
              </div>
            </>
          )}
        </section>
      )}

      {creating && <NewRule provider={provider} onDone={closeForm} />}

      {list.length === 0 && !creating && (
        <div className="rounded-xl border border-dashed border-[var(--color-border)] px-6 py-10 text-center">
          <p className="text-sm text-[var(--color-text-muted)]">No rules yet. A rule names a failure ServiceHub has already seen, and how carefully to retry it.</p>
          <button type="button" onClick={() => setCreating(true)} className="mt-4 inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white">
            <Plus className="h-4 w-4" aria-hidden="true" /> Create your first rule
          </button>
        </div>
      )}

      <ul className="grid gap-4 lg:grid-cols-2">
        {list.map((r) => <RuleCard key={r.id} rule={r} provider={provider} />)}
      </ul>
    </div>
  )
}

function Tile({ value, label, tone = 'plain' }: { value: number | string; label: string; tone?: 'plain' | 'amber' | 'red' }) {
  const color = tone === 'amber' ? 'text-[#b45309]' : tone === 'red' ? 'text-[#b91c1c]' : 'text-[var(--color-text)]'
  return (
    <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-3">
      <p className={`tabular text-2xl font-bold ${color}`}>{typeof value === 'number' ? value.toLocaleString() : value}</p>
      <p className="text-[12px] text-[var(--color-text-muted)]">{label}</p>
    </div>
  )
}

/** What a rule would have done, by today's checks. Sends nothing. */
function TestResult({ manual = false, test }: { manual?: boolean; test: { days: number; matched: number; stillWaiting: number; wouldRun: number; heldBack: number; holds: readonly { reasonCode: string }[] } }) {
  return (
    <p className="flex items-start gap-2 rounded-lg border border-[#a7f3d0] bg-[#ecfdf5] px-3 py-2.5 text-[12.5px]">
      <Eye className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[#047857]" aria-hidden="true" />
      <span>
        <b>Tested on the last {test.days} days:</b> it would have matched <b>{test.matched} {test.matched === 1 ? 'message' : 'messages'}</b>
        {test.stillWaiting === 0
          ? '; none of them are still waiting in the queue — they have already left it, so there is nothing to replay now.'
          : `; ${test.stillWaiting} ${test.stillWaiting === 1 ? 'is' : 'are'} still waiting — ${test.wouldRun} would pass the safety checks today${test.heldBack > 0 ? ` and ${test.heldBack} would be held back` : ''}.`}
        {test.holds.length > 0 && <span className="mt-1 block text-[12px]">{test.holds.map((h) => holdWords(h.reasonCode, manual)).join(' ')}</span>}
      </span>
    </p>
  )
}

const outcomeWords: Record<string, string> = {
  verified: 'stayed fixed', watching: 'being watched', verification_required: 'needs a look', returned: 'came back', not_sent: 'not sent', unknown: 'unknown',
}

/** The replays this rule really sent — the proof that it is working, straight from the ledger. */
function RuleActivity({ rule }: { rule: Rule }) {
  const { search } = useLocation()
  // Message details open in a modal over this page.
  const detailsParams = (dlqMessageId: number) => { const n = new URLSearchParams(search); n.set('message', String(dlqMessageId)); n.set('view', 'modal'); return n.toString() }
  const { data, isPending, isError } = useQuery({
    queryKey: ['rule-activity', rule.id, rule.replayed],
    queryFn: () => fetchReplays({ provider: rule.provider, ruleId: rule.id, pageSize: 5 }),
  })
  const now = new Date()
  if (isPending) return <Skeleton label="Reading what it replayed…" rows={2} />
  if (isError) return <p role="alert" className="text-[12px] text-[var(--color-error)]">Couldn't read this rule's activity.</p>
  return (
    <ul className="divide-y divide-[var(--color-border)] rounded-lg border border-[var(--color-border)] text-[12.5px]">
      {data.items.map((i: ReplayListItem) => (
        <li key={i.id} className="flex flex-wrap items-center gap-x-3 gap-y-0.5 px-3 py-2">
          <span className="tabular text-[var(--color-text-muted)]">{formatWhen(i.replayedAt, now)}</span>
          <Link to={`?${detailsParams(i.dlqMessageId)}`} className="font-mono text-[12px] text-[var(--color-primary-700)] hover:underline [overflow-wrap:anywhere]" title="Open the message details">{i.messageId}</Link>
          <span className="font-mono text-[11.5px] text-[var(--color-text-muted)]">{i.sourceEntity} → {i.targetEntity}</span>
          <span className="ml-auto">{i.outcomeStatus === 'accepted' ? (outcomeWords[i.verification.status] ?? i.verification.status) : i.outcomeStatus === 'rejected' ? 'cloud refused it' : 'outcome unknown'}</span>
        </li>
      ))}
      {data.total > data.items.length && <li className="px-3 py-2 text-[12px] text-[var(--color-text-muted)]">Showing the latest {data.items.length} of {data.total}.</li>}
    </ul>
  )
}

function RuleCard({ rule: r, provider }: { rule: Rule; provider: CloudProvider }) {
  const manual = useCloudIsManual(provider)
  const mayToggle = permission(useMe().data, r.enabled ? 'Operator' : 'Approver', r.enabled ? 'switch this rule off' : 'switch this rule on', { recover: true })
  const client = useQueryClient()
  const [confirmOn, setConfirmOn] = useState(false)
  const toggle = useMutation({
    mutationFn: (enabled: boolean) => setRuleEnabled(r.id, enabled),
    onSuccess: () => { setConfirmOn(false); void client.invalidateQueries({ queryKey: rulesKey(provider) }) },
  })
  const tripped = r.disabledReason === 'CircuitBreaker'
  const split = r.disabledReason === 'SignatureSplit'
  const now = new Date()
  const [editing, setEditing] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [, setParams] = useSearchParams()
  const mayDelete = permission(useMe().data, 'Operator', 'delete this rule', { recover: true })
  const mayReplay = permission(useMe().data, 'Operator', 'replay these messages', { recover: true })
  const remove = useMutation({
    mutationFn: () => deleteRule(r.id),
    onSuccess: () => void client.invalidateQueries({ queryKey: rulesKey(provider) }),
  })
  // Replay all hands the rule's waiting messages to Bulk Replay: the same preview and the same gate as a person's own selection.
  const replayAll = useMutation({
    mutationFn: async () => {
      const ids = await fetchRuleMatches(r.id)
      if (ids.length === 0) throw new Error('none')
      bulkSelection.set({ ids })
    },
    onSuccess: () => setParams((c) => { const n = new URLSearchParams(c); n.set('modal', 'bulk-replay'); n.delete('job'); ;['message', 'view', 'replay'].forEach((k) => n.delete(k)); return n }),
  })
  // What is waiting for this rule right now, so Replay all is only offered when it has something to preview.
  const waiting = useQuery({ queryKey: ['rule-matches', r.id], queryFn: () => fetchRuleMatches(r.id), refetchInterval: 15_000 })
  const nothingWaiting = waiting.data?.length === 0
  const replayTitle = mayReplay.reason ?? (nothingWaiting
    ? 'Nothing to replay: no message waiting in the dead-letter queue matches this rule right now. (Test counts past matches too — those have already left the queue.)'
    : 'Preview replaying every message this rule matches, now')
  const test = useMutation({ mutationFn: () => testRule({ provider, reason: r.reason ?? undefined, entityName: r.entityName ?? undefined, signatureHash: r.signatureHash ?? undefined }) })

  return (
    <li className={`rounded-xl border p-4 ${tripped ? 'border-[#fecaca] bg-[#fff5f5]' : 'border-[var(--color-border)] bg-[var(--color-surface)]'}`}>
      <div className="flex items-start gap-3">
        <button
          type="button"
          role="switch"
          aria-checked={r.enabled}
          aria-label={`${r.name} is ${r.enabled ? 'on' : 'off'}`}
          disabled={toggle.isPending || tripped || split || !mayToggle.allowed}
          title={mayToggle.reason ?? undefined}
          onClick={() => toggle.mutate(!r.enabled)}
          className={`mt-0.5 h-6 w-11 shrink-0 rounded-full transition-colors disabled:opacity-60 ${r.enabled ? 'bg-[var(--color-success)]' : 'bg-[#d1d5db]'}`}
        >
          <span className={`block h-5 w-5 rounded-full bg-white shadow transition-transform ${r.enabled ? 'translate-x-[22px]' : 'translate-x-0.5'}`} />
        </button>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h2 className="text-[15px] font-bold" title={r.name}>{ruleTitle(r.name)}</h2>
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
            {r.replayed > 0 && r.lastReplayedAt && <span>last replayed {formatAgo(r.lastReplayedAt, now)} · {r.replayed} {r.replayed === 1 ? 'message' : 'messages'}</span>}
            {r.replayed === 0 && !tripped && <span>hasn't replayed anything yet</span>}
          </p>
        </div>
      </div>

      {r.enabled && r.askedCount > 0 && (
        <p className="mt-3 flex items-start gap-2 rounded-lg border border-[#fde68a] bg-[var(--color-warning-light)] px-3 py-2 text-[12.5px] text-[#78350f]">
          <Eye className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          <span><b>{heldWords([r])} matching {r.askedCount === 1 && !r.askedIsLowerBound ? 'message is' : 'messages are'} waiting for a person.</b> {holdWords(r.lastAskedReason, manual)}{' '}<Link to={waitingHref(r)} className="font-semibold underline">See {r.askedCount === 1 && !r.askedIsLowerBound ? 'it' : 'them'} ›</Link></span>
        </p>
      )}

      {tripped && (
        <div className="mt-3 rounded-lg border border-[#fecaca] bg-white px-3 py-2.5 text-[12.5px] text-[#7f1d1d]">
          <p className="flex items-start gap-2"><Zap className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" /><span><b>Stopped itself{r.updatedAt ? ` ${formatAgo(r.updatedAt, now)}` : ''}.</b> {r.disabledDetail}</span></p>
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
      {r.disabledReason === 'SignatureSplit' && (
        <p className="mt-2 text-[12px] text-[var(--color-text-muted)]">
          {r.disabledDetail ?? 'ServiceHub now groups failures by their error message, so the group this rule was made for no longer exists and it was switched off.'}
        </p>
      )}

      <div className="mt-3 space-y-3 border-t border-[var(--color-border)] pt-3">
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            disabled={test.isPending}
            onClick={() => test.mutate()}
            className="flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-[12px] font-semibold text-[var(--color-text)] hover:bg-[var(--color-surface-muted)] disabled:opacity-50"
          >
            <FlaskConical className="h-3.5 w-3.5" aria-hidden="true" /> {test.isPending ? 'Testing…' : 'Test this rule'}
          </button>
          {/* The tooltip sits on the wrapper: a disabled button does not always show its own. */}
          <span title={replayTitle}>
            <button
              type="button"
              disabled={replayAll.isPending || !mayReplay.allowed || nothingWaiting}
              onClick={() => replayAll.mutate()}
              className="flex items-center gap-1.5 rounded-lg border border-[#fde68a] bg-[var(--color-warning-light)] px-3 py-1.5 text-[12px] font-semibold text-[#78350f] disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Play className="h-3.5 w-3.5" aria-hidden="true" /> {replayAll.isPending ? 'Finding…' : 'Replay all'}
            </button>
          </span>
          <button
            type="button"
            onClick={() => { setEditing((e) => !e); setConfirmDelete(false) }}
            className="flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-[12px] font-semibold text-[var(--color-text)] hover:bg-[var(--color-surface-muted)]"
          >
            <Pencil className="h-3.5 w-3.5" aria-hidden="true" /> Edit
          </button>
          <button
            type="button"
            aria-label={`Delete ${r.name}`}
            disabled={!mayDelete.allowed}
            title={mayDelete.reason ?? 'Delete this rule'}
            onClick={() => { setConfirmDelete(true); setEditing(false) }}
            className="ml-auto flex items-center gap-1.5 rounded-lg border border-[#fecaca] px-3 py-1.5 text-[12px] font-semibold text-[#b91c1c] hover:bg-[var(--color-error-light)] disabled:opacity-50"
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Delete
          </button>
        </div>
        <p className="text-[11.5px] text-[var(--color-text-muted)]">Test shows what it would do today and sends nothing. Replay all shows a preview first.</p>
        <NotAllowed reason={mayReplay.reason ?? mayDelete.reason ?? mayToggle.reason} />
        {replayAll.isError && (
          // Right under the buttons and clearly a result: a muted line at the foot of the card read as "nothing happened".
          <p role="alert" className="flex items-start gap-2 rounded-lg border border-[#fde68a] bg-[var(--color-warning-light)] px-3 py-2 text-[12.5px] text-[#78350f]">
            <Eye className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            {(replayAll.error as Error).message === 'none'
              ? <span><b>Nothing to replay right now.</b> No message waiting in the dead-letter queue matches this rule. Replay all will preview them as soon as one does.</span>
              : <span>Couldn’t look for this rule’s messages. Try again in a moment.</span>}
          </p>
        )}
        {editing && <EditRule rule={r} provider={provider} onDone={() => setEditing(false)} />}
        {confirmDelete && (
          <div role="alertdialog" aria-label={`Delete ${r.name}?`} className="rounded-lg border border-[#fecaca] bg-[var(--color-error-light)] px-3 py-2.5 text-[12.5px] text-[#7f1d1d]">
            <p><b>Delete “{r.name}”?</b> The rule is removed for good. What it replayed stays in Replayed.</p>
            {remove.isError && <p role="alert" className="mt-1">ServiceHub couldn't delete it.</p>}
            <div className="mt-2 flex gap-2">
              <button type="button" disabled={remove.isPending} onClick={() => remove.mutate()} className="rounded-lg bg-[#dc2626] px-3 py-1.5 text-[12px] font-semibold text-white disabled:opacity-50">{remove.isPending ? 'Deleting…' : 'Yes, delete it'}</button>
              <button type="button" onClick={() => setConfirmDelete(false)} className="rounded-lg border border-[var(--color-border)] bg-white px-3 py-1.5 text-[12px] font-semibold">Keep it</button>
            </div>
          </div>
        )}
        {test.isError && <p role="alert" className="text-[12px] text-[var(--color-error)]">Couldn't test this rule just now.</p>}
        {test.data && <TestResult test={test.data} manual={manual} />}
        {r.replayed > 0 && (
          <Collapsible title="What it replayed" summary={`${r.replayed} ${r.replayed === 1 ? 'message' : 'messages'}`} defaultOpen={false}>
            <RuleActivity rule={r} />
          </Collapsible>
        )}
      </div>
    </li>
  )
}

/** Name and pace only. What a rule matches never changes — a different failure is a new rule. */
function EditRule({ rule: r, provider, onDone }: { rule: Rule; provider: CloudProvider; onDone: () => void }) {
  const mayEdit = permission(useMe().data, 'Approver', 'change this rule', { recover: true })
  const client = useQueryClient()
  const [name, setName] = useState(r.name)
  const [maxPerHour, setMax] = useState(r.maxPerHour)
  const [wait, setWait] = useState(Math.round(r.waitSeconds / 60))
  const [backOff, setBackOff] = useState(r.backOff)
  const save = useMutation({
    mutationFn: () => updateRule(r.id, { name, maxPerHour, waitSeconds: wait * 60, backOff }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: rulesKey(provider) }); onDone() },
  })
  return (
    <form aria-label={`Edit ${r.name}`} onSubmit={(e) => { e.preventDefault(); if (name.trim()) save.mutate() }} className="space-y-2.5 rounded-lg border border-[var(--color-primary-200)] p-3">
      <label className="block text-[12.5px] font-semibold">
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} className="mt-1 block w-full rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-sm font-normal" />
      </label>
      <div className="grid grid-cols-3 gap-3 text-[12.5px] font-semibold">
        <label>At most<div className="mt-1 flex items-center gap-1"><input type="number" min={1} max={1000} value={maxPerHour} onChange={(e) => setMax(Number(e.target.value) || 1)} className="w-full rounded-lg border border-[var(--color-border)] px-2 py-1.5 text-sm font-normal" /><span className="whitespace-nowrap text-[12px] font-normal">an hour</span></div></label>
        <label>Wait first<div className="mt-1 flex items-center gap-1"><input type="number" min={0} max={1440} value={wait} onChange={(e) => setWait(Number(e.target.value) || 0)} className="w-full rounded-lg border border-[var(--color-border)] px-2 py-1.5 text-sm font-normal" /><span className="whitespace-nowrap text-[12px] font-normal">minutes</span></div></label>
        <label className="flex flex-col">Back off<span className="mt-1 flex items-center gap-2 py-1.5 text-[12.5px] font-normal"><input type="checkbox" checked={backOff} onChange={(e) => setBackOff(e.target.checked)} /> Longer each time</span></label>
      </div>
      <p className="text-[11.5px] text-[var(--color-text-muted)]">To match a different failure, make a new rule — what a rule matches doesn't change.</p>
      {save.isError && <p role="alert" className="text-[12px] text-[var(--color-error)]">ServiceHub couldn't save the change.</p>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onDone} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-[12.5px] font-semibold">Cancel</button>
        <button type="submit" disabled={!name.trim() || save.isPending || !mayEdit.allowed} className="rounded-lg bg-[var(--color-primary-600)] px-3 py-1.5 text-[12.5px] font-semibold text-white disabled:opacity-50">Save</button>
      </div>
      <NotAllowed reason={mayEdit.reason} />
    </form>
  )
}

function NewRule({ provider, onDone }: { provider: CloudProvider; onDone: () => void }) {
  const manual = useCloudIsManual(provider)
  const mayCreate = permission(useMe().data, 'Approver', 'create a rule (it starts on)', { recover: true })
  const client = useQueryClient()
  const sources = useQuery({ queryKey: ['rule-sources', provider], queryFn: () => fetchRuleSources(provider) })
  const covered = new Set((useQuery({ queryKey: rulesKey(provider), queryFn: () => fetchRules(provider) }).data ?? []).map((r) => r.signatureHash))
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
      <p className="text-[15px] font-bold">New rule <span className="text-[12.5px] font-normal text-[var(--color-text-muted)]">— start from a failure you've already seen</span></p>

      <div className="block text-[13px] font-semibold">
        Based on
        <Select ariaLabel="Based on" value={source?.signatureHash ?? ''} onChange={pick} className="mt-1">
          <option value="">{sources.isPending ? 'Reading recent failures…' : 'Choose a failure…'}</option>
          {sources.data?.map((s) => <option key={s.signatureHash} value={s.signatureHash}>{s.reason} · {s.entityName} · {s.messages} {s.messages === 1 ? 'message' : 'messages'}{covered.has(s.signatureHash) ? ' · already has a rule' : ''}</option>)}
        </Select>
      </div>
      {source && covered.has(source.signatureHash) && <p className="text-[12.5px] text-[#78350f]">A rule for this failure already exists. A second one would race it; edit the existing rule instead unless you want a different pace.</p>}
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
          {test.data && <TestResult test={test.data} manual={manual} />}
        </>
      )}

      <p className="text-[12px] text-[var(--color-text-muted)]">Rules never run in Production namespaces, go through the same safety checks as you, and <b>stop themselves</b> if fewer than half of their replays stay fixed.</p>
      {create.isError && <p role="alert" className="text-sm text-[var(--color-error)]">ServiceHub couldn't create the rule. Check the name and try again.</p>}
      <div className="flex justify-end gap-2">
        <button type="button" onClick={onDone} className="rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Cancel</button>
        <button type="submit" disabled={!condition || !name.trim() || create.isPending || !mayCreate.allowed} className="rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white disabled:opacity-50">Create and turn on</button>
      </div>
      <NotAllowed reason={mayCreate.reason} />
    </form>
  )
}
