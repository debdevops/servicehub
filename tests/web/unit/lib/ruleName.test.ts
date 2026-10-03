import { describe, expect, it } from 'vitest'
import { ruleTitle } from '@/lib/ruleName'

describe('ruleTitle', () => {
  it('says "No recorded reason" instead of a failure called Unknown, and reads a subscription path as topic › subscription', () => {
    expect(ruleTitle('Auto: Unknown in orders-topic/subscriptions/billing')).toBe('Auto: No recorded reason in orders-topic › billing')
  })
  it('leaves a name a person chose, or a real reason, alone', () => {
    expect(ruleTitle('Payment timeouts')).toBe('Payment timeouts')
    expect(ruleTitle('Auto: Timeout in payments')).toBe('Auto: Timeout in payments')
  })
})
