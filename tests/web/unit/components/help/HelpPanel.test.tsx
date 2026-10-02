import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { helpAnswers, pageHelpStep, screenHelpStep } from '@/content/help'
import { azureGuide } from '@/content/guides/azure.generated'
import HelpPanel from '@/components/help/HelpPanel'
import { expectNoAxeViolations } from '@tests/support/axe'

describe('HelpPanel', () => {
  it('has no accessibility violations (6.6)', async () => {
    const { container } = render(<MemoryRouter><HelpPanel /></MemoryRouter>)
    await expectNoAxeViolations(container)
  })

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

  it('shows the step-by-step guide, and ?topic=guide-azure opens it (7.6)', () => {
    render(<MemoryRouter initialEntries={['/?panel=help&topic=guide-azure']}><HelpPanel /></MemoryRouter>)
    expect(screen.getByText('Every screen, with real screenshots').closest('details')).toHaveAttribute('open')
  })

  it('every guide screenshot exists on disk and carries a numbered key (7.6)', () => {
    const shots = azureGuide.sections.flatMap((s) => s.steps.flatMap((st) => st.shots))
    expect(shots.length).toBeGreaterThan(20)
    for (const shot of shots) {
      expect(existsSync(resolve(__dirname, '../../../../../apps/servicehub/public', shot.image.slice(1))), shot.image).toBe(true)
    }
  })

  it('every in-app Help link points at a topic that exists (7.6)', () => {
    const ids = new Set([...helpAnswers.map((a) => a.id), 'guide-azure'])
    for (const a of helpAnswers) {
      const m = a.link?.href.match(/topic=([\w-]+)/)
      if (m) expect(ids.has(m[1]), `${a.id} → ${m[1]}`).toBe(true)
    }
  })

  it('?step= opens the guide on that step, and every screen’s (?) names a step that exists (7.6)', () => {
    render(<MemoryRouter initialEntries={['/?panel=help&topic=guide-azure&step=4.4']}><HelpPanel /></MemoryRouter>)
    expect(screen.getByText(/^4\.4 Replay one message/).closest('details')).toHaveAttribute('open')
    expect(screen.getByText(/^4\.3 Open a message/).closest('details')).not.toHaveAttribute('open')
    const steps = azureGuide.sections.flatMap((s) => s.steps.map((st) => st.title.split(' ')[0]))
    for (const [screenId, step] of Object.entries({ ...screenHelpStep, ...pageHelpStep })) expect(steps, screenId).toContain(step)
  })

  it('every control on every Azure screenshot has a numbered callout (7.6, from scripts/docs-shots)', () => {
    const dir = resolve(__dirname, '../../../../../docs/screenshots/azure')
    const gaps = JSON.parse(readFileSync(resolve(dir, 'uncovered.json'), 'utf8')) as Record<string, string[]>
    const keys = JSON.parse(readFileSync(resolve(dir, 'keys.json'), 'utf8')) as Record<string, unknown[]>
    for (const [shot, list] of Object.entries(gaps)) expect(list, `${shot} has controls with no callout`).toEqual([])
    for (const shot of azureGuide.sections.flatMap((s) => s.steps.flatMap((st) => st.shots))) {
      const name = shot.image.split('/').pop() as string
      if (!/^(portal-|18-(search|bell|user))/.test(name) && !name.startsWith('08-message-details-')) expect(keys[name]?.length ?? 0, name).toBeGreaterThan(0)
    }
  })
})
