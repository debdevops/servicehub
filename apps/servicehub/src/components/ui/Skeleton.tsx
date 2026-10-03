/**
 * Loading that draws the shape of what is coming (unit 6.1) — never a spinner over a blank page, never a 0.
 * `label` is what a screen reader hears; it keeps the old "Reading …" sentence so the status stays announced.
 */
export function Skeleton({ label, rows = 4, variant = 'rows', className = '' }: {
  label: string
  rows?: number
  /** `rows` = table/list lines · `block` = one card-sized slab · `line` = a single line of text */
  variant?: 'rows' | 'block' | 'line'
  className?: string
}) {
  const bar = 'animate-pulse rounded bg-[var(--color-border)]'
  return (
    <div role="status" className={className}>
      <span className="sr-only">{label}</span>
      <div aria-hidden="true">
        {variant === 'block' && <div className={`${bar} h-24 w-full`} />}
        {variant === 'line' && <div className={`${bar} h-3.5 w-1/2`} />}
        {variant === 'rows' && (
          <div className="space-y-3">
            {Array.from({ length: rows }, (_, i) => (
              <div key={i} className="flex items-center gap-4">
                <div className={`${bar} h-3.5 w-20`} />
                <div className={`${bar} h-3.5 w-32`} />
                <div className={`${bar} h-3.5 flex-1`} />
                <div className={`${bar} h-3.5 w-14`} />
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
