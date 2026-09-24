import { Check, KeyRound, Bot, UserRound } from 'lucide-react'
import { Link, useLocation } from 'react-router-dom'
import { formatWhen } from '../lib/format'

/** Who did it, as far as ServiceHub actually knows. Structurally the same as the API's actor. */
export interface AttributionActor {
  readonly identity: string
  readonly kind: 'user' | 'apiKey' | 'automation' | 'system'
  readonly label: string
  readonly isSession: boolean
}

const initials = (name: string) =>
  name
    .split(/[\s@._-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]!.toUpperCase())
    .join('')

/**
 * Attribution: avatar · name · role · time · tick. It NEVER invents a person (R6) — a made-up name inside a
 * tamper-evident ledger is worse than none. Four honest renderings, decided by what an identity source supplied:
 *
 *  - a sign-in supplied a name → the name (initials avatar), with the role only when it is known;
 *  - an API key → the key's name, visibly a credential (key icon, "API key"), not a person;
 *  - ServiceHub itself (an agent) → "ServiceHub · <component>", not a person;
 *  - nothing configured → "from this browser session" and a quiet link to connect a sign-in. No initials, no name.
 *
 * `role` is passed only when it is known for THIS actor; it is never guessed.
 */
export function Attribution({
  actor,
  at,
  verb = 'Replayed',
  role,
  now = new Date(),
  compact = false,
}: {
  actor: AttributionActor
  at: string
  verb?: string
  role?: string | null
  now?: Date
  compact?: boolean
}) {
  const { search } = useLocation()
  const when = formatWhen(at, now)

  let avatar: React.ReactNode
  let name: string
  let sub: string | null = null
  let extra: React.ReactNode = null

  if (actor.kind === 'apiKey') {
    name = actor.identity.replace(/^ApiKey:/, '')
    sub = 'API key'
    avatar = <KeyRound className="h-4 w-4" aria-hidden="true" />
  } else if (actor.kind === 'system' || actor.kind === 'automation') {
    name = `ServiceHub · ${actor.identity.replace(/^(System|Rule):/, '')}`
    avatar = <Bot className="h-4 w-4" aria-hidden="true" />
  } else if (actor.isSession) {
    name = `${verb} ${actor.label}`
    avatar = <UserRound className="h-4 w-4" aria-hidden="true" />
    const next = new URLSearchParams(search)
    next.set('modal', 'settings')
    extra = (
      <Link to={`?${next.toString()}`} className="text-[var(--color-primary-700)] hover:underline">
        Connect a sign-in to record who
      </Link>
    )
  } else {
    name = actor.label
    sub = role ?? null
    avatar = <span aria-hidden="true" className="text-xs font-semibold">{initials(actor.label)}</span>
  }

  if (compact) {
    return (
      <span className="inline-flex items-center gap-1.5" data-actor-kind={actor.kind}>
        <span className="text-[var(--color-text-muted)]">{avatar}</span>
        <span>{actor.isSession ? 'This browser session' : name}</span>
        {actor.kind === 'apiKey' && <span className="rounded bg-[var(--color-surface-muted)] px-1 text-[11px] text-[var(--color-text-muted)]">API key</span>}
      </span>
    )
  }

  return (
    <section aria-label="Attribution" className="flex items-start gap-3" data-actor-kind={actor.kind}>
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--color-primary-100)] text-[var(--color-primary-700)]">
        {avatar}
      </span>
      <div className="min-w-0 flex-1 text-sm">
        {!actor.isSession && actor.kind !== 'system' && actor.kind !== 'automation' && (
          <p className="text-xs uppercase tracking-wide text-[var(--color-text-muted)]">{verb} by</p>
        )}
        <p className="font-medium">{name}</p>
        <p className="text-xs text-[var(--color-text-muted)]">
          {sub && <>{sub} · </>}
          {when}
        </p>
        {extra && <p className="mt-0.5 text-xs">{extra}</p>}
      </div>
      <Check className="mt-1 h-4 w-4 text-[var(--color-success)]" aria-label="Recorded in the ledger" />
    </section>
  )
}
