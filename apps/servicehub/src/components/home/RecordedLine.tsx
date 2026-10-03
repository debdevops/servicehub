import { formatWhen } from '../../lib/format'

/**
 * "N recorded · newest HH:MM" — what a namespace ServiceHub does not watch on its own has to say
 * instead of a live count (plan §5.2, §5.3). `newestIso` is the newest recorded dead letter's
 * `detectedAtUtc`; `null` means nothing has been recorded there yet.
 */
export function RecordedLine({ total, newestIso, now = new Date() }: { total: number; newestIso: string | null; now?: Date }) {
  if (total === 0 || newestIso === null) return <>No stuck dead letter is recorded yet — press Look now</>
  return <>{total.toLocaleString()} recorded · newest {formatWhen(newestIso, now)}</>
}
