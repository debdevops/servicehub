// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, from apps/servicehub
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/**
 * Found live (2026-10-09, e2e loop 1/2): with real held replays the Approve window's message-body block scrolled, and axe reported
 * scrollable-region-focusable (serious) — a keyboard user could not scroll it. Demo data never made the block overflow, so no existing test saw it.
 * A scrolling <pre> must be reachable by keyboard (tabIndex 0) and say what it is.
 */
function files(dir: string): string[] {
  return (readdirSync(dir) as string[]).flatMap((name) => {
    const path = `${dir}/${name}`
    return (statSync(path).isDirectory() ? files(path) : [path]).filter((p: string) => p.endsWith('.tsx'))
  })
}

describe('scrolling text blocks', () => {
  it('can all be focused from the keyboard and have an accessible name', () => {
    const offenders: string[] = []
    for (const file of files('src')) {
      const text = readFileSync(file, 'utf8') as string
      for (const match of text.matchAll(/<pre\b[^>]*>/g)) {
        const tag = match[0]
        if (/overflow-(auto|scroll|y-auto|x-auto)/.test(tag) && !(/tabIndex=\{0\}/.test(tag) && /aria-label=/.test(tag))) {
          offenders.push(`${file}: ${tag.slice(0, 90)}`)
        }
      }
    }
    expect(offenders).toEqual([])
  })
})
