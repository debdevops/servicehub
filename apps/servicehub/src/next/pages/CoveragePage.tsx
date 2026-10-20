import { Pager } from '../../components/ui/Pager'
import { usePagedList } from '../../hooks/usePagedList'
import { useCoverage } from '../../hooks/useCoverage'
import type { CloudKind, CoverageRow, SupportStatus } from '../../lib/api/coverage'
import { toProblem } from '../../lib/api/client'
import { Th } from '../components/Tip'
import { Tip } from '../components/Tip'

const clouds: readonly CloudKind[] = ['azure', 'aws', 'gcp']

/** How a status is tinted. The words come from the server (`statusWords`), so the screen and the README say the same thing. */
const statusTone: Readonly<Record<SupportStatus, string>> = {
  supported: 'bg-[var(--color-success-light)] text-green-800',
  beingValidated: 'bg-[var(--color-warning-light)] text-amber-800',
  onRequest: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]',
  notSupported: 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]',
}

const tiles: readonly { readonly status: SupportStatus; readonly label: string; readonly hint: string }[] = [
  { status: 'supported', label: 'Supported', hint: 'Seen working on a real cloud' },
  { status: 'beingValidated', label: 'Being validated', hint: 'In 5.0.0, no recorded live run yet' },
  { status: 'onRequest', label: 'On request', hint: 'Built only if someone asks' },
  { status: 'notSupported', label: 'Not supported', hint: 'With the reason' },
]

function CloudTable({ name, rows }: { name: string; rows: readonly CoverageRow[] }) {
  const { items, pager } = usePagedList(rows)
  return (
    <section aria-label={name} className="mb-4 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <h2 className="mb-3 text-base font-semibold text-[var(--color-text)]">{name}<Tip label="section:Cloud coverage" /></h2>
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-y border-[var(--color-border)] bg-[var(--color-surface-muted)] text-xs text-[var(--color-text-muted)]">
            <Th context="coverage">Component</Th>
            <Th context="coverage">Where it gets stuck</Th>
            <Th context="coverage">How ServiceHub finds it</Th>
            <Th context="coverage">How it moves on</Th>
            <Th context="coverage">Status</Th>
            <Th context="coverage">Note</Th>
          </tr>
        </thead>
        <tbody>
          {items.map((row) => (
            <tr key={row.id} className="border-b border-[var(--color-border)] align-top text-[var(--color-text)]">
              <th scope="row" className="px-2 py-2 font-semibold">{row.name}</th>
              <td className="px-2 py-2">{row.stuckPlace}</td>
              <td className="px-2 py-2">{row.detectionWords}</td>
              <td className="px-2 py-2">{row.movesOnBy}</td>
              <td className="px-2 py-2">
                <span className={`inline-block whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-semibold ${statusTone[row.status]}`}>{row.statusWords}</span>
              </td>
              <td className="px-2 py-2 text-[var(--color-text-muted)]">{row.note ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pager {...pager} label={name} />
    </section>
  )
}

/**
 * What ServiceHub covers (design `p15-what-servicehub-covers`, build-plan unit A3) — the first screen of the 5.0.0 layout that is
 * real end to end: `ComponentCatalog` (Core) → `GET /fleet/coverage` → `useCoverage` → here, and the demo answers from a
 * test-guarded copy of the same list. Every other screen follows this path.
 */
export default function CoveragePage() {
  const { data, isPending, isError, error } = useCoverage()

  if (isPending) return <p role="status" className="px-6 py-6 text-sm text-[var(--color-text-muted)]">Loading what ServiceHub covers…</p>
  if (isError) {
    return (
      <p role="alert" className="m-6 rounded-lg border border-[var(--color-error)] bg-[var(--color-error-light)] px-4 py-3 text-sm text-[var(--color-text)]">
        ServiceHub could not load the coverage list. {toProblem(error).message}
      </p>
    )
  }

  const rows = data.rows
  return (
    <div className="px-6 py-5">
      <h1 className="text-2xl font-semibold text-[var(--color-text)]">What ServiceHub covers<Tip label="page:What ServiceHub covers" /></h1>
      <p className="mt-1 text-sm text-[var(--color-text-muted)]">
        Every component, on every cloud: where failed messages end up, how ServiceHub finds them, and whether it can move them on.
      </p>

      <dl className="my-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tiles.map((tile) => (
          <div key={tile.status} className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] px-4 py-3">
            <dt className="flex items-center text-sm font-semibold text-[var(--color-text)]">{tile.label}<Tip label={tile.label} context="coverage" /></dt>
            <dd className="text-2xl font-bold tabular-nums text-[var(--color-text)]">{rows.filter((r) => r.status === tile.status).length}</dd>
            <dd className="text-xs text-[var(--color-text-muted)]">{tile.hint}</dd>
          </div>
        ))}
      </dl>

      <p className="mb-4 rounded-lg border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-3 text-sm text-[var(--color-text)]">
        <strong>ServiceHub recovers messages only where a failed copy is kept and can be put back safely.</strong> Many services keep no copy, or
        are not messaging at all. Those are listed here so nobody has to guess, with where to look instead.
      </p>

      {clouds.map((cloud) => {
        const mine = rows.filter((r) => r.cloud === cloud)
        return mine.length > 0 ? <CloudTable key={cloud} name={mine[0].cloudName} rows={mine} /> : null
      })}

      <p className="rounded-lg border border-[var(--color-primary-200)] bg-[var(--color-primary-50)] px-4 py-3 text-sm text-[var(--color-text)]">
        <strong>Supported means seen, not assumed.</strong> A row is Supported only after a recorded run on a real cloud. This page and the
        README table are built from the same list, so they cannot disagree.
      </p>
    </div>
  )
}
