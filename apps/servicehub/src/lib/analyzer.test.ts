import { describe, expect, it } from 'vitest'
import { explainFailure } from './analyzer'

describe('explainFailure — the one-line reading for a table row', () => {
  const headline = (reason: string | null, description: string | null = null, tries = 5) => explainFailure(reason, description, tries).headline

  it('names the missing field', () => expect(headline('Validation', 'Required field customerId missing')).toContain('customerId'))
  it('says a malformed body is malformed', () => expect(headline(null, 'Unexpected token in JSON at position 0')).toMatch(/malformed/))
  it('explains max-delivery with the real number of tries', () => expect(headline('MaxDeliveryCountExceeded', 'Message could not be consumed after 10 delivery attempts.', 10)).toBe('Delivered 10 times and never completed'))
  it('says one try in the singular', () => expect(headline('MaxDeliveryCountExceeded', null, 1)).toBe('Delivered 1 time and never completed'))
  it('reads a lost lock', () => expect(headline('LockLost', 'Message lock expired before completion')).toMatch(/lost its claim/))
  it('reads a timeout', () => expect(headline('Timeout', 'Downstream took longer than 30 s')).toMatch(/too long/))
  it('reads a rejected credential', () => expect(headline('Authentication', 'Token expired for downstream call')).toMatch(/not allowed/))
  it('says what is true when the cloud recorded nothing, and does not pass it off as a reading', () => {
    const e = explainFailure(null, null, 4)
    expect(e.headline).toBe('The cloud recorded no reason — read the message itself')
    expect(e.recorded).toBe(false)
  })
  it('never invents a reading it does not have', () => expect(headline('SomethingOdd', 'zzz')).toBe('No reading available — see the recorded reason'))
})
