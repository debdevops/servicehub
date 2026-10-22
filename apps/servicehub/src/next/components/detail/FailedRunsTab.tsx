import { ExternalLink } from 'lucide-react'
import { useState } from 'react'
import { Pager } from '../../../components/ui/Pager'
import { Select } from '../../../components/ui/Select'
import { useFailedItem, useFailedItems } from '../../../hooks/useComponentSource'
import { usePagedList } from '../../../hooks/usePagedList'
import { toProblem } from '../../../lib/api/client'
import type { ComponentSource, FailedItem } from '../../../lib/api/componentSources'
import { formatWhen } from '../../../lib/format'
import { Th, Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'
const ALL = '__all__'
// The cloud's own words for how a run ended. A state not listed is shown as the cloud wrote it, never relabelled.
const statusTone: Record<string, string> = { FAILED: 'bg-red-50 text-red-700', TIMED_OUT: 'bg-amber-50 text-amber-800', ABORTED: 'bg-slate-100 text-slate-700', CANCELLED: 'bg-slate-100 text-slate-700' }
const statusWords: Record<string, string> = { FAILED: 'Failed', TIMED_OUT: 'Timed out', ABORTED: 'Aborted', CANCELLED: 'Cancelled' }

/**
 * The failed runs of a component that is not a message store (PLAN-PLUG-AND-PLAY-COMPONENTS, tier T1). One tab for every such component:
 * what differs between Step Functions, Workflows and Logic Apps is what the source returns, so nothing here names a cloud or a service.
 *
 * Read-only by construction: there is no replay, re-run or purge control, because this version moves nothing on. It says so, and says
 * where to do it. It never reads a run's input or output, and a run's reason is read only when someone opens it.
 */
export function FailedRunsTab({ namespaceId, componentKey, source, componentName }: { namespaceId: string; componentKey: string; source: ComponentSource; componentName: string }) {
  const [place, setPlace] = useState<string>(ALL)
  const [open, setOpen] = useState<string | null>(null)
  const failed = useFailedItems(namespaceId, componentKey, place === ALL ? null : place, source.available)
  const detail = useFailedItem(namespaceId, componentKey, open)
  const { items, pager } = usePagedList(failed.data?.items ?? [])
  const now = new Date()
  const placeName = (id: string) => source.places.find((p) => p.id === id)?.name ?? id
  const selected = failed.data?.items.find((i) => i.id === open) ?? null

  return (
    <div className="space-y-4">
      <section aria-labelledby="runs-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
          <h2 id="runs-title" className="text-base font-semibold">Failed runs{failed.data ? ` (${failed.data.items.length}${failed.data.reachedLimit ? '+' : ''})` : ''}<Tip label="section:Failed runs" /></h2>
          {source.places.length > 0 && (
            <div className="flex items-center gap-1">
              <Select ariaLabel="Place" variant="inline" value={place} onChange={(v) => { setPlace(v); setOpen(null) }}>
                <option value={ALL}>All places</option>
                {source.places.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </Select>
              <Tip label="Place" />
            </div>
          )}
        </div>

        {(source.placesReachedLimit || source.placesIncomplete) && (
          <p role="status" className="border-t border-[var(--color-border)] bg-amber-50 px-4 py-2 text-sm text-amber-900">
            {source.placesIncomplete ? 'Part of the list of places could not be read, so one that exists may be missing from the Place filter.' : `Only the first ${source.places.length} places are listed in the Place filter.`}
          </p>
        )}
        {failed.isError ? (
          <p role="alert" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-red-800">{toProblem(failed.error).message} Nothing is shown rather than a list that may be wrong.</p>
        ) : failed.isPending ? (
          <p role="status" className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">Loading…</p>
        ) : (
          <>
            {failed.data.incomplete && (
              <p role="status" className="border-t border-[var(--color-border)] bg-amber-50 px-4 py-2 text-sm text-amber-900">
                Part of this read failed or took too long, so runs that exist may be missing. Choose one place to read it on its own.
              </p>
            )}
            {failed.data.reachedLimit && (
              <p role="status" className="border-t border-[var(--color-border)] bg-amber-50 px-4 py-2 text-sm text-amber-900">
                Showing the newest {failed.data.items.length}. There are more than this; choose one place to narrow it.
              </p>
            )}
            {failed.data.items.length === 0 ? (
              <p className="border-t border-[var(--color-border)] px-4 py-6 text-sm text-[var(--color-text-muted)]">
                No failed, timed-out or aborted {componentName} runs were found{failed.data.incomplete ? ' in what could be read' : ''}.
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full border-t border-[var(--color-border)]">
                  <caption className="sr-only">Failed {componentName} runs, newest first</caption>
                  <thead>
                    <tr><Th>When</Th><Th>Status</Th><Th>Place</Th><Th>Summary</Th><Th>Actions</Th></tr>
                  </thead>
                  <tbody>
                    {items.map((i) => <Row key={i.id} item={i} place={placeName(i.place)} open={open === i.id} onToggle={() => setOpen(open === i.id ? null : i.id)} now={now} />)}
                  </tbody>
                </table>
              </div>
            )}
            <Pager {...pager} label="Failed runs" />
          </>
        )}
        <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">
          ServiceHub only reads these. To run one again, open it in the cloud’s console: this version does not move work on for {componentName}.
        </p>
      </section>

      {open !== null && (
        <section aria-labelledby="run-detail-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
          <h2 id="run-detail-title" className="mb-2 text-base font-semibold">{selected?.summary ?? 'Run'}<Tip label="section:Run detail" /></h2>
          {detail.isPending ? <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading…</p>
            : detail.isError ? <p role="alert" className="text-sm text-red-800">{toProblem(detail.error).message}</p>
            : detail.data.item === null ? <p className="text-sm text-[var(--color-text-muted)]">The cloud no longer has this run.</p>
            : (
              <dl className="text-sm">
                <Fact label="Reason">{detail.data.reason ?? 'None recorded'}</Fact>
                <Fact label="Cause">{detail.data.cause ?? 'None recorded'}</Fact>
                <Fact label="Started">{detail.data.startedUtc ? formatWhen(detail.data.startedUtc, now) : '—'}</Fact>
                <Fact label="Stopped">{detail.data.stoppedUtc ? formatWhen(detail.data.stoppedUtc, now) : '—'}</Fact>
              </dl>
            )}
        </section>
      )}
    </div>
  )
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 last:border-0"><dt className="w-24 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 break-words font-semibold">{children}</dd></div>
}

function Row({ item: i, place, open, onToggle, now }: { item: FailedItem; place: string; open: boolean; onToggle: () => void; now: Date }) {
  return (
    <tr className="border-t border-[var(--color-border)]">
      <td className={td}>{i.atUtc ? formatWhen(i.atUtc, now) : '—'}</td>
      <td className={td}><span className={`inline-block rounded-md px-2 py-0.5 text-xs font-semibold ${statusTone[i.status] ?? 'bg-slate-100 text-slate-700'}`}>{statusWords[i.status] ?? i.status}</span></td>
      <td className={td}>{place}</td>
      <td className={`${td} break-words`}>{i.summary}</td>
      <td className={td}>
        <div className="flex flex-wrap gap-2">
          <button type="button" aria-expanded={open} aria-label={`${open ? 'Hide' : 'Show'} details of ${i.summary}`} onClick={onToggle} className="rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold text-[var(--color-primary-700)] hover:bg-[var(--color-primary-50)]">{open ? 'Hide' : 'Details'}</button>
          {i.consoleUrl && (
            <a href={i.consoleUrl} target="_blank" rel="noreferrer noopener" aria-label={`Open ${i.summary} in the cloud console`} className="inline-flex items-center gap-1 rounded-lg border border-[var(--color-border)] px-3 py-1.5 text-xs font-semibold hover:bg-[var(--color-surface-muted)]">
              Console <ExternalLink className="h-3 w-3" aria-hidden="true" />
            </a>
          )}
        </div>
      </td>
    </tr>
  )
}
