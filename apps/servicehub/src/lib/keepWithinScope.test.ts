import { describe, expect, it } from 'vitest'
import { keepWithinScope } from './keepWithinScope'

describe('keepWithinScope', () => {
  const previous = { rows: [1, 2, 3] }

  it('keeps the previous page while paging or filtering inside one cloud', () => {
    expect(keepWithinScope<typeof previous>('azure')(previous, { queryKey: ['dead-letters', 'list', { provider: 'azure', page: 1 }] })).toBe(previous)
  })

  it('shows nothing — so the screen shows loading — while switching to another cloud', () => {
    expect(keepWithinScope<typeof previous>('aws')(previous, { queryKey: ['dead-letters', 'list', { provider: 'azure', page: 1 }] })).toBeUndefined()
  })

  it('reads the cloud from a bare key part too (the trend)', () => {
    expect(keepWithinScope<typeof previous>('gcp')(previous, { queryKey: ['dead-letters', 'trend', 'azure', 7] })).toBeUndefined()
    expect(keepWithinScope<typeof previous>('azure')(previous, { queryKey: ['dead-letters', 'trend', 'azure', 14] })).toBe(previous)
  })

  it('keeps nothing when there was no previous query', () => {
    expect(keepWithinScope<typeof previous>('azure')(previous, undefined)).toBeUndefined()
  })
})
