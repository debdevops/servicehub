import { Search } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { helpAnswers, shortcuts } from '../../content/help'

/**
 * Help (unit 6.4, `?panel=help`): "How do I…" with a search box, the everyday and setting-up questions, and the keyboard
 * shortcuts — over whatever you were doing. Each answer is a few sentences and a link into the product; never a tour.
 */
export default function HelpPanel() {
  const [q, setQ] = useState('')
  const { pathname, search } = useLocation()
  const resolve = (href: string) => {
    if (!href.startsWith('?')) return href
    const next = new URLSearchParams(search)
    next.delete('panel')
    new URLSearchParams(href.slice(1)).forEach((v, k) => next.set(k, v))
    return `${pathname}?${next}`
  }
  const words = q.trim().toLowerCase().split(/\s+/).filter(Boolean)
  const matching = helpAnswers.filter((a) => words.every((w) => `${a.question} ${a.answer}`.toLowerCase().includes(w)))

  return (
    <div className="space-y-5 text-sm">
      <label className="flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2">
        <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
        <span className="sr-only">How do I…</span>
        <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder="How do I…" className="w-full bg-transparent outline-none" />
      </label>

      {(['Everyday', 'Setting up'] as const).map((group) => {
        const items = matching.filter((a) => a.group === group)
        if (items.length === 0) return null
        return (
          <section key={group} aria-label={group}>
            <h3 className="mb-1 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">{group}</h3>
            <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
              {items.map((a) => (
                <li key={a.id}>
                  <details open={words.length > 0 && items.length <= 2}>
                    <summary className="cursor-pointer px-4 py-2.5 font-semibold">{a.question}</summary>
                    <div className="px-4 pb-3 text-[var(--color-text-muted)]">
                      <p>{a.answer}</p>
                      {a.link && <Link to={resolve(a.link.href)} className="mt-1 inline-block font-semibold text-[var(--color-primary-700)] hover:underline">{a.link.label} ›</Link>}
                    </div>
                  </details>
                </li>
              ))}
            </ul>
          </section>
        )
      })}
      {matching.length === 0 && <p className="text-[var(--color-text-muted)]">Nothing matches “{q}”. Try fewer words.</p>}

      <section aria-label="Keyboard">
        <h3 className="mb-1 text-[10.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Keyboard</h3>
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5">
          {shortcuts.map((s) => (
            <div key={s.keys} className="contents">
              <dt><kbd className="rounded border border-[var(--color-border)] bg-[var(--color-surface-muted)] px-1.5 py-0.5 font-mono text-xs">{s.keys}</kbd></dt>
              <dd className="text-[var(--color-text-muted)]">{s.does}</dd>
            </div>
          ))}
        </dl>
      </section>
    </div>
  )
}
