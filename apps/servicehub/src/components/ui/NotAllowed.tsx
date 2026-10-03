import { Lock } from 'lucide-react'

/** The reason an action is disabled, shown in place — what is missing and who can grant it (unit 5.7). Never a hidden button. */
export function NotAllowed({ reason, tone = 'light' }: { reason: string | null; tone?: 'light' | 'dark' }) {
  if (!reason) return null
  return (
    <p role="note" className={`mt-1.5 flex items-start gap-1.5 text-xs ${tone === 'dark' ? 'text-[#fcd34d]' : 'text-[var(--color-text-muted)]'}`}>
      <Lock className="mt-0.5 h-3 w-3 shrink-0" aria-hidden="true" /> {reason}
    </p>
  )
}
