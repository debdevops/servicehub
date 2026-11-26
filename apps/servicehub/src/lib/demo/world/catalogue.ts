import type { CoverageResponse, CoverageRow } from '../../api/coverage'
import type { CloudProvider } from '../../api/namespaces'
// The demo's copy of the catalogue; `CoverageEndpointTests` (backend) keeps it equal to the real one.
import raw from './coverage.json?raw'

export const catalogue: readonly CoverageRow[] = (JSON.parse(raw) as CoverageResponse).rows

/** The catalogue row an entity belongs to — the demo's copy of `ComponentCatalog.ForEntity`. */
export const rowFor = (provider: CloudProvider, kind: string): CoverageRow | undefined =>
  catalogue.find((r) => r.cloud === provider && r.detection === 'store' && r.status === 'supported' && r.entityKinds.includes(kind))

/**
 * The family of the stop reasons the demo can raise — the demo's copy of `ReasonFamilies` (backend), for the three codes the demo's
 * Agent produces. A code not listed has no family, shown as no label, exactly as the real API does.
 */
const families: Readonly<Record<string, string>> = {
  AUTONOMY_GRANT_INSUFFICIENT: 'policyBlocked',
  PROVIDER_CANNOT_VERIFY_ABSENCE: 'unverified',
  SIGNATURE_RECENT_RESULTS_MIXED: 'rejected',
}
export const familyOf = (code: string): string | null => families[code] ?? null
