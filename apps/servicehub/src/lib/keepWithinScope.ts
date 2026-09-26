import type { QueryKey } from '@tanstack/react-query'

/** Finds the cloud a query key is about: a `provider` field anywhere in it, or a bare cloud name. */
function providerOf(key: QueryKey): string | undefined {
  for (const part of key) {
    if (part === 'azure' || part === 'aws' || part === 'gcp') return part
    if (part && typeof part === 'object' && 'provider' in part && typeof (part as { provider?: unknown }).provider === 'string') {
      return (part as { provider: string }).provider
    }
  }
  return undefined
}

/** Which namespace or environment a query key narrows to: a `namespaceId` / `environment` field in one of its parts. */
function narrowOf(key: QueryKey): string {
  for (const part of key) {
    if (part && typeof part === 'object' && ('namespaceId' in part || 'environment' in part)) {
      const p = part as { namespaceId?: unknown; environment?: unknown }
      return `${typeof p.namespaceId === 'string' ? p.namespaceId : ''}|${typeof p.environment === 'string' ? p.environment : ''}`
    }
  }
  return '|'
}

/**
 * `placeholderData` that keeps the previous page on screen while the next one loads — but only within the same cloud.
 *
 * Paging and filtering should never flash empty, so the old rows stay until the new ones arrive. Switching CLOUD is different:
 * for those frames the screen would show one cloud's numbers under another cloud's name, and a number from the wrong cloud is
 * indistinguishable from a real one (unit 3.7). Across clouds this returns nothing, so the screen shows its loading state.
 * The same holds for a narrower scope: pass `narrow` and a change of namespace or environment is treated like a change of cloud.
 */
export function keepWithinScope<T>(provider: string | undefined, narrow?: { namespaceId?: string; environment?: string }) {
  const wanted = `${narrow?.namespaceId ?? ''}|${narrow?.environment ?? ''}`
  return (previous: T | undefined, previousQuery?: { queryKey: QueryKey }): T | undefined =>
    previousQuery && providerOf(previousQuery.queryKey) === provider && narrowOf(previousQuery.queryKey) === wanted ? previous : undefined
}
