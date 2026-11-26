import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { useMe } from '../../hooks/useIdentity'
import { useNamespaces, useRemoveNamespace, useTestConnection, namespaceKeys } from '../../hooks/useNamespaces'
import { useComponentList } from '../../hooks/useComponentEnablement'
import { toProblem } from '../../lib/api/client'
import type { AuthType, CloudProvider, Namespace } from '../../lib/api/namespaces'
import { formatAgo } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { providerLabel } from '../../lib/providers'
import { environmentMeta } from '../../components/provider/scopeChoice'
import { NotAllowed } from '../../components/ui/NotAllowed'
import { Skeleton } from '../../components/ui/Skeleton'
import { Tip } from '../components/Tip'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { nextEntry, nextHref } from '../nav/navigation'

const providers: readonly CloudProvider[] = ['azure', 'aws', 'gcp']

/** How a connection signs in, in words. A pasted secret is the second choice and says so; an identity stores nothing. */
const signIn: Readonly<Record<AuthType, { readonly words: string; readonly secret: boolean }>> = {
  connectionString: { words: 'Connection string', secret: true },
  awsAccessKey: { words: 'Access key', secret: true },
  gcpServiceAccount: { words: 'Service-account key', secret: true },
  managedIdentity: { words: 'Managed identity', secret: false },
  servicePrincipal: { words: 'Service principal', secret: true },
  defaultAzureCredential: { words: 'Azure sign-in', secret: false },
  awsIamRole: { words: 'IAM role', secret: false },
  awsOidc: { words: 'AWS OIDC', secret: false },
  gcpWorkloadIdentity: { words: 'Workload identity', secret: false },
}

/** Where on the cloud this connection points: a namespace, a region or a project. */
const placeOf = (n: Namespace): string =>
  n.provider === 'aws' ? (n.awsRegion ?? n.name) : n.provider === 'gcp' ? (n.gcpProjectId ?? n.name) : n.name

/**
 * Connections (5.0.0): every connection ServiceHub holds, for every provider, in one place. Each says where it points, how it signs in,
 * whether it could still reach the cloud the last time it was tested, and which components are on for it. From here a person tests a
 * connection, adds a component to it, or removes it; connecting a new cloud is the one button at the top. Nothing on this page shows a
 * credential: they are stored encrypted and never returned.
 */
export default function ConnectionsPage() {
  const { pathname } = useLocation()
  const client = useQueryClient()
  const namespaces = useNamespaces()
  const admin = permission(useMe().data, 'Admin', 'connect or remove a cloud')
  const all = namespaces.data ?? []

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope="all" title="Connections" count={null} updatedAtMs={namespaces.dataUpdatedAt} onRefresh={() => void client.invalidateQueries({ queryKey: namespaceKeys.all })}
        lookingAt="Every cloud account ServiceHub is connected to, on every provider: whether it can still reach the cloud, how it signs in, and which components are on for it." />

      <div className="flex flex-wrap items-center gap-2">
        <Link to={nextHref(nextEntry('connect'), {}, pathname)} aria-disabled={!admin.allowed}
          className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-xs font-bold text-white hover:bg-[var(--color-primary-700)]">
          <Plus className="h-4 w-4" aria-hidden="true" /> Connect a cloud
        </Link>
        <Tip label="Connect a cloud" />
        <Link to={nextHref(nextEntry('add-component'), {}, pathname)} className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] px-3.5 py-2 text-xs font-semibold hover:bg-[var(--color-surface-muted)]">
          Add a component
        </Link>
        <Tip label="Add a component" />
      </div>
      <NotAllowed reason={admin.reason} />

      {namespaces.isPending && <Skeleton label="Reading connections…" rows={4} />}
      {namespaces.isError && <p role="alert" className="text-sm text-red-800">{toProblem(namespaces.error).message}</p>}

      {namespaces.data && providers.map((provider) => {
        const mine = all.filter((n) => n.provider === provider)
        return (
          <section key={provider} aria-label={`${providerLabel[provider]} connections`} className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)]">
            <h2 className="flex items-center gap-2 px-4 py-3 text-base font-semibold">
              {providerLabel[provider]} connections ({mine.length})<Tip label="section:Connections by cloud" />
            </h2>
            {mine.length === 0 ? (
              <p className="border-t border-[var(--color-border)] px-4 py-4 text-sm text-[var(--color-text-muted)]">
                No {providerLabel[provider]} connection yet. <Link className="font-semibold underline" to={nextHref(nextEntry('connect'), {}, pathname)}>Connect a cloud</Link> to add one.
              </p>
            ) : (
              <ul className="divide-y divide-[var(--color-border)] border-t border-[var(--color-border)]">
                {mine.map((n) => <Row key={n.id} ns={n} admin={admin} pathname={pathname} />)}
              </ul>
            )}
          </section>
        )
      })}
      <p className="text-xs text-[var(--color-text-muted)]">Credentials are stored encrypted and never shown again. To change one, connect again and remove the old connection.</p>
    </div>
  )
}

function Row({ ns, admin, pathname }: { ns: Namespace; admin: { allowed: boolean }; pathname: string }) {
  const test = useTestConnection()
  const remove = useRemoveNamespace()
  const components = useComponentList(ns.id)
  const [confirming, setConfirming] = useState(false)
  const ok = ns.lastConnectionTestSucceeded
  const name = ns.displayName ?? ns.name
  const on = (components.data?.components ?? []).filter((c) => c.state === 'on' || c.state === 'alwaysOn').map((c) => c.name)
  const how = signIn[ns.authType]
  const btn = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-xs font-semibold hover:bg-[var(--color-surface-muted)] disabled:opacity-50'
  return (
    <li className="flex flex-wrap items-start gap-3 px-4 py-3">
      <div className="min-w-0 flex-1">
        <p className="flex flex-wrap items-center gap-2 text-sm font-semibold">
          {name}
          <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${environmentMeta[ns.environment].chip}`}>{environmentMeta[ns.environment].label}</span>
        </p>
        <p className="text-xs text-[var(--color-text-muted)]">
          {placeOf(ns)} · {how?.words ?? ns.authType}{how?.secret ? '' : ' · no secret stored'}
          {ns.lastConnectionTestAt ? ` · checked ${formatAgo(ns.lastConnectionTestAt, new Date())}` : ' · not checked yet'}
        </p>
        <p className="mt-1 text-xs">
          <span className="text-[var(--color-text-muted)]">Components on<Tip label="addc:on-for-connection" /> </span>
          {components.isPending ? '…' : on.length > 0 ? on.join(', ') : 'none yet'}
        </p>
      </div>
      <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${ok ? 'bg-[var(--color-success-light)] text-green-800' : ok === false ? 'bg-[var(--color-warning-light)] text-amber-800' : 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]'}`}>
        {ok ? 'Connected' : ok === false ? 'Could not connect' : 'Not tested yet'}
      </span>
      <button type="button" className={btn} disabled={test.isPending} onClick={() => test.mutate(ns.id)} aria-label={`Test ${name}`}>{test.isPending ? 'Testing…' : 'Test'}</button>
      <Link className={btn} to={`${nextHref(nextEntry('add-component'), {}, pathname)}&ns=${ns.id}`} aria-label={`Add a component to ${name}`}>Add a component</Link>
      {confirming ? (
        <span className="flex items-center gap-2 text-xs">
          Remove {name}? Its recorded history is kept.
          <button type="button" className="rounded-lg bg-[#dc2626] px-3 py-1.5 font-semibold text-white" disabled={remove.isPending} onClick={() => remove.mutate(ns.id)}>Remove</button>
          <button type="button" className={btn} onClick={() => setConfirming(false)}>Keep</button>
        </span>
      ) : (
        <button type="button" className={btn} disabled={!admin.allowed} aria-label={`Remove ${name}`} onClick={() => setConfirming(true)}><Trash2 className="h-4 w-4" aria-hidden="true" /></button>
      )}
      {test.data && <p role="status" className="w-full text-xs text-[var(--color-text-muted)]">{test.data.message}</p>}
      {(remove.isError || test.isError) && <p role="alert" className="w-full text-xs text-red-800">{toProblem(remove.error ?? test.error).message}</p>}
    </li>
  )
}
