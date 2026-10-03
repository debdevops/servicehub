import axe from 'axe-core'
import { expect } from 'vitest'

/**
 * Accessibility gate (unit 6.6): axe over a rendered screen, failing on any violation. jsdom has no layout or colours, so axe's
 * colour-contrast rule cannot run here — contrast is checked in the design tokens, not by this helper.
 *
 * The `region` rule (all content inside a landmark) stays ON so full-screen tests catch content outside landmarks. Pass
 * `{ isolatedComponent: true }` only for a component rendered alone, where there is no surrounding page landmark.
 */
export async function expectNoAxeViolations(container: Element, options: { isolatedComponent?: boolean } = {}): Promise<void> {
  const result = await axe.run(container, {
    rules: { 'color-contrast': { enabled: false }, region: { enabled: !options.isolatedComponent } },
  })
  const report = result.violations.map((v) => `${v.id}: ${v.help}\n  ${v.nodes.slice(0, 3).map((n) => n.html).join('\n  ')}`).join('\n')
  expect(result.violations, report).toEqual([])
}
