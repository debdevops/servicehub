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
 * Each cloud's own brand colours, so a page reads as that cloud's at a glance: Azure blue, AWS squid ink with its orange,
 * Google Cloud blue. Colour only: nothing is decided from it (R4). `all` keeps ServiceHub's own.
 */
export const cloudBrand: Readonly<Record<CloudKind, { bg: string; accent: string }>> = {
  azure: { bg: '#0078d4', accent: '#0078d4' },
  aws: { bg: '#232f3e', accent: '#ff9900' },
  gcp: { bg: '#1a73e8', accent: '#1a73e8' },
}

/** The header's own colour per cloud (the three Home designs differ on purpose). Azure and Google use a darker shade of the brand blue so the white header text keeps 4.5:1 contrast. */
export const cloudHeaderClass: Readonly<Record<CloudScope, string>> = {
  all: 'bg-[var(--color-primary-900)]',
  azure: 'bg-[#005a9e]',
  aws: 'bg-[#232f3e] border-b-4 border-[#ff9900]',
  gcp: 'bg-[#1557b0]',
}
