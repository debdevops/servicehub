import axe from 'axe-core'
import { expect } from 'vitest'

/**
 * Accessibility gate (unit 6.6): axe over a rendered screen, failing on any violation. jsdom has no layout or colours, so axe's
 * colour-contrast rule cannot run here — contrast is checked in the design tokens, not by this helper.
 */
export async function expectNoAxeViolations(container: Element): Promise<void> {
  const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false }, region: { enabled: false } } })
  const report = result.violations.map((v) => `${v.id}: ${v.help}\n  ${v.nodes.slice(0, 3).map((n) => n.html).join('\n  ')}`).join('\n')
  expect(result.violations, report).toEqual([])
}
