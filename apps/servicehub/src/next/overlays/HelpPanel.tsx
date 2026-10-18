import { Search } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useOverlayTitle } from '../../components/overlays/overlayTitle'
import { nextHelpAnswers } from '../content/help'
import { cloudOf, parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'
import type { NextOverlayBodyProps } from './registry'

/**
 * Help (build-plan unit F6; design `d09-help`), at `?panel=help`: "How do I…" with a search box, over whatever you were doing. Each answer is
 * a few sentences and a link to the page that does it; `?topic=<id>` opens on that answer. Content is `next/content/help.ts`.
 * **Not yet here:** the per-cloud step-by-step guides (their screenshots are the 4.2.0 screens and are re-shot at the release, unit I3)
 * and the keyboard shortcuts.
 */
export default function HelpPanel(_props: NextOverlayBodyProps) { // eslint-disable-line @typescript-eslint/no-unused-vars
  const [params] = useSearchParams()
  const { pathname } = useLocation()
  const topic = params.get('topic')
  const [q, setQ] = useState('')
  const segment = pathname.split('/')[2]
  const cloud = cloudOf(parseScope(segment)) ?? 'all'
  useOverlayTitle('Help', 'Answers by task')
  const words = q.trim().toLowerCase().split(/\s+/).filter(Boolean)
  const shown = nextHelpAnswers.filter((a) => words.every((w) => `${a.question} ${a.answer}`.toLowerCase().includes(w)))
  // Under "all clouds" Home is the all-clouds page; every other page takes `all` as its scope.
  const href = (to: string) => (cloud === 'all' && to === 'home' ? nextHref(nextEntry('home-all')) : nextHref(nextEntry(to), { cloud }))

  return (
    <div className="space-y-4 text-sm">
      <label className="flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2">
        <Search className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" />
        <span className="sr-only">Search help</span>
        <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search help…" className="w-full bg-transparent outline-none" />
      </label>
      <section aria-label="How do I…">
        <h3 className="mb-1 text-sm font-bold">How do I…</h3>
        {shown.length === 0 ? <p className="text-[var(--color-text-muted)]">Nothing matches “{q}”. Try fewer words.</p> : (
          <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
            {shown.map((a) => (
              <li key={a.id} id={`help-${a.id}`}>
                <details open={a.id === topic || (words.length > 0 && shown.length <= 2)}>
                  <summary className="cursor-pointer px-4 py-2.5 font-semibold">{a.question}</summary>
                  <div className="px-4 pb-3 text-[var(--color-text-muted)]">
                    <p>{a.answer}</p>
                    {a.link && <Link to={href(a.link.to)} className="mt-1 inline-block font-semibold text-[var(--color-primary-700)] hover:underline">{a.link.label} ›</Link>}
                  </div>
                </details>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section aria-label="More">
        <h3 className="mb-1 text-sm font-bold">More</h3>
        <p className="flex flex-wrap gap-x-4 gap-y-1 font-semibold text-[var(--color-primary-700)]">
          <Link to={nextHref(nextEntry('coverage'))} className="hover:underline">What ServiceHub covers →</Link>
          <Link to={nextHref(nextEntry('security'), {}, pathname)} className="hover:underline">Security and data →</Link>
          <Link to={nextHref(nextEntry('autonomy'), {}, pathname)} className="hover:underline">How autonomy is earned →</Link>
          <Link to="/demo" className="hover:underline">Open the demo →</Link>
        </p>
      </section>
    </div>
  )
}
