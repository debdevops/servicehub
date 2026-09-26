import { describe, expect, it } from 'vitest'
import type { PendingWorkItem } from './api/pendingWork'
import { pendingRows } from './pendingRows'

const item = (over: Partial<PendingWorkItem>): PendingWorkItem => ({
  kind: 'approval', id: 'e1', entryId: 'e1', agentId: null, dlqMessageId: 1, namespaceId: 'n1', namespaceName: 'aws-eu-west-1', provider: 'aws',
  environment: 'dev', entity: 'orders-sqs', deadLetterReason: 'Timeout', ruleId: 1, ruleName: 'Retry timeouts',
  reasonCode: 'PROVIDER_CANNOT_VERIFY_ABSENCE', reason: 'This cloud can’t prove…', since: '2026-09-26T10:00:00Z', ...over,
})

describe('pendingRows', () => {
  it('makes approvals in one namespace one question with one Review action', () => {
    const rows = pendingRows([item({}), item({ id: 'e2', entryId: 'e2', entity: 'payments-sqs' })])
    expect(rows).toHaveLength(1)
    expect(rows[0].title).toBe('2 replays need your approval')
    expect(rows[0].where).toBe('AWS · aws-eu-west-1 · orders-sqs, payments-sqs')
    expect(rows[0].action.href).toBe('?modal=approve&group=aws%3An1')
    expect(rows[0].items).toHaveLength(2)
  })
  it('keeps the server order — a stopped agent first — and gives each its own resolving action', () => {
    const rows = pendingRows([
      item({ kind: 'agent', id: 'agent:dlq-monitor', agentId: 'dlq-monitor', provider: null, reasonCode: 'AGENT_STALE', reason: 'Dead-letter Monitor: stopped' }),
      item({ kind: 'rule', id: 'rule:4', ruleId: 4, ruleName: 'Billing timeouts', entryId: null, reasonCode: 'RULE_CIRCUIT_BREAKER' }),
      item({}),
    ])
    expect(rows.map((r) => r.kind)).toEqual(['agent', 'rule', 'approval'])
    expect(rows[0].action.href).toBe('/advanced/agents?agent=dlq-monitor')
    expect(rows[1].action.href).toBe('?panel=rules&rule=4')
    expect(rows[2].title).toBe('1 replay needs your approval')
  })
})
