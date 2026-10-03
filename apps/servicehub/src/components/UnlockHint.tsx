import { Lock } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { hintFor } from '../lib/unlockHints'

/**
 * An unlock hint (unit 6.2): amber, with a padlock — what holds this back, what would change it, and where to go. The highest
 * trust-per-line element in the product: it turns a dead end into a path. `record` is the verified track record, when known.
 */
export function UnlockHint({ code, record, approvable = false }: { code: string | null | undefined; record?: { fixed: number; of: number }; approvable?: boolean }) {
  const hint = hintFor(code)
  const { pathname, search } = useLocation()
  const href = hint.go?.href.startsWith('?') ? `${pathname}?${new URLSearchParams([...new URLSearchParams(search), ...new URLSearchParams(hint.go.href.slice(1))])}` : hint.go?.href
  return (
    <div role="note" className="flex items-start gap-2.5 rounded-xl border border-[#fde68a] bg-[#fffbeb] px-3.5 py-3 text-[13px] text-[#78350f]">
      <Lock className="mt-0.5 h-4 w-4 shrink-0 text-[#d97706]" aria-hidden="true" />
      <p>
        {hint.earnable && <b>I could handle this one myself. </b>}
        {hint.earnable && record && record.of > 0 && <>I’ve fixed this failure {record.fixed} {record.fixed === 1 ? 'time' : 'times'} out of {record.of}. </>}
        {hint.why} {hint.takes}
        {approvable && ' A person with approval rights can decide this one now.'}
        {hint.go && href && <> <Link to={href} className="whitespace-nowrap font-semibold text-[var(--color-primary-700)] hover:underline">{hint.go.label} →</Link></>}
        {code && <span className="ml-1 font-mono text-[11px] opacity-70">{code}</span>}
      </p>
    </div>
  )
}
