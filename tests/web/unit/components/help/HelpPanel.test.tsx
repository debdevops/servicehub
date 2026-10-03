import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, from apps/servicehub
import { existsSync, readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { helpAnswers, pageHelpStep, screenHelpStep } from '@/content/help'
import { guides } from '@/content/guides'
import HelpPanel from '@/components/help/HelpPanel'
import { expectNoAxeViolations } from '@tests/support/axe'

describe('HelpPanel', () => {
  it('has no accessibility violations (6.6)', async () => {
    const { container } = render(<MemoryRouter><HelpPanel /></MemoryRouter>)
    await expectNoAxeViolations(container, { isolatedComponent: true })
  }, 120000) // the panel holds every cloud's guide, so axe has a lot of (folded) markup to walk

  it('lists task answers and the keyboard shortcuts, and filters as you type', () => {
    render(<MemoryRouter><HelpPanel /></MemoryRouter>)
    expect(screen.getByText('Replay a dead-lettered message')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Keyboard' })).toBeInTheDocument()
    fireEvent.change(screen.getByPlaceholderText('How do I…'), { target: { value: 'many' } })
    expect(screen.getByText('Replay many at once')).toBeInTheDocument()
    expect(screen.queryByText('Replay a dead-lettered message')).not.toBeInTheDocument()
  })

  it('?topic= opens straight on that answer, expanded (Home’s "Why?" links, 2026-09-27)', () => {
    render(<MemoryRouter initialEntries={['/?panel=help&topic=verification-required']}><HelpPanel /></MemoryRouter>)
    const details = screen.getByText('Why does AWS or Google Cloud say "Verification required"?').closest('details')
    expect(details).toHaveAttribute('open')
  })

  it('shows a step-by-step guide per cloud, and ?topic= opens the one asked for (7.6)', () => {
    for (const cloud of Object.keys(guides)) {
      const { unmount } = render(<MemoryRouter initialEntries={[`/?panel=help&topic=guide-${cloud}`]}><HelpPanel /></MemoryRouter>)
      expect(screen.getByText(new RegExp(`Every screen, with real screenshots — ${guides[cloud].title}`)).closest('details')).toHaveAttribute('open')
      for (const other of Object.keys(guides).filter((c) => c !== cloud)) {
        expect(screen.getByText(new RegExp(`Every screen, with real screenshots — ${guides[other].title}`)).closest('details')).not.toHaveAttribute('open')
      }
      unmount()
    }
  })

  it('every guide screenshot exists on disk (7.6)', () => {
    for (const [cloud, guide] of Object.entries(guides)) {
      const shots = guide.sections.flatMap((s) => s.steps.flatMap((st) => st.shots))
      expect(shots.length, cloud).toBeGreaterThan(20)
      for (const shot of shots) {
        expect(existsSync(`public/${shot.image.slice(1)}`), shot.image).toBe(true)
      }
    }
  })

  it('every in-app Help link points at a topic that exists (7.6)', () => {
    const ids = new Set([...helpAnswers.map((a) => a.id), ...Object.keys(guides).map((c) => `guide-${c}`)])
    for (const a of helpAnswers) {
      const m = a.link?.href.match(/topic=([\w-]+)/)
      if (m) expect(ids.has(m[1]), `${a.id} → ${m[1]}`).toBe(true)
    }
  })

  it('?step= opens that cloud’s guide on that step, and every screen’s (?) names a step each guide has (7.6)', () => {
    render(<MemoryRouter initialEntries={['/?panel=help&topic=guide-aws&step=4.4']}><HelpPanel /></MemoryRouter>)
    const open = document.getElementById('guide-step-aws-4.4')!.querySelector('details')!
    expect(open).toHaveAttribute('open')
    expect(document.getElementById('guide-step-aws-4.3')!.querySelector('details')!).not.toHaveAttribute('open')
    expect(document.getElementById('guide-step-azure-4.4')!.querySelector('details')!).not.toHaveAttribute('open')
    for (const [cloud, guide] of Object.entries(guides)) {
      const steps = guide.sections.flatMap((sec) => sec.steps.map((st) => st.title.split(' ')[0]))
      for (const [screenId, step] of Object.entries({ ...screenHelpStep, ...pageHelpStep })) expect(steps, `${cloud}: ${screenId}`).toContain(step)
    }
  })

  it.each(Object.keys(guides))('every control on every %s screenshot has a numbered callout (7.6, from scripts/docs-shots)', (cloud) => {
    const dir = `../../docs/screenshots/${cloud}`
    const gaps = JSON.parse(readFileSync(`${dir}/uncovered.json`, 'utf8')) as Record<string, string[]>
    const keys = JSON.parse(readFileSync(`${dir}/keys.json`, 'utf8')) as Record<string, unknown[]>
    for (const [shot, list] of Object.entries(gaps)) expect(list, `${shot} has controls with no callout`).toEqual([])
    // Below the fold: a control out of view in one shot must be covered by a callout in another (scripts/docs-shots finishRun).
    const unreached = `${dir}/unreached.json`
    if (existsSync(unreached)) expect(JSON.parse(readFileSync(unreached, 'utf8')) as string[], 'controls never in a callout (below the fold)').toEqual([])
    for (const shot of guides[cloud].sections.flatMap((sec) => sec.steps.flatMap((st) => st.shots))) {
      const name = shot.image.split('/').pop() as string
      if (!/^(portal-|18-(search|bell|user))/.test(name) && !name.startsWith('08-message-details-')) expect(keys[name]?.length ?? 0, name).toBeGreaterThan(0)
    }
  })
  // 7.6: Simple's words. Advanced (step 7.3) is where this vocabulary lives, so that one step is exempt.
  it.each(Object.keys(guides))('the %s guide uses no 4.0.0 vocabulary outside the Advanced step (7.6)', (cloud) => {
    const banned = /\b(signatures?|pillars?|dispositions?|autonomy levels?|attestations?|ledger|L[45])\b/i
    // Part 1 is the cloud's own console, so it keeps the cloud's words. "grant" is left out of the list: as a verb it is ordinary English.
    for (const step of guides[cloud].sections.filter((sec) => !sec.part.startsWith('Part 1')).flatMap((sec) => sec.steps)) {
      if (step.title.startsWith('7.3')) continue
      const texts = [step.text, ...step.shots.flatMap((shot) => shot.keys.map((k) => k.text))]
      // Proper names the build itself shows are not ours to reword: Advanced's page names,.
      for (const raw of texts) {
        const t = raw.replace(/Recovery Ledger|Advanced is read-only pages \([^)]*\)/g, '')
        expect(t.match(banned)?.[0] ?? null, `${cloud} ${step.title}: "${raw}"`).toBeNull()
      }
    }
  })
})
