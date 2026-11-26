import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import type { NextOverlayBodyProps } from './registry'
import { Th } from '../components/Tip'
import { Tip } from '../components/Tip'

const td = 'px-3 py-2.5 align-top text-sm'

/**
 * Security and data (build-plan unit F10; design `d13-security-and-data`), at `?panel=security`: what ServiceHub keeps on this machine,
 * how, and for how long; and what it may do in your cloud. **Only what is checked is said.** The design's two marked lines are left out
 * until design check C16 is done: that nothing else leaves the machine, and that secrets never reach the logs. Nor is "the full body of a
 * message is read from your cloud" said: it is not the same on every page. Each statement below is already stated by the product itself
 * (the Access tab shows encryption at rest and the key; the Message dialog says what is kept of a body; the ledger is hash-chained).
 */
export default function SecurityPanel(_props: NextOverlayBodyProps) { // eslint-disable-line @typescript-eslint/no-unused-vars
  useOverlayTitle('Security and data', 'What is kept, for how long, and what ServiceHub may do')
  return (
    <div className="space-y-5 text-sm">
      <section aria-label="What ServiceHub keeps on this machine">
        <h3 className="mb-1.5 text-sm font-bold">What ServiceHub keeps on this machine<Tip label="section:What ServiceHub keeps on this machine" /></h3>
        <table className="w-full border border-[var(--color-border)]">
          <caption className="sr-only">What ServiceHub keeps, how, and for how long</caption>
          <thead><tr className="bg-[var(--color-surface-muted)]"><Th>What</Th><Th>How it is kept</Th><Th>For how long</Th></tr></thead>
          <tbody>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Connection strings and keys</td><td className={td}>Encrypted. The key can be rotated. Never shown again after you save it</td><td className={td}>Until you delete the connection</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>A message’s fingerprint</td><td className={td}>A hash of the body, so a replay can be recognised</td><td className={td}>As long as the ledger</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>The first 500 characters of a dead-lettered message</td><td className={td}><b>Plain text</b> in the local database</td><td className={td}><b>Not removed by age yet</b> (an open decision)</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>The recovery ledger</td><td className={td}>Each entry is chained to the one before by a hash, so a changed or missing entry is detectable</td><td className={td}>Kept</td></tr>
          </tbody>
        </table>
      </section>

      <section aria-label="What ServiceHub can do in your cloud">
        <h3 className="mb-1.5 text-sm font-bold">What ServiceHub can do in your cloud<Tip label="section:What ServiceHub can do in your cloud" /></h3>
        <table className="w-full border border-[var(--color-border)]">
          <caption className="sr-only">The rights ServiceHub uses</caption>
          <thead><tr className="bg-[var(--color-surface-muted)]"><Th>Right</Th><Th>Lets ServiceHub</Th></tr></thead>
          <tbody>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Read</td><td className={td}>See dead-letter messages and counts. Never changes a message</td></tr>
            <tr className="border-t border-[var(--color-border)]"><td className={td}>Replay</td><td className={td}>Put one message back, on approval or by a rule you made, through the same safety checks every time</td></tr>
          </tbody>
        </table>
      </section>

      <section aria-label="What stops it">
        <h3 className="mb-1 text-sm font-bold">What stops it<Tip label="section:What stops it" /></h3>
        <p>Emergency stop halts everything ServiceHub would do on its own. A person can still replay one message. It cannot recall a move the cloud itself has already started.</p>
      </section>

      <p className="rounded-xl border border-amber-300 bg-amber-50 px-3.5 py-2.5 text-xs text-amber-950">
        <b>Not said here yet:</b> where else data may go, and what reaches the logs. Those are being checked, and this panel will not say them until they are.
      </p>
    </div>
  )
}
