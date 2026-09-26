import { useState } from 'react'
import { CircleAlert, CircleCheck, CircleHelp } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import type { OverlayBodyProps } from '../overlays/registry'
import { useNamespaces, useRemoveNamespace, useTestConnection } from '../../hooks/useNamespaces'
import type { Namespace } from '../../lib/api/namespaces'
import { providerLabel, providerService } from '../../lib/providers'
import { environmentMeta, groupByCloudEnvironment } from '../provider/scopeChoice'

const order = ['azure', 'aws', 'gcp'] as const

function status(ns: Namespace) {
  if (ns.lastConnectionTestSucceeded === true) return { Icon: CircleCheck, text: 'Connected', tone: 'text-[var(--color-success)]' }
  if (ns.lastConnectionTestSucceeded === false) return { Icon: CircleAlert, text: 'Could not connect at last check', tone: 'text-[var(--color-warning)]' }
  return { Icon: CircleHelp, text: 'Not tested yet', tone: 'text-[var(--color-text-muted)]' }
}

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : 'never')

/**
 * Connections (`?panel=connections`): every namespace, grouped by cloud, then environment, with whether it answered at its
 * last check. Test re-checks one now and says why when it fails. Remove asks twice. The credential is
 * never shown — the API never sends it (R5).
 */
export default function ConnectionsPanel({ close }: OverlayBodyProps) {
  const namespaces = useNamespaces()
  const test = useTestConnection()
  const remove = useRemoveNamespace()
  const { search } = useLocation()
  const [results, setResults] = useState<Record<string, string>>({})
  const [confirming, setConfirming] = useState<string | null>(null)

  if (namespaces.isPending) return <p role="status" className="text-sm text-[var(--color-text-muted)]">Loading your connections…</p>
  if (namespaces.isError) return <p role="alert" className="text-sm text-[var(--color-error)]">Couldn’t load your connections.</p>

  const runTest = (ns: Namespace) =>
    test.mutate(ns.id, {
      onSuccess: (r) => setResults((cur) => ({ ...cur, [ns.id]: r.message })),
      onError: () => setResults((cur) => ({ ...cur, [ns.id]: 'The test itself could not run. Try again.' })),
    })

  const addHref = `?${new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(search)), modal: 'add-cloud' })}`

  return (
    <div className="space-y-6">
      {order.map((provider) => {
        const environments = groupByCloudEnvironment(namespaces.data.filter((n) => n.provider === provider))[0]?.environments ?? []
        if (environments.length === 0) return null
        return (
          <section key={provider} aria-label={providerLabel[provider]}>
            <h3 className="mb-2 text-sm font-semibold text-[var(--color-text)]">
              {providerLabel[provider]} <span className="font-normal text-[var(--color-text-muted)]">· {providerService[provider]}</span>
            </h3>
            {environments.map(({ env, items }) => (
            <div key={env} role="group" aria-label={environmentMeta[env].label} className="mb-3 last:mb-0">
            <h4 className="mb-1.5 flex items-center gap-2 text-[11px] font-bold uppercase tracking-wider text-[var(--color-text-muted)]">
              <span aria-hidden="true" className="inline-block h-2 w-2 rounded-full" style={{ background: environmentMeta[env].dot }} />
              {environmentMeta[env].label}
              <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold normal-case tracking-normal ${environmentMeta[env].chip}`}>{items.length}</span>
            </h4>
            <ul className="space-y-2">
              {items.map((ns) => {
                const s = status(ns)
                const testingThis = test.isPending && test.variables === ns.id
                return (
                  <li key={ns.id} className="rounded-lg border border-[var(--color-border)] p-3 text-sm">
                    <p className="font-medium text-[var(--color-text)]">{ns.displayName ?? ns.name}</p>
                    <p className="text-xs text-[var(--color-text-muted)]">
                      {ns.name}
                      {ns.awsRegion ? ` · ${ns.awsRegion}` : ''}
                      {ns.gcpProjectId ? ` · ${ns.gcpProjectId}` : ''}
                    </p>
                    <p className={`mt-2 flex items-center gap-1.5 ${s.tone}`}>
                      <s.Icon className="h-4 w-4" aria-hidden="true" /> {s.text}
                    </p>
                    <p className="text-xs text-[var(--color-text-muted)]">Last checked: {when(ns.lastConnectionTestAt)}</p>
                    {results[ns.id] && <p role="status" className="mt-1 text-xs text-[var(--color-text)]">{results[ns.id]}</p>}
                    {confirming === ns.id ? (
                      <div role="alertdialog" aria-label={`Remove ${ns.displayName ?? ns.name}`} className="mt-3 rounded-md border border-[var(--color-border)] bg-[var(--color-surface-muted)] p-3">
                        <p className="text-sm text-[var(--color-text)]">
                          Remove <b>{ns.displayName ?? ns.name}</b>? ServiceHub stops watching it. The cloud itself is not touched.
                        </p>
                        <div className="mt-3 flex justify-end gap-2">
                          <button
                            type="button"
                            onClick={() => setConfirming(null)}
                            className="rounded-md border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-xs font-medium hover:bg-[var(--color-surface-muted)]"
                          >
                            Cancel
                          </button>
                          <button
                            type="button"
                            onClick={() => remove.mutate(ns.id, { onSuccess: () => { setConfirming(null); if ((namespaces.data?.length ?? 0) <= 1) close() } })}
                            className="rounded-md bg-[var(--color-error)] px-3 py-1.5 text-xs font-medium text-white hover:opacity-90"
                          >
                            Remove connection
                          </button>
                        </div>
                      </div>
                    ) : (
                      <div className="mt-3 flex items-center justify-between gap-3">
                        <button
                          type="button"
                          disabled={testingThis}
                          onClick={() => runTest(ns)}
                          className="whitespace-nowrap rounded-md border border-[var(--color-border)] px-3 py-1.5 text-xs font-medium hover:bg-[var(--color-surface-muted)] disabled:opacity-60"
                        >
                          {testingThis ? 'Testing…' : 'Test connection'}
                        </button>
                        <button type="button" onClick={() => setConfirming(ns.id)} className="whitespace-nowrap text-xs text-[var(--color-text-muted)] hover:text-[var(--color-error)] hover:underline">
                          Remove
                        </button>
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
            </div>
            ))}
          </section>
        )
      })}
      <Link to={addHref} className="inline-block text-sm text-[var(--color-primary-700)] hover:underline">
        + Add a cloud
      </Link>
    </div>
  )
}
