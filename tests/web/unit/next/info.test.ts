import { describe, expect, it } from 'vitest'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node
import { readdirSync, readFileSync, statSync } from 'node:fs'
// @ts-expect-error -- same
import { join } from 'node:path'
import { helpFor, labels } from '@/next/content/info'
import { abilities } from '@/next/lib/roles'

declare const __dirname: string
const SRC = join(__dirname, '../../../../apps/servicehub/src/next')
const files = (dir: string): string[] => (readdirSync(dir) as string[]).flatMap((f) => (statSync(join(dir, f)).isDirectory() ? files(join(dir, f)) : f.endsWith('.tsx') || f.endsWith('.ts') ? [join(dir, f)] : []))
const sources = files(SRC).map((f) => ({ f: f.replace(SRC, 'next'), text: readFileSync(f, 'utf8') as string }))

/** The label (and its context) of every literal `<Th>`, `<Th label>`, `<Info label>` and `<Labelled label>` in the shell. */
function used(): { f: string; label: string; context?: string }[] {
  const out: { f: string; label: string; context?: string }[] = []
  for (const { f, text } of sources) {
    for (const m of text.matchAll(/<Th(?:\s+context="([^"]+)")?>([^<{}]+)<\/Th>/g)) out.push({ f, label: m[2], context: m[1] })
    for (const m of text.matchAll(/<(?:Tip|Labelled|Th)\b([^>]*?)\blabel="([^"]+)"([^>]*)>/g)) {
      const context = /context="([^"]+)"/.exec(m[1] + m[3])?.[1]
      out.push({ f, label: m[2], context })
    }
  }
  return out
}

describe('the (i) on everything', () => {
  it('has no raw column header: every <th> in the shell goes through Th (with an (i)) or ThHidden (no visible label)', () => {
    for (const { f, text } of sources) {
      if (f.endsWith('components/Tip.tsx')) continue
      const raw = [...text.matchAll(/<th\b[^>]*>/g)].filter((m) => !m[0].includes('scope="row"'))
      expect(raw.map((m) => m[0]), `${f} draws a column header without an (i): use <Th>`).toEqual([])
    }
  })

  it('has an (i) on every heading in the shell (a heading that is only for screen readers, or the placeholder, is exempt)', () => {
    const exempt = ['next/pages/NextPlaceholderPage.tsx', 'next/components/Tip.tsx']
    const bare: string[] = []
    for (const { f, text } of sources) {
      if (exempt.includes(f)) continue
      for (const m of text.matchAll(/<(h[123])\b([^>]*)>((?:(?!<\/\1>)[\s\S])*?)<\/\1>/g)) {
        if (/sr-only/.test(m[2])) continue
        if (!/<Tip\b|<Labelled\b/.test(m[3])) bare.push(`${f}: ${m[3].replace(/\s+/g, ' ').slice(0, 70)}`)
      }
    }
    expect(bare).toEqual([])
  })

  it('has an entry for every label a page draws', () => {
    const missing = used().filter((u) => !helpFor(u.label, u.context)).map((u) => `${u.f}: “${u.label}”${u.context ? ` (${u.context})` : ''}`)
    expect(missing).toEqual([])
  })

  it('explains every column of the role table (its labels are built, not typed)', () => {
    for (const a of abilities) expect(helpFor(a.label), a.label).toBeDefined()
  })

  it('writes each entry as an explanation: a title, and at least a sentence of text', () => {
    for (const [key, help] of Object.entries(labels)) {
      expect(help.title.trim().length, key).toBeGreaterThan(1)
      expect(help.text.trim().length, key).toBeGreaterThanOrEqual(25)
      expect(help.text.trim(), key).toMatch(/[.:?”)]$/)
    }
  })
})
