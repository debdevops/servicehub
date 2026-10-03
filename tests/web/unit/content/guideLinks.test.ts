import { describe, expect, it } from 'vitest'
import { guides } from '@/content/guides'
import { guideClouds } from '@/content/guides/links'

describe('guide links', () => {
  it('lists exactly the clouds that have a guide', () => {
    expect([...guideClouds].sort()).toEqual(Object.keys(guides).sort())
  })
})
