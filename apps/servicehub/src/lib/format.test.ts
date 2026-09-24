import { describe, expect, it } from 'vitest'
import { formatAge, formatBytes, formatWhen } from './format'

const now = new Date('2026-09-24T14:00:00')

describe('format', () => {
  it('sizes', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(2048)).toBe('2.0 KB')
    expect(formatBytes(12 * 1024)).toBe('12 KB')
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB')
  })

  it('how long something has waited, never negative', () => {
    expect(formatAge('2026-09-24T13:59:40', now)).toBe('just now')
    expect(formatAge('2026-09-24T13:48:00', now)).toBe('12 min')
    expect(formatAge('2026-09-24T10:00:00', now)).toBe('4 h')
    expect(formatAge('2026-09-21T14:00:00', now)).toBe('3 d')
    expect(formatAge('2026-09-25T09:00:00', now)).toBe('just now')
  })

  it('shows only the clock for today and adds the date otherwise', () => {
    expect(formatWhen('2026-09-24T10:12:00', now)).toBe('10:12')
    expect(formatWhen('2026-09-22T10:12:00', now)).toBe('Sep 22, 10:12')
  })
})
