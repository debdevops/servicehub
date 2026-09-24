import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useDeadLetterTrend } from '../hooks/useDeadLetters'
import type { TrendDay } from '../lib/api/deadLetters'
import type { CloudProvider } from '../lib/api/namespaces'

const RANGES = [7, 14, 30] as const

// Two series, two hues — not a palette. Darker than the app's status tokens so both clear 3:1 against the white
// card, and separated by a legend and tooltip as well as colour (validated: contrast ✓, colour-blind ΔE 8.6).
const NEW = '#dc2626'
const RESOLVED = '#059669'
const INK_MUTED = '#6b7280'
const GRID = '#e5e7eb'

/** "Mon 22" — the day, from a UTC date, without letting the browser's timezone move it to another day. */
const dayLabel = (iso: string) => {
  const d = new Date(`${iso}T00:00:00Z`)
  return `${d.toLocaleDateString('en-GB', { weekday: 'short', timeZone: 'UTC' })} ${d.getUTCDate()}`
}

/**
 * Dead letters — new vs resolved, per day. Real data from what ServiceHub stored (unit 2.10): it is DAILY because
 * no hourly series exists and none is invented. Grouped bars ≤ 24px with a 4px rounded top, hairline grid,
 * a legend (two series), a hover tooltip, and a table view for anyone the colours do not serve.
 * Loaded lazily, so the charting library is not in the initial bundle.
 */
export default function TrendChart({ provider }: { provider: CloudProvider }) {
  const [days, setDays] = useState<(typeof RANGES)[number]>(7)
  const [table, setTable] = useState(false)
  const { data, isPending, isError, refetch } = useDeadLetterTrend(provider, days)
  const empty = data?.series.every((d) => d.new === 0 && d.resolved === 0) ?? false

  return (
    <section aria-label="Dead letters, new versus resolved" className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <header className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold">Dead letters — new vs resolved, last {days} days</h2>
        <div className="flex items-center gap-2 text-sm">
          <div role="radiogroup" aria-label="Range" className="flex overflow-hidden rounded-lg border border-[var(--color-border)]">
            {RANGES.map((r) => (
              <button
                key={r}
                type="button"
                role="radio"
                aria-checked={days === r}
                onClick={() => setDays(r)}
                className={`px-3 py-1 ${days === r ? 'bg-[var(--color-primary-600)] font-semibold text-white' : 'text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]'}`}
              >
                {r} days
              </button>
            ))}
          </div>
          <button type="button" onClick={() => setTable((t) => !t)} className="text-[var(--color-primary-700)] hover:underline">
            {table ? 'Show chart' : 'Show as table'}
          </button>
        </div>
      </header>

      {isPending && <p role="status" className="py-10 text-center text-sm text-[var(--color-text-muted)]">Reading the trend…</p>}
      {isError && (
        <p role="alert" className="py-6 text-center text-sm">
          ServiceHub couldn’t read the trend. <button type="button" onClick={() => void refetch()} className="text-[var(--color-primary-700)] hover:underline">Try again</button>
        </p>
      )}

      {data && (table ? <TrendTable series={data.series} /> : (
        <>
          <div style={{ height: 240 }} data-testid="trend-chart">
            <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 240 }}>
              <BarChart data={[...data.series]} barGap={2} margin={{ top: 8, right: 8, bottom: 0, left: -12 }}>
                <CartesianGrid vertical={false} stroke={GRID} strokeWidth={1} />
                <XAxis dataKey="date" tickFormatter={dayLabel} tickLine={false} axisLine={{ stroke: GRID }} tick={{ fill: INK_MUTED, fontSize: 12 }} interval={days === 30 ? 4 : 0} />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={{ fill: INK_MUTED, fontSize: 12 }} tickFormatter={(v: number) => v.toLocaleString()} />
                <Tooltip cursor={{ fill: 'rgba(0,0,0,0.04)' }} content={<TrendTooltip />} />
                <Legend verticalAlign="top" align="left" iconType="circle" iconSize={8} wrapperStyle={{ paddingBottom: 8, fontSize: 12, color: INK_MUTED }} />
                <Bar dataKey="new" name="New" fill={NEW} maxBarSize={24} radius={[4, 4, 0, 0]} isAnimationActive={false} />
                <Bar dataKey="resolved" name="Resolved" fill={RESOLVED} maxBarSize={24} radius={[4, 4, 0, 0]} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          {empty && <p className="mt-2 text-center text-sm text-[var(--color-text-muted)]">Nothing new and nothing resolved in the last {days} days.</p>}
        </>
      ))}
      <p className="mt-2 text-xs text-[var(--color-text-muted)]">Per day (UTC), from what ServiceHub has seen. “Resolved” means seen to leave the queue.</p>
    </section>
  )
}

function TrendTooltip({ active, payload, label }: { active?: boolean; payload?: readonly { dataKey?: string | number; value?: number }[]; label?: string | number }) {
  if (!active || !payload?.length) return null
  const value = (key: string) => payload.find((p) => p.dataKey === key)?.value ?? 0
  return (
    <div className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 text-sm shadow">
      <p className="font-medium">{dayLabel(String(label))}</p>
      <p><span aria-hidden="true" className="mr-1.5 inline-block h-2 w-2 rounded-full" style={{ background: NEW }} />{value('new')} new</p>
      <p><span aria-hidden="true" className="mr-1.5 inline-block h-2 w-2 rounded-full" style={{ background: RESOLVED }} />{value('resolved')} resolved</p>
    </div>
  )
}

function TrendTable({ series }: { series: readonly TrendDay[] }) {
  return (
    <table className="w-full text-sm">
      <caption className="sr-only">New and resolved dead letters per day</caption>
      <thead>
        <tr className="text-left text-[var(--color-text-muted)]"><th className="py-1 font-medium">Day</th><th className="py-1 text-right font-medium">New</th><th className="py-1 text-right font-medium">Resolved</th></tr>
      </thead>
      <tbody>
        {series.map((d) => (
          <tr key={d.date} className="border-t border-[var(--color-border)]">
            <td className="py-1">{dayLabel(d.date)}</td><td className="py-1 text-right tabular-nums">{d.new}</td><td className="py-1 text-right tabular-nums">{d.resolved}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
