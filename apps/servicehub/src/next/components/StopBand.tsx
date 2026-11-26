import { OctagonAlert } from 'lucide-react'
import { formatWhen } from '../../lib/format'
import { useStopState } from '../lib/useStopState'

/**
 * Above every page while the emergency stop is on — and while its state cannot be read, which is treated as on. The stop halts every
 * replay ServiceHub would do on its own, so a person must never be left to find out from the absence of activity.
 */
export function StopBand() {
  const stop = useStopState()
  if (stop.kind === 'loading' || stop.kind === 'off') return null
  const unreadable = stop.kind === 'unreadable'
  return (
    <div role="alert" className="flex items-start gap-2.5 border-b border-red-300 bg-red-50 px-5 py-2.5 text-sm text-red-900">
      <OctagonAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <p>
        <strong>{unreadable ? 'Emergency stop: its state cannot be read, so it is shown as ON.' : 'Emergency stop is ON.'}</strong>{' '}
        {unreadable
          ? 'Nothing will replay on its own until it can be read and is off.'
          : <>No replay is done on its own, on any cloud.{stop.by ? ` Set by ${stop.by}` : ''}{stop.at ? ` at ${formatWhen(stop.at, new Date())}` : ''}{stop.by || stop.at ? '.' : ''}{stop.reason ? ` Reason: ${stop.reason}` : ''}</>}
      </p>
    </div>
  )
}
