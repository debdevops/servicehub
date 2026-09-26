import { ChevronDown, HelpCircle, LogOut, Settings } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useMe } from '../hooks/useIdentity'

const initials = (label: string) => label.replace(/^ApiKey:/, '').split(/[\s._-]+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '·'

/**
 * The account menu (unit 6.3): who you are as the server knows you (never a made-up name, R6), your role, and the few places
 * that belong to you — Settings and Help. Sign out appears only where the sign-in method has one (App Service EasyAuth); a
 * browser session has nothing to sign out of, so no button pretends otherwise.
 */
export function AccountMenu() {
  const me = useMe().data
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const { pathname, search } = useLocation()
  useEffect(() => setOpen(false), [pathname, search])
  useEffect(() => {
    if (!open) return
    const down = (e: MouseEvent) => box.current && !box.current.contains(e.target as Node) && setOpen(false)
    const key = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', down)
    document.addEventListener('keydown', key)
    return () => { document.removeEventListener('mousedown', down); document.removeEventListener('keydown', key) }
  }, [open])
  if (!me) return null

  const href = (k: 'modal' | 'panel', v: string) => { const q = new URLSearchParams(search); q.set(k, v); return `${pathname}?${q}` }
  const who = me.actor.isSession ? 'This browser' : me.actor.label
  const role = me.governanceActive ? me.effectiveRole ?? 'No role' : 'Administrator'

  return (
    <div ref={box} className="relative">
      <button type="button" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen((o) => !o)} className="flex items-center gap-2 rounded-lg px-1.5 py-1 hover:bg-[var(--color-surface-muted)]">
        <span aria-hidden="true" className="flex h-8 w-8 items-center justify-center rounded-full bg-[var(--color-primary-700)] text-xs font-bold text-white">{me.actor.isSession ? '·' : initials(me.actor.label)}</span>
        <span className="hidden text-left leading-tight sm:block">
          <span className="block text-[13px] font-semibold text-[var(--color-text)]">{who}</span>
          <span className="block text-[11px] text-[var(--color-text-muted)]">{role}</span>
        </span>
        <ChevronDown className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
      </button>
      {open && (
        <div role="menu" className="absolute right-0 top-11 z-40 w-64 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] shadow-xl">
          <div className="border-b border-[var(--color-border)] px-4 py-3 text-sm">
            <p className="font-semibold">{who}</p>
            <p className="text-xs text-[var(--color-text-muted)]">{role} · {me.actor.isSession ? 'no sign-in on this server' : `signed in via ${me.authMethod}`}</p>
          </div>
          <Link role="menuitem" to={href('modal', 'settings')} className="flex items-center gap-2 px-4 py-2.5 text-sm hover:bg-[var(--color-surface-muted)]"><Settings className="h-4 w-4" aria-hidden="true" /> Settings</Link>
          <Link role="menuitem" to={href('panel', 'help')} className="flex items-center gap-2 px-4 py-2.5 text-sm hover:bg-[var(--color-surface-muted)]"><HelpCircle className="h-4 w-4" aria-hidden="true" /> Help and shortcuts</Link>
          {me.authMethod === 'EasyAuth' && (
            <a role="menuitem" href="/.auth/logout" className="flex items-center gap-2 border-t border-[var(--color-border)] px-4 py-2.5 text-sm hover:bg-[var(--color-surface-muted)]"><LogOut className="h-4 w-4" aria-hidden="true" /> Sign out</a>
          )}
        </div>
      )}
    </div>
  )
}
