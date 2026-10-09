import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, from apps/servicehub
import { readFileSync } from 'node:fs'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { guides } from '@/content/guides'
import HelpPanel from '@/components/help/HelpPanel'

/**
 * Found live (2026-10-09, e2e loop 1): opening Help logged "Encountered two children with the same key" on every cloud, because the Google
 * guide's bulk-replay screenshot numbered two callouts "7". The list renders by that number, so a repeat is a React warning and a confusing guide.
 */
describe('the cloud guides', () => {
  it('number the callouts of each screenshot 1, 2, 3 … with no repeats', () => {
    for (const [cloud, guide] of Object.entries(guides)) {
      for (const section of guide.sections) {
        for (const step of section.steps) {
          for (const shot of step.shots) {
            const numbers = shot.keys.map((k) => k.n)
            expect(numbers, `${cloud} · ${step.title} · ${shot.image}`).toEqual(numbers.map((_, i) => i + 1))
          }
        }
      }
    }
  })

  it('open in the Help panel without React warning about duplicate keys', () => {
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    render(<MemoryRouter><HelpPanel /></MemoryRouter>)
    const duplicate = errors.mock.calls.filter((c: unknown[]) => String(c[0]).includes('same key'))
    expect(duplicate).toHaveLength(0)
  })

  afterEach(() => vi.restoreAllMocks())
})

describe('the page shell', () => {
  it('declares an icon, so the browser does not ask for /favicon.ico and get a 404 on every first load', () => {
    const html = readFileSync('index.html', 'utf8') as string // vitest runs from apps/servicehub
    expect(html).toMatch(/<link[^>]+rel="icon"/)
  })
})
