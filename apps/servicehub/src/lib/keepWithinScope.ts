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

/**
 * `placeholderData` that keeps the previous page on screen while the next one loads — but only within the same cloud.
 *
 * Paging and filtering should never flash empty, so the old rows stay until the new ones arrive. Switching CLOUD is different:
 * for those frames the screen would show one cloud's numbers under another cloud's name, and a number from the wrong cloud is
 * indistinguishable from a real one (unit 3.7). Across clouds this returns nothing, so the screen shows its loading state.
 */
export function keepWithinScope<T>(provider: string | undefined) {
  return (previous: T | undefined, previousQuery?: { queryKey: QueryKey }): T | undefined =>
    previousQuery && providerOf(previousQuery.queryKey) === provider ? previous : undefined
}
