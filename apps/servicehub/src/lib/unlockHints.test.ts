import gate from '../../../../services/api/src/ServiceHub.Infrastructure/RecoveryLedger/RecoveryEligibilityGate.cs?raw'
import { describe, expect, it } from 'vitest'
import { GATE_REASON_CODES, hintFor, unlockHints } from './unlockHints'

describe('unlock hints', () => {
  it('cover every reason code the gate can give — read from the gate itself', () => {
    const codes = new Set([...gate.matchAll(/"([A-Z][A-Z_]{6,})"/g)].map((m) => m[1]))
    expect(codes.size).toBeGreaterThan(10)
    for (const code of codes) expect(GATE_REASON_CODES, `no unlock hint for ${code}`).toContain(code)
  })

  it('never leave a dead end: each says what would change the answer', () => {
    for (const code of GATE_REASON_CODES) {
      const h = unlockHints[code]
      expect(h.why.length, code).toBeGreaterThan(10)
      expect(h.takes.length, code).toBeGreaterThan(10)
    }
    expect(hintFor('SOMETHING_NEW').go?.href).toBe('/advanced/ledger?state=Waiting')
  })
})
