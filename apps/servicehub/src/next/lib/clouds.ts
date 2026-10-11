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
