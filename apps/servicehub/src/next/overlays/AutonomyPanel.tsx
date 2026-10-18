import { useQuery } from '@tanstack/react-query'
import { Check, CircleAlert, X } from 'lucide-react'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { useCoverage } from '../../hooks/useCoverage'
import { fetchAuthority } from '../../lib/api/signatures'
import { evidenceSentence } from '../lib/words'
import type { NextOverlayBodyProps } from './registry'

const th = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]'
const td = 'px-3 py-2.5 align-top text-sm'
const pct = (r: number) => `${Math.round(r * 100)}%`
const Item = ({ tone, title, note }: { tone: 'ok' | 'warn'; title: string; note: string }) => (
  <li className="flex items-start gap-3 px-3 py-2 text-sm">
    <span className="mt-0.5 shrink-0">{tone === 'ok' ? <Check className="h-4 w-4 text-green-700" aria-label="Holds" /> : <CircleAlert className="h-4 w-4 text-amber-600" aria-label="Takes it away" />}</span>
    <span><span className="font-medium">{title}</span><span className="block text-xs text-[var(--color-text-muted)]">{note}</span></span>
  </li>
)

/**
 * How autonomy is earned (build-plan unit F11; design `d14-how-autonomy-is-earned`), at `?panel=autonomy`. The one number it can state is
 * the Standing threshold, **read from the server's own trust rules** (`GET /signatures/authority` → `needs`), never typed here, so the
 * panel cannot drift from what the trust service enforces. The Unattended thresholds are not returned by any endpoint, so they are not
 * drawn; and which clouds can earn it is read from the coverage catalogue, never decided from a cloud's name. Nothing here acts.
 */
export default function AutonomyPanel(_props: NextOverlayBodyProps) { // eslint-disable-line @typescript-eslint/no-unused-vars
  useOverlayTitle('How autonomy is earned', 'From “a person approves each message” to “the Agent handles it”')
  const authority = useQuery({ queryKey: ['next', 'authority'], queryFn: () => fetchAuthority({ days: 30 }) })
  const coverage = useCoverage()
  const need = authority.data?.needs
  const standing = need ? `${need.sample} verified fixes at ${pct(need.rate)} or better, no duplicate` : authority.isError ? 'Not read' : 'Reading…'
  const rows = (coverage.data?.rows ?? []).filter((r) => r.inSidebar && r.offersReplay)

  return (
    <div className="space-y-5">
      <p className="rounded-xl border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-3.5 py-2.5 text-sm"><b>It is not “a few approvals”.</b> The Agent earns the right from <b>verified</b> results for one kind of failure on one cloud. Approving a message that then comes back does not count.</p>

      <section aria-label="The ladder">
        <h3 className="mb-1.5 text-sm font-bold">The ladder</h3>
        <table className="w-full border border-[var(--color-border)]">
          <caption className="sr-only">The levels and what each takes</caption>
          <thead><tr className="bg-[var(--color-surface-muted)]"><th scope="col" className={th}>Level</th><th scope="col" className={th}>What the Agent does</th><th scope="col" className={th}>What it takes</th></tr></thead>
          <tbody>
            <tr className="border-t border-[var(--color-border)]"><td className={`${td} font-semibold`}>L3 Approve</td><td className={td}>Prepares the replay. <b>A person approves each message.</b></td><td className={td}>Where every new kind of failure starts</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={`${td} font-semibold`}>L4 Standing</td><td className={td}>Replays that kind <b>without asking each time</b>, within its limits.</td><td className={td}>{standing}</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={`${td} font-semibold`}>L5 Unattended</td><td className={td}>The same, with the widest limits.</td><td className={td}>More verified fixes, at a higher rate, and no duplicate</td></tr>
          </tbody>
        </table>
      </section>

      <section aria-label="What it depends on">
        <h3 className="mb-1.5 text-sm font-bold">What it depends on</h3>
        <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
          <Item tone="ok" title="Counted per kind of failure, on one cloud" note="Trust earned for one never carries to another" />
          <Item tone="ok" title="Only verified results count" note="Unverified, returned and failed results add nothing" />
          <Item tone="ok" title="The cloud can show it did not come back" note="Only a component that can reach Verified can earn it" />
          <Item tone="ok" title="No duplicate and no unsafe outcome" note="One duplicate sends it straight back to asking" />
          <Item tone="warn" title="Production is never automatic" note="A person decides there, at every level" />
        </ul>
      </section>

      <section aria-label="Which clouds can earn it">
        <h3 className="mb-1.5 text-sm font-bold">Which clouds can earn it</h3>
        <table className="w-full border border-[var(--color-border)]">
          <caption className="sr-only">Which components can earn autonomy</caption>
          <thead><tr className="bg-[var(--color-surface-muted)]"><th scope="col" className={th}>Component</th><th scope="col" className={th}>Can it earn L4 and L5?</th><th scope="col" className={th}>Why</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-t border-[var(--color-border)]">
                <td className={td}>{r.cloudName} {r.name}</td>
                <td className={td}><span className={`rounded-md px-2 py-0.5 text-xs font-semibold ${r.mayEarnAutonomy ? 'bg-green-50 text-green-800' : 'bg-amber-50 text-amber-800'}`}>{r.mayEarnAutonomy ? 'Yes' : 'Not on its own'}</span></td>
                <td className={td}>{evidenceSentence(r).text}</td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td className={td} colSpan={3}>{coverage.isError ? 'What ServiceHub covers could not be read.' : 'Reading…'}</td></tr>}
          </tbody>
        </table>
      </section>

      <section aria-label="What takes it away">
        <h3 className="mb-1.5 text-sm font-bold">What takes it away</h3>
        <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
          <Item tone="warn" title="A duplicate" note="Straight back to approval" />
          <Item tone="warn" title="The verified rate drops" note="Back to approval" />
          <Item tone="warn" title="The component’s settings change" note="Not built yet" />
          <li className="flex items-start gap-3 px-3 py-2 text-sm"><X className="mt-0.5 h-4 w-4 shrink-0 text-green-700" aria-hidden="true" /><span><span className="font-medium">Anything you turn off</span><span className="block text-xs text-[var(--color-text-muted)]">Emergency stop pauses the Agent; a person can still replay</span></span></li>
        </ul>
      </section>

      <section aria-label="Even at L4, the Agent is limited by">
        <h3 className="mb-1 text-sm font-bold">Even at L4, the Agent is limited by</h3>
        <p className="text-sm">Rate limits, a cap on how often the same failure may come back, a circuit breaker that stops a rule that keeps failing, and the emergency stop. How long it takes is how long it takes to collect the verified fixes for that kind of failure: it is a count, not a date.</p>
      </section>
    </div>
  )
}
