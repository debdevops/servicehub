import { describe, expect, it } from 'vitest'
import { auditActionWords } from '@/lib/auditWords'

// Every action the API writes to the audit trail (AuditActions.cs + the Governance grant service). One that is missing here
// reaches Recent activity as a raw code like "Message.Send".
const emitted = [
  'Namespace.Connect', 'Namespace.Remove', 'Replay.Message', 'DeadLetters.Look', 'Agent.Pause', 'Agent.Resume', 'Purge.Message',
  'Message.Send', 'Backup.Create', 'Backup.Restore', 'Rule.Create', 'Rule.Update', 'Rule.Toggle', 'Rule.Delete', 'Rule.Generate',
  'Governance.Grant', 'Governance.Revoke', 'DlqObserver.Configure',
]

describe('auditActionWords', () => {
  it.each(emitted)('has plain words for %s', (action) => {
    expect(auditActionWords[action]).toBeTruthy()
    expect(auditActionWords[action]).not.toMatch(/\w\.\w/)
  })
})
