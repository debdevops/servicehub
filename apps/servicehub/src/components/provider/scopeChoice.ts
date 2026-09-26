import type { CloudProvider, EnvironmentKind, Namespace } from '../../lib/api/namespaces'

/** Most sensitive first, so Production is never buried under Development. */
export const environmentOrder: readonly EnvironmentKind[] = ['prod', 'uat', 'dev']

export const environmentMeta: Record<EnvironmentKind, { label: string; dot: string; chip: string }> = {
  prod: { label: 'Production', dot: '#dc2626', chip: 'bg-[#fee2e2] text-[#991b1b]' },
  uat: { label: 'UAT', dot: '#d97706', chip: 'bg-[#fef3c7] text-[#92400e]' },
  dev: { label: 'Development', dot: '#16a34a', chip: 'bg-[#dcfce7] text-[#166534]' },
}

/** "orders-prod · Production": a namespace named with its environment, so two look different wherever they sit side by side. */
export const namespaceTag = (n: Namespace): string => {
  const name = n.displayName ?? n.name
  const env = environmentMeta[n.environment]?.label
  return env ? `${name} · ${env}` : name
}

export interface ScopeChoice {
  /** Set when one namespace is chosen. */
  readonly ns: Namespace | null
  /** Set when a whole environment is chosen. */
  readonly env: EnvironmentKind | null
  /** The namespaces the page should show. */
  readonly namespaces: readonly Namespace[]
}

/**
 * Reads `?ns=` / `?env=` against the namespaces of the current cloud. An id or environment that is not
 * in this cloud (another cloud's, or one that no longer exists) is ignored, so a stale link shows everything.
 */
export function resolveScope(inCloud: readonly Namespace[], params: URLSearchParams): ScopeChoice {
  const ns = inCloud.find((n) => n.id === params.get('ns')) ?? null
  if (ns) return { ns, env: null, namespaces: [ns] }
  const env = environmentOrder.find((e) => e === params.get('env') && inCloud.some((n) => n.environment === e)) ?? null
  if (env) return { ns: null, env, namespaces: inCloud.filter((n) => n.environment === env) }
  return { ns: null, env: null, namespaces: inCloud }
}

/** The query string that reproduces a choice, for links that must keep the scope. */
export function scopeQuery(choice: ScopeChoice): string {
  if (choice.ns) return `&ns=${encodeURIComponent(choice.ns.id)}`
  if (choice.env) return `&env=${choice.env}`
  return ''
}

export function groupByEnvironment(namespaces: readonly Namespace[]) {
  return environmentOrder
    .map((env) => ({ env, items: namespaces.filter((n) => n.environment === env) }))
    .filter((g) => g.items.length > 0)
}

const cloudOrder: readonly CloudProvider[] = ['azure', 'aws', 'gcp']

export const cloudColor: Record<CloudProvider, string> = { azure: '#0284c7', aws: '#f97316', gcp: '#22c55e' }

/** A cloud named in the URL, or null — anything else (a typo, a cloud that is not connected) is ignored, never trusted. */
export const asCloud = (v: string | null): CloudProvider | null => cloudOrder.find((c) => c === v) ?? null

/**
 * The one hierarchy the product uses to lay out namespaces: Cloud → Environment → Namespace. Clouds keep a fixed
 * order, environments run Production first, and rows keep the order they arrived in (callers sort worst-first).
 * Empty clouds and environments are left out, never drawn as empty headings.
 */
export function groupByCloudEnvironment<T extends { readonly provider: CloudProvider; readonly environment: EnvironmentKind }>(items: readonly T[]) {
  return cloudOrder
    .map((provider) => ({
      provider,
      environments: environmentOrder
        .map((env) => ({ env, items: items.filter((i) => i.provider === provider && i.environment === env) }))
        .filter((e) => e.items.length > 0),
    }))
    .filter((c) => c.environments.length > 0)
}
