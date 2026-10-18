import { Link } from 'react-router-dom'
import { Pager } from '../../../components/ui/Pager'
import { useDlqObserver } from '../../../hooks/useNamespaces'
import { usePagedList } from '../../../hooks/usePagedList'
import { useSettings } from '../../../hooks/useSettings'
import type { ConnectedComponent } from '../../../lib/api/components'
import type { Namespace } from '../../../lib/api/namespaces'
import { formatWhen } from '../../../lib/format'
import { entityRows } from '../../lib/entityRows'
import { channelsWords, observerWords } from '../../lib/words'
import { nextEntry, nextHref } from '../../nav/navigation'
import { Th } from '../Tip'
import { Tip } from '../Tip'

const td = 'px-3 py-2.5 align-top text-sm'

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="flex gap-4 border-b border-[var(--color-border)] py-2 text-sm last:border-0"><dt className="w-32 shrink-0 text-[var(--color-text-muted)]">{label}</dt><dd className="min-w-0 font-semibold">{children}</dd></div>
}

/**
 * The Configuration tab of one connection (build-plan unit D6; design `p05e-service-bus-configuration`). Two halves, kept apart so
 * nothing is mistaken for the other: what the CLOUD holds for each place (read-only; ServiceHub never changes it), and the few
 * settings SERVICEHUB keeps for this connection.
 *
 * Only what is really read is drawn. Today the API returns one cloud-side setting per place, where its dead letters go; the limits the
 * design shows (max deliveries, lock duration, time to live, sessions) are read inside the adapters but not carried to the screen, so
 * the page says so instead of drawing columns of dashes (unit D6b). Nothing here is a setting that is not stored.
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
              <thead><tr><Th>Entity</Th><Th>Type</Th><Th>Dead letters go to</Th></tr></thead>
              <tbody>
                {items.map((e) => (
                  <tr key={`${e.parent ?? ''}/${e.name}`} className="border-t border-[var(--color-border)]">
                    <th scope="row" className={`${td} text-left font-semibold`}>{e.parent ? `${e.parent} / ${e.name}` : e.name}</th>
                    <td className={`${td} capitalize`}>{e.kind}</td>
                    <td className={td}>{e.deadLetterStore ?? <span className="text-[var(--color-text-muted)]">Its own dead-letter place, not a separate one</span>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pager {...pager} label="places" />
        <p className="border-t border-[var(--color-border)] px-4 py-2 text-xs text-[var(--color-text-muted)]">
          Read from {cloudName} and shown as they are. ServiceHub never changes them. The limits each place holds (how many deliveries before a message is dead-lettered, how long it is locked, how long it lives) are not read yet, so they are not shown.
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
