import { describe, expect, it } from 'vitest'
import { waitingHref } from '@/lib/urlState'

describe('where a rule’s held messages are listed', () => {
  it('asks for a real reason by name', () => {
    expect(waitingHref({ provider: 'azure', reason: 'Timeout', entityName: 'orders' })).toBe('/?tab=dlq&provider=azure&reason=Timeout&entity=orders')
  })

  it('"Unknown" asks for the messages with no recorded reason — the list has no reason called Unknown, so it would be blank', () => {
    expect(waitingHref({ provider: 'gcp', reason: 'Unknown', entityName: null })).toBe('/?tab=dlq&provider=gcp&reason=__none__')
  })
})
