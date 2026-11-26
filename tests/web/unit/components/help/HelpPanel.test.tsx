import { render } from '@testing-library/react'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, from apps/servicehub
import { existsSync, readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { pageHelpStep, screenHelpStep } from '@/content/help'
import { guides } from '@/content/guides'
import StepByStep from '@/next/overlays/StepByStep'
import { guideClouds, guideTopic } from '@/content/guides/links'
import { expectNoAxeViolations } from '@tests/support/axe'

const panel = (topic: string | null = null) => <StepByStep topic={topic} clouds={guideClouds} topicOf={guideTopic} />

describe('the step-by-step guides in Help', () => {
  it('has no accessibility violations (6.6)', async () => {
    const { container } = render(panel())
    await expectNoAxeViolations(container, { isolatedComponent: true })
  }, 120000)

  it('shows a step-by-step guide per cloud, and ?topic= opens the one asked for (7.6)', () => {
    for (const cloud of Object.keys(guides)) {
      const { unmount } = render(panel(`guide-${cloud}`))
      expect(document.getElementById(`help-guide-${cloud}`) as HTMLElement).toHaveAttribute('open')
      for (const other of Object.keys(guides).filter((c) => c !== cloud)) {
        expect(document.getElementById(`help-guide-${other}`) as HTMLElement).not.toHaveAttribute('open')
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

  it('every screen’s (?) names a step each guide has (7.6)', () => {
    for (const [cloud, guide] of Object.entries(guides)) {
      const steps = guide.sections.flatMap((sec) => sec.steps.map((st) => st.title.split(' ')[0]))
      for (const [screenId, step] of Object.entries({ ...screenHelpStep, ...pageHelpStep })) expect(steps, `${cloud}: ${screenId}`).toContain(step)
    }
  })

  // 5.0.0: the pictures are `docs/screenshots/<cloud>-5`, shot by scripts/docs-shots/v5.spec.mjs. Each one has numbered callouts, and every number
  // has its sentence. (The 4.2.0 rule that every control on a screen be numbered is not kept: a 5.0.0 page has hundreds of (i) buttons, and the
  // text under a picture names the controls that matter.)
  it.each(Object.keys(guides))('every %s screenshot has numbered callouts, each with its sentence', (cloud) => {
    const keys = JSON.parse(readFileSync(`../../docs/screenshots/${cloud}-5/keys.json`, 'utf8')) as Record<string, { n: number; t: string }[]>
    for (const shot of guides[cloud].sections.flatMap((sec) => sec.steps.flatMap((st) => st.shots))) {
      const name = shot.image.split('/').pop() as string
      if (/^portal-/.test(name)) continue
      expect(keys[name]?.length ?? 0, name).toBeGreaterThan(0)
      for (const k of keys[name]) expect(k.t.trim().length, `${name} callout ${k.n} has no text`).toBeGreaterThan(10)
    }
  })
  // The 4.0.0 words stay out of the guides. "Failure signatures" and "Evidence ledger" are page names in the 5.0.0 menu, so they are allowed.
  it.each(Object.keys(guides))('the %s guide uses no 4.0.0 vocabulary outside the Advanced step (7.6)', (cloud) => {
    const banned = /\b(pillars?|dispositions?|autonomy levels?|attestations?|L[45])\b/i
    // Part 1 is the cloud's own console, so it keeps the cloud's words. "grant" is left out of the list: as a verb it is ordinary English.
    for (const step of guides[cloud].sections.filter((sec) => !sec.part.startsWith('Part 1')).flatMap((sec) => sec.steps)) {
      const texts = [step.text, ...step.shots.flatMap((shot) => shot.keys.map((k) => k.text))]
      // Proper names the build itself shows are not ours to reword: Advanced's page names,.
      for (const raw of texts) {
        const t = raw.replace(/Recovery Ledger|Advanced is read-only pages \([^)]*\)/g, '')
        expect(t.match(banned)?.[0] ?? null, `${cloud} ${step.title}: "${raw}"`).toBeNull()
      }
    }
  })
})
