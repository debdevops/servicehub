import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Pager } from '../../../components/ui/Pager'
import { useDlqObserver } from '../../../hooks/useNamespaces'
import { usePagedList } from '../../../hooks/usePagedList'
import { useSettings } from '../../../hooks/useSettings'
import type { ConnectedComponent } from '../../../lib/api/components'
import { fetchEntitySettings, type EntityKind, type Namespace } from '../../../lib/api/namespaces'
import { formatWhen } from '../../../lib/format'
import { entityRows, fullName } from '../../lib/entityRows'
import { channelsWords, observerWords } from '../../lib/words'
import { nextEntry, nextHref } from '../../nav/navigation'
import { Th } from '../Tip'
import { Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm last:border-0"><dt className="w-32 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 font-semibold">{children}</dd></div>
}

/** One place's limits, read from the cloud only when asked (some clouds need a call per place). Nothing is drawn that was not read. */
function Limits({ namespaceId, name, kind }: { namespaceId: string; name: string; kind: EntityKind }) {
  const [asked, setAsked] = useState(false)
  const read = useQuery({ queryKey: ['entity-settings', namespaceId, kind, name], queryFn: () => fetchEntitySettings(namespaceId, name, kind), enabled: asked, staleTime: 60_000 })
  if (!asked) return <button type="button" onClick={() => setAsked(true)} className="rounded-lg border border-[var(--color-border)] px-2.5 py-1 text-xs font-bold hover:bg-[var(--color-surface-muted)]">Show limits</button>
  if (read.isPending) return <span className="text-[var(--color-text-muted)]">Reading…</span>
  if (read.isError) return <span role="alert" className="text-[var(--color-text-muted)]">Could not be read. <button type="button" onClick={() => void read.refetch()} className="font-bold underline">Try again</button></span>
  if (read.data.settings === null) return <span className="text-[var(--color-text-muted)]">Not read for this kind of place</span>
  if (read.data.settings.length === 0) return <span className="text-[var(--color-text-muted)]">The cloud reported none</span>
  return <dl className="space-y-0.5">{read.data.settings.map((s) => <div key={s.label} className="flex gap-2"><dt className="text-[var(--color-text-muted)]">{s.label}</dt><dd className="font-semibold">{s.value}</dd></div>)}</dl>
}

/**
 * The Configuration tab of one connection (build-plan unit D6; design `p05e-service-bus-configuration`). Two halves, kept apart so
 * nothing is mistaken for the other: what the CLOUD holds for each place (read-only; ServiceHub never changes it), and the few
 * settings SERVICEHUB keeps for this connection.
 *
 * Only what is really read is drawn. Where its dead letters go comes with the list; the limits (max deliveries, lock duration, time to
 * live, sessions) are read on demand, one place at a time, through `/entities/settings` (unit D6b), and a kind of place the cloud does
 * not read says "Not read" instead of drawing dashes. Nothing here is a setting that is not stored.
 */
export function ConfigurationTab({ component: c, namespace: ns, cloudName }: { component: ConnectedComponent; namespace: Namespace | undefined; cloudName: string }) {
  const settings = useSettings()
  // Only a cloud that cannot confirm a fix by itself has an observer to set up (the cloud's own capability, never its name).
  const wantsObserver = ns?.capabilities?.canProveDlqAbsence === false
  const observer = useDlqObserver(c.namespaceId, wantsObserver)
  const { items, pager } = usePagedList(entityRows(c.entities, () => null).flatMap((r) => (r.kind === 'place' ? [r.entity] : [])))
  const now = new Date()
  const fmt = (iso: string) => formatWhen(iso, now)

  return (
    <div className="space-y-4">
      <section aria-labelledby="cloud-title" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
        <h2 id="cloud-title" className="px-4 pt-4 text-base font-semibold">Settings read from {cloudName}<Tip label="section:Settings read from the cloud" /></h2>
        {c.entities.length === 0 ? (
          <p className="px-4 py-6 text-sm text-[var(--color-text-muted)]">{c.reachable ? 'No queue or subscription is listed for this connection.' : 'Its places cannot be listed until it can be reached.'}</p>
        ) : (
          <div className="mt-3 overflow-x-auto">
            <table className="w-full border-t border-[var(--color-border)]">
              <caption className="sr-only">Settings {cloudName} holds for each place in {c.name}</caption>
              <thead><tr><Th>Entity</Th><Th>Type</Th><Th>Dead letters go to</Th><Th>Limits</Th></tr></thead>
              <tbody>
                {items.map((e) => (
                  <tr key={`${e.parent ?? ''}/${e.name}`} className="border-t border-[var(--color-border)]">
                    <th scope="row" className={`${td} text-left font-semibold`}>{e.parent ? `${e.parent} / ${e.name}` : e.name}</th>
                    <td className={`${td} capitalize`}>{e.kind}</td>
                    <td className={td}>{e.deadLetterStore ?? <span className="text-[var(--color-text-muted)]">Its own dead-letter place, not a separate one</span>}</td>
                    <td className={td}>{e.kind === 'queue' || e.kind === 'subscription' ? <Limits namespaceId={c.namespaceId} name={fullName(e)} kind={e.kind} /> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pager {...pager} label="places" />
        <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">
          Read from {cloudName} and shown as they are. ServiceHub never changes them. Press Show limits on a place to read its limits (how many deliveries before a message is dead-lettered, how long it is locked, how long it lives) from {cloudName}; they are read one place at a time, only when you ask.
        </p>
      </section>

      <section aria-labelledby="own-title" className="max-w-xl rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <h2 id="own-title" className="mb-2 text-base font-semibold">ServiceHub’s own settings for this connection<Tip label="section:ServiceHub’s own settings" /></h2>
        <dl>
          <Fact label="Tell">{settings.isError ? 'Could not be read' : channelsWords(settings.data?.notifications.channels)}</Fact>
          <Fact label="Environment"><span className="capitalize">{c.environment}</span></Fact>
          <Fact label="Looks">{c.watched ? 'Automatically' : 'Only when you press Look now'}</Fact>
          {wantsObserver && (
            <Fact label="Confirming fixes">
              {observer.isError ? 'Could not be read' : observer.isPending ? '…' : observer.data.needed ? observerWords(observer.data, fmt) : 'Nothing to set up'}
            </Fact>
          )}
        </dl>
        <Link to={nextHref(nextEntry('configuration'))} className="mt-3 inline-block rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-bold hover:bg-[var(--color-surface-muted)]">Change</Link>
      </section>
    </div>
  )
}
