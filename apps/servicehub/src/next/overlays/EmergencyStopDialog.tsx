import { Check, CircleAlert, X } from 'lucide-react'
import { useState } from 'react'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { useMe } from '../../hooks/useIdentity'
import { useEmergencyStop, useSettingsMutation } from '../../hooks/useSettings'
import { toProblem } from '../../lib/api/client'
import { setEmergencyStop } from '../../lib/api/settings'
import { formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import type { NextOverlayBodyProps } from './registry'
import { Tip } from '../components/Tip'

type Tone = 'stops' | 'maybe' | 'stays'
const what: readonly { tone: Tone; title: string; note: string }[] = [
  { tone: 'stops', title: 'The Agent stops replaying', note: 'Auto Replay rules and earned fixes do nothing until you turn this off' },
  { tone: 'maybe', title: 'A bulk replay a person already started may carry on', note: 'The stop halts automatic actions only. Check live which bulk jobs it halts (design check C15)' },
  { tone: 'stays', title: 'Nothing is deleted or moved', note: 'Messages stay where they are' },
  { tone: 'stays', title: 'ServiceHub keeps watching and recording', note: 'Lists, counts and the ledger stay up to date' },
  { tone: 'stays', title: 'A person can still replay one message', note: 'After the usual preview, and it is recorded. This is by design: the stop pushes work to a person, so it must not block the person' },
  { tone: 'maybe', title: 'A cloud-side move already started keeps going', note: 'For example an SQS redrive, if unit 3a.4 is built. Cancel it from its own screen' },
]
const mark = { stops: <X className="h-4 w-4 text-red-600" aria-label="Stops" />, maybe: <CircleAlert className="h-4 w-4 text-amber-600" aria-label="May continue" />, stays: <Check className="h-4 w-4 text-green-700" aria-label="Stays" /> } as const

/**
 * Emergency stop (build-plan unit F5; design `d08-emergency-stop-confirm`), at `?dialog=emergency-stop`. **Never one click:** a reason
 * is required (recorded with the person's name and sent to the notification channels) and so is the typed word the server demands
 * (STOP to switch on, LIFT to switch off — `SettingsController`). The list says what stops and, as the design does, what does NOT:
 * the stop halts automatic actors only, so a person's own replay still works, and a bulk run or a cloud-side move already started
 * may carry on. When the stop's state cannot be read the header already treats it as on, so the dialog offers to lift it only when it
 * knows it is on. Switching the stop on or off needs the **Admin** role (the server's rule; the design draws Approver).
 */
export default function EmergencyStopDialog({ close }: NextOverlayBodyProps) {
  const stop = useEmergencyStop()
  const me = useMe().data
  const set = useSettingsMutation(setEmergencyStop)
  const [reason, setReason] = useState('')
  const [confirm, setConfirm] = useState('')
  const on = stop.data?.active === true
  const word = on ? 'LIFT' : 'STOP'
  const may = permission(me, 'Admin', on ? 'switch emergency stop off' : 'switch emergency stop on')
  useOverlayTitle(on ? 'Turn off emergency stop?' : 'Turn on emergency stop?', `All clouds · needs the Admin role`)

  if (stop.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading the emergency stop…</p>
  if (stop.isError) {
    return <p role="alert" className="text-sm">ServiceHub couldn’t read whether emergency stop is on, so it behaves as if it is on. Try again in a moment.</p>
  }

  const submit = () => set.mutate({ active: !on, reason: reason.trim(), confirm: confirm.trim() }, { onSuccess: close })
  return (
    <form className="space-y-4" onSubmit={(e) => { e.preventDefault(); if (reason.trim() && confirm.trim() === word && may.allowed) submit() }}>
      {on ? (
        <p className="rounded-xl border border-red-300 bg-red-50 px-3.5 py-2.5 text-sm text-red-900">
          <b>Emergency stop is on</b> since {stop.data?.at ? formatWhen(stop.data.at, new Date()) : '—'}{stop.data?.by ? `, by ${stop.data.by}` : ''}{stop.data?.reason ? ` — “${stop.data.reason}”` : ''}.
          Turning it off hands automatic authority back, so it is as deliberate as turning it on.
        </p>
      ) : (
        <p className="flex items-start gap-2 rounded-xl border border-red-300 bg-red-50 px-3.5 py-2.5 text-sm text-red-900"><CircleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" /><b>This stops every replay ServiceHub would do on its own, on every cloud, now.</b></p>
      )}

      {!on && (
        <section aria-label="What stops">
          <h3 className="mb-1.5 text-sm font-bold">What stops<Tip label="section:What stops" /></h3>
          <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
            {what.map((w) => (
              <li key={w.title} className="flex items-start gap-3 px-3 py-2 text-sm">
                <span className="mt-0.5 shrink-0">{mark[w.tone]}</span>
                <span><span className="font-medium">{w.title}</span><span className="block text-xs text-[var(--color-text-muted)]">{w.note}</span></span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="space-y-1.5 text-sm">
        <label htmlFor="stop-why" className="inline-block font-bold">{on ? 'Why is it safe to resume?' : 'Why (recorded, and sent to your notification channels)'}</label>
        <Tip label="dialog:Why" />
        <input id="stop-why" value={reason} onChange={(e) => setReason(e.target.value)} placeholder={on ? 'recorded with your name' : 'e.g. Payment gateway outage; holding replays'}
          className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2" />
        <label htmlFor="stop-word" className="inline-block font-bold">Type {word} to confirm</label>
        <Tip label="dialog:Typed word" />
        <input id="stop-word" value={confirm} onChange={(e) => setConfirm(e.target.value)} autoComplete="off" className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 font-mono" />
        <p className="text-xs text-[var(--color-text-muted)]">Turning it back off also needs a person. If ServiceHub cannot read whether emergency stop is on, it behaves as if it is on.</p>
      </div>

      <NotAllowed reason={may.reason} />
      {set.isError && <p role="alert" className="text-sm text-red-800">{toProblem(set.error).message} Nothing was changed.</p>}
      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[var(--color-border)] pt-3">
        <span className="text-xs text-[var(--color-text-muted)]">Recorded with your name.</span>
        <span className="flex gap-2">
          <button type="button" onClick={close} disabled={set.isPending} className="rounded-lg border border-[var(--color-border)] px-4 py-2 text-sm font-semibold">Cancel</button>
          <button type="submit" disabled={!may.allowed || set.isPending || !reason.trim() || confirm.trim() !== word}
            className={`rounded-lg px-4 py-2 text-sm font-bold text-white disabled:opacity-50 ${on ? 'bg-[var(--color-primary-600)]' : 'bg-red-700'}`}>
            {set.isPending ? 'Saving…' : on ? 'Switch emergency stop off' : 'Stop all automatic replays'}
          </button>
        </span>
      </div>
    </form>
  )
}
