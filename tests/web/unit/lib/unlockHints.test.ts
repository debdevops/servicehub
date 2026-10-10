import gate from '../../../../services/api/src/ServiceHub.Infrastructure/RecoveryLedger/RecoveryEligibilityGate.cs?raw'
import guard from '../../../../services/api/src/ServiceHub.Infrastructure/RecoveryLedger/RecentResultsGuardGate.cs?raw'
import escalations from '../../../../services/api/src/ServiceHub.Core/Constants/EscalationReasons.cs?raw'
import { describe, expect, it } from 'vitest'
import { GATE_REASON_CODES, hintFor, unlockHints } from '@/lib/unlockHints'

describe('unlock hints', () => {
  it('cover every reason code the gate can give — read from the gate and its guard', () => {
    const codes = new Set([...(gate + guard).matchAll(/"([A-Z][A-Z_]{6,})"/g)].map((m) => m[1]))
    expect(codes.size).toBeGreaterThan(10)
    for (const code of codes) expect(GATE_REASON_CODES, `no unlock hint for ${code}`).toContain(code)
  })

  it('has a hint for the unknown-outcome block, which comes from the escalation reasons rather than the gate', () => {
    expect(escalations).toContain('REPLAY_OUTCOME_UNKNOWN')
    expect(GATE_REASON_CODES).toContain('REPLAY_OUTCOME_UNKNOWN')
    expect(hintFor('REPLAY_OUTCOME_UNKNOWN').takes).toMatch(/says what happened/)
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
