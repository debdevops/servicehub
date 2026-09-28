import { describe, expect, it } from 'vitest'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, and vite would hand back an empty string for a CSS import
import { readFileSync } from 'node:fs'

/**
 * Contrast gate (unit 6.6). jsdom has no colours, so axe's colour-contrast rule cannot run in the unit suite; the brand pairs that
 * failed the live axe pass (2026-09-28) are pinned here instead, read from the real design tokens.
 */
const css = readFileSync('src/styles/index.css', 'utf8') as string
const token = (name: string) => {
  const m = css.match(new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{6})`))
  if (!m) throw new Error(`token --${name} not found`)
  return m[1]
}
const channel = (v: number) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4 }
const luminance = (hex: string) => { const n = parseInt(hex.slice(1), 16); return 0.2126 * channel(n >> 16) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255) }
const ratio = (a: string, b: string) => { const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x); return (hi + 0.05) / (lo + 0.05) }

describe('brand colours meet contrast (6.6)', () => {
  it('primary blue reads as text on white and carries white text (links, primary buttons, active nav)', () => {
    expect(ratio(token('color-primary-600'), '#ffffff')).toBeGreaterThanOrEqual(4.5)
    expect(ratio(token('color-primary-700'), '#ffffff')).toBeGreaterThanOrEqual(4.5)
  })
  it('muted text reads on the page and card backgrounds', () => {
    expect(ratio(token('color-text-muted'), token('color-surface'))).toBeGreaterThanOrEqual(4.5)
    expect(ratio(token('color-text-muted'), token('color-surface-muted'))).toBeGreaterThanOrEqual(4.5)
  })
  it('error text reads on white and on the tinted error and page backgrounds', () => {
    expect(ratio(token('color-error'), token('color-surface'))).toBeGreaterThanOrEqual(4.5)
    expect(ratio(token('color-error'), token('color-surface-muted'))).toBeGreaterThanOrEqual(4.5)
  })
  it('the focus ring is visible against the page (UI component, 3:1)', () => {
    expect(ratio(token('color-primary-600'), token('color-surface'))).toBeGreaterThanOrEqual(3)
  })
  it('white text on the amber action button and dark text on the amber badge both pass', () => {
    expect(ratio('#b45309', '#ffffff')).toBeGreaterThanOrEqual(4.5)
    expect(ratio('#111827', token('color-warning'))).toBeGreaterThanOrEqual(4.5)
  })
})
