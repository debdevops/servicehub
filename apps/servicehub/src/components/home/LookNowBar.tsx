import { Eye, Loader2 } from 'lucide-react'
import { lookNowReason } from '../../content/capabilities'
import { RecordedLine } from './RecordedLine'
import { useDeadLetters, useLookAtDeadLetters } from '../../hooks/useDeadLetters'
import type { CloudProvider, EnvironmentKind } from '../../lib/api/namespaces'
import { providerLabel } from '../../lib/providers'

/**
 * "ServiceHub records {Cloud}'s dead letters when you ask — it doesn't watch on its own" (plan §5.3):
 * shown only for a cloud whose namespaces are not watched. One button records every namespace in scope
 * (the same `lookAtDeadLetters` the All-clouds table and the Dead letters tab use), and the line below
 * it is the same "N recorded · newest HH:MM" every not-watched cell on this page uses.
 */
export function LookNowBar({ provider, namespaceIds, namespaceId, environment }: {
  provider: CloudProvider
  namespaceIds: readonly string[]
  namespaceId?: string
  environment?: EnvironmentKind
}) {
  const cloud = providerLabel[provider]
  const recorded = useDeadLetters({ provider, namespaceId, environment, status: 'active', page: 1, pageSize: 1 })
  const look = useLookAtDeadLetters()

  return (
    <div className="flex flex-wrap items-center gap-4 rounded-xl border border-[var(--color-primary-200)] bg-gradient-to-r from-[var(--color-primary-50)] to-[var(--color-surface)] px-4 py-3">
      <Eye className="h-5 w-5 shrink-0 text-[var(--color-primary-700)]" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-[12.5px] font-bold text-[var(--color-primary-800)]">ServiceHub records {cloud}’s dead letters when you ask — it doesn’t watch on its own</p>
        <p className="text-[11.5px] text-[var(--color-primary-700)]">
          {lookNowReason[provider]} <RecordedLine total={recorded.data?.paging.total ?? 0} newestIso={recorded.data?.items[0]?.detectedAtUtc ?? null} />.
        </p>
      </div>
      <button
        type="button"
        disabled={look.isPending || namespaceIds.length === 0}
        onClick={() => look.mutate(namespaceIds)}
        className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-[12.5px] font-bold text-white hover:bg-[var(--color-primary-700)] disabled:opacity-60"
      >
        {look.isPending ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Eye className="h-4 w-4" aria-hidden="true" />}
        {look.isPending ? 'Looking…' : 'Look now'}
      </button>
    </div>
  )
}
