import { describeEntity, kindWord } from '../../lib/entities'

/**
 * Where a message is stuck, named the same on every cloud: a queue by its name, or a topic's subscription as
 * `topic › subscription`. The small tag says which of the two it is, so a topic is never mistaken for a queue.
 */
export function EntityCell({ entityName, entityType, topicName, note, size = 'md' }: { entityName: string; entityType: string; topicName?: string | null; note?: string; size?: 'sm' | 'md' }) {
  const e = describeEntity(entityName, entityType, topicName)
  const text = size === 'sm' ? 'text-[12px]' : 'text-[13px]'
  return (
    <div className="min-w-0">
      {/* Long names (Google's are) wrap instead of being cut off: a name you cannot read in full is a name you cannot trust. */}
      {e.kind === 'subscription' && e.topic && (
        <div title={`Topic: ${e.topic}`} className={`break-all font-mono text-[var(--color-text-muted)] ${text}`}>{e.topic}</div>
      )}
      <div title={`${kindWord(e.kind)}: ${e.name}`} className={`break-all font-mono font-medium ${text}`}>
        {e.kind === 'subscription' && e.topic && <span aria-hidden="true" className="mr-1 text-[var(--color-text-muted)]">›</span>}
        {e.name}
      </div>
      <div className="mt-1 flex items-center gap-1.5">
        <span className="rounded bg-[var(--color-surface-muted)] px-1.5 py-px text-[10px] font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">
          {e.kind === 'subscription' ? 'Topic subscription' : 'Queue'}
        </span>
        {note && <span className="text-xs text-[var(--color-text-muted)]">{note}</span>}
      </div>
    </div>
  )
}
