import { describe, expect, it } from 'vitest'
import { isEnded } from './bulk'

// The API writes enums as camelCase words. A live run once looked "ended" from its first second because the client compared
// against PascalCase — and every test that mocked the API with the same wrong casing passed.
describe('isEnded', () => {
  it('reads the words the API actually sends', () => {
    expect(isEnded('running')).toBe(false)
    expect(isEnded('previewed')).toBe(false)
    for (const s of ['completed', 'cancelled', 'stopped', 'expired'] as const) expect(isEnded(s)).toBe(true)
  })
})
