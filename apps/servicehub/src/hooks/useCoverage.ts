import { useQuery } from '@tanstack/react-query'
import { fetchCoverage, type CloudKind, type CoverageRow } from '../lib/api/coverage'
import type { EnabledComponent } from '../lib/api/componentEnablement'

export const coverageKeys = { all: ['coverage'] as const }

/** The one place "what does ServiceHub cover?" is asked. The menu and the coverage page both read it. It changes only with a release. */
export function useCoverage() {
  return useQuery({ queryKey: coverageKeys.all, queryFn: fetchCoverage, staleTime: Infinity })
}

/**
 * The components the menu lists for a cloud: one per component, in catalogue order. A component that is read on demand (a run history)
 * is listed only where someone ticked it in Add a component; a message store is listed for every connected cloud, as before.
 */
export function sidebarComponents(rows: readonly CoverageRow[], cloud: CloudKind | null, enabled: readonly EnabledComponent[] = []): readonly CoverageRow[] {
  const seen = new Set<string>()
  return rows.filter(
    (r) =>
      r.inSidebar &&
      (cloud === null || r.cloud === cloud) &&
      (r.detection !== 'runs' || enabled.some((e) => e.cloud === r.cloud && e.componentKey === r.componentKey)) &&
      !seen.has(r.componentKey) &&
      seen.add(r.componentKey),
  )
}

/** Components that exist in the catalogue but are built only if someone asks. Listed, never linked. */
export function onRequestComponents(rows: readonly CoverageRow[], cloud: CloudKind | null): readonly CoverageRow[] {
  const seen = new Set<string>()
  return rows.filter((r) => r.status === 'onRequest' && (cloud === null || r.cloud === cloud) && !seen.has(r.componentKey) && seen.add(r.componentKey))
}
