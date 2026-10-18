import { Eye, Loader2, TriangleAlert } from 'lucide-react'
import { lookNowReason } from '../../../content/capabilities'
import { useLookAtDeadLetters } from '../../../hooks/useDeadLetters'
import { toProblem } from '../../../lib/api/client'
import type { Me } from '../../../lib/api/identity'
import type { CloudProvider } from '../../../lib/api/namespaces'
import { permission } from '../../../lib/permissions'
import { Tip } from '../Tip'

/**
 * Shown when some component is not watched in the background (build-plan unit C1). It says why, from the cloud's own reason, and
 * carries the one Look now button — the same mutation the 4.2.0 Home uses, one look per namespace, never faster than the cloud allows.
 */
export function NotWatchedNotice({ cloudName, provider, namespaceIds, me, onLook }: {
  cloudName: string
  provider: CloudProvider
  namespaceIds: readonly string[]
  me: Me | undefined
  onLook?: () => void
}) {
  const look = useLookAtDeadLetters()
  const allowed = permission(me, 'Operator', 'look at these dead letters', { recover: true })
  const disabled = look.isPending || namespaceIds.length === 0 || !allowed.allowed
  return (
    <div role="note" className="flex flex-wrap items-center gap-3 rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950">
      <TriangleAlert className="h-5 w-5 shrink-0 text-amber-700" aria-hidden="true" />
      <p className="min-w-0 flex-1">
        <strong>{cloudName} is not watched in the background.</strong> {lookNowReason[provider]}{' '}
        <strong>Look now</strong> records what is in each dead-letter queue now.
        {look.isError && <span className="ml-1 font-semibold text-red-700"> {toProblem(look.error).message}</span>}
        {look.isSuccess && (
          <span className="ml-1 font-semibold">
            {look.data.failed > 0 ? ` ${look.data.failed} could not be read.` : ` Looked: ${look.data.newMessages} new.`}
          </span>
        )}
      </p>
      <button
        type="button"
        disabled={disabled}
        title={allowed.reason ?? undefined}
        onClick={() => look.mutate(namespaceIds, { onSuccess: () => onLook?.() })}
        className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-amber-400 bg-white px-3.5 py-2 text-xs font-bold text-amber-950 hover:bg-amber-100 disabled:opacity-60"
      >
        {look.isPending ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Eye className="h-4 w-4" aria-hidden="true" />}
        {look.isPending ? 'Looking…' : 'Look now'}
      </button>
      <Tip label="Look now" />
    </div>
  )
}
