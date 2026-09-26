import { describe, expect, it } from 'vitest'
import type { SignatureTrust } from './api/signatures'
import { trustWords } from './trustWords'

const t = (over: Partial<SignatureTrust> = {}): SignatureTrust => ({
  level: 'approve', sampleSize: 0, verifiedSuccessRate: null, recovered: 0, returned: 0, failed: 0, unverified: 0,
  nextLevel: 'standing', moreVerifiedNeeded: 10, rateNeeded: 0.95, cloudCanConfirm: true, productionCeiling: false, reasons: [], ...over,
})

describe('trustWords — can ServiceHub replay this failure on its own?', () => {
  it('starts at "not yet", with the number still needed', () => {
    const w = trustWords(t(), 'Azure')
    expect(w.answer).toBe('not yet')
    expect(w.text).toContain('10 more verified fixes, at 95%')
    expect(w.text).toContain('Nothing has been replayed and verified yet')
  })
  it('counts progress from recorded outcomes', () => {
    expect(trustWords(t({ sampleSize: 4, recovered: 4, verifiedSuccessRate: 1, moreVerifiedNeeded: 6 }), 'Azure').text).toContain('4 of 4 verified replays stayed fixed (100%)')
  })
  it('says no, and why, where the cloud cannot confirm — never "not yet"', () => {
    const w = trustWords(t({ cloudCanConfirm: false, nextLevel: null, moreVerifiedNeeded: null, rateNeeded: null }), 'AWS')
    expect(w.answer).toBe('no')
    expect(w.text).toContain('AWS can’t confirm')
  })
  it('says Production is always a person', () => expect(trustWords(t({ productionCeiling: true, nextLevel: null }), 'Azure').answer).toBe('no'))
  it('says yes once earned', () => expect(trustWords(t({ level: 'standing', sampleSize: 12, recovered: 12, verifiedSuccessRate: 1, nextLevel: 'unattended', moreVerifiedNeeded: 18, rateNeeded: 0.99 }), 'Azure').text).toContain('18 more at 99%'))
  it('does not claim a lack of samples when the rate is what is missing', () =>
    expect(trustWords(t({ sampleSize: 12, recovered: 10, verifiedSuccessRate: 10 / 12, moreVerifiedNeeded: 0 }), 'Azure').text).toContain('not at 95% or better'))
})
