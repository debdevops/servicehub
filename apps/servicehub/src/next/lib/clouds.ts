import type { CloudKind } from '../../lib/api/coverage'

/** The scope a page is looking at: one cloud, or every connected cloud. It lives in the URL (`/next/azure/…`), nowhere else. */
export type CloudScope = CloudKind | 'all'

export const cloudScopes: readonly CloudScope[] = ['all', 'azure', 'aws', 'gcp']

/** Names for display only. What a cloud can do is never decided from its name (R4). */
export const cloudLabel: Readonly<Record<CloudScope, string>> = {
  all: 'All clouds',
  azure: 'Azure',
  aws: 'AWS',
  gcp: 'Google Cloud',
}

/** The scope a path segment names, or `all` for anything unrecognised — an unknown segment is never guessed at. */
export function parseScope(segment: string | undefined): CloudScope {
  return (cloudScopes as readonly string[]).includes(segment ?? '') ? (segment as CloudScope) : 'all'
}

/** The cloud a scope narrows to, or null for every cloud. */
export function cloudOf(scope: CloudScope): CloudKind | null {
  return scope === 'all' ? null : scope
}

/**
 * The header's own colour per cloud (the three Home designs differ on purpose). Colour only: nothing is decided from it (R4).
 * `all` and the non-cloud pages keep ServiceHub's own.
 */
export const cloudHeaderClass: Readonly<Record<CloudScope, string>> = {
  all: 'bg-[var(--color-primary-900)]',
  azure: 'bg-[#0b2f63]',
  aws: 'bg-[#4a2310]',
  gcp: 'bg-[#0f4a2b]',
}
