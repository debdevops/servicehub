import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as backup from '../../lib/api/backup'
import * as identity from '../../lib/api/identity'
import type { Me } from '../../lib/api/identity'
import { BackupSection } from './BackupSection'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/backup')
vi.mock('../../lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

const me = (role: 'Admin' | 'Viewer'): Me => ({ ownerId: 'o', authMethod: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, effectiveRole: role, governanceActive: role !== 'Admin' }) as Me
const one = { backupId: '20260926-120000Z', createdAtUtc: '2026-09-26T12:00:00Z', totalSizeBytes: 4096, integrityCheck: 'ok', namespaceStorePresent: false }

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><BackupSection keyFingerprint="abc123" /></QueryClientProvider>)
}

describe('Settings → Backup', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(identity.fetchMe).mockResolvedValue(me('Admin'))
    vi.mocked(backup.fetchBackups).mockResolvedValue({ backups: [one], pending: null })
  })

  it('has no accessibility violations (6.6)', async () => {
    const { container } = wrap()
    await screen.findByRole('button', { name: 'Restore…' })
    await expectNoAxeViolations(container)
  })

  it('says the encryption key is not in a backup and where to keep it', async () => {
    wrap()
    expect(await screen.findByText(/does/)).toHaveTextContent(/keep that key somewhere else/)
    expect(screen.getByText('abc123')).toBeInTheDocument()
  })

  it('restores only after every check passes and RESTORE is typed — and then only at the next start', async () => {
    const user = userEvent.setup()
    vi.mocked(backup.checkBackup).mockResolvedValue({ backupId: one.backupId, canRestore: true, checks: [{ name: 'Its evidence ledger verifies', passed: true, detail: '4 events, every chain unbroken.' }] })
    vi.mocked(backup.restoreBackup).mockResolvedValue({ backupId: one.backupId, canRestore: true, checks: [] })
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Restore…' }))
    expect(await screen.findByText(/every chain unbroken/)).toBeInTheDocument()
    const go = screen.getByRole('button', { name: 'Restore at the next start' })
    expect(go).toBeDisabled()
    await user.type(screen.getByLabelText('Type RESTORE to confirm'), 'RESTORE')
    await user.click(go)
    expect(backup.restoreBackup).toHaveBeenCalledWith(one.backupId, 'RESTORE')
  })

  it('shows why a backup cannot be restored and offers no restore button', async () => {
    const user = userEvent.setup()
    vi.mocked(backup.checkBackup).mockResolvedValue({ backupId: one.backupId, canRestore: false, checks: [{ name: 'It was made with this server’s encryption key', passed: false, detail: 'It was made with a different encryption key.' }] })
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Restore…' }))
    const checks = await screen.findByRole('list', { name: 'Checks' })
    expect(within(checks).getByLabelText('Failed')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Restore at the next start' })).not.toBeInTheDocument()
  })

  it('shows a staged restore and lets it be cancelled', async () => {
    const user = userEvent.setup()
    vi.mocked(backup.fetchBackups).mockResolvedValue({ backups: [one], pending: { backupId: one.backupId, stagedAtUtc: '2026-09-26T12:05:00Z' } })
    vi.mocked(backup.cancelRestore).mockResolvedValue()
    wrap()
    expect(await screen.findByText(/will replace the database at the next start/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cancel the restore' }))
    expect(backup.cancelRestore).toHaveBeenCalled()
  })

  it('is Admin only and says so', async () => {
    vi.mocked(identity.fetchMe).mockResolvedValue(me('Viewer'))
    wrap()
    expect(await screen.findByText(/manage backups/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Take a backup/ })).not.toBeInTheDocument()
    expect(backup.fetchBackups).not.toHaveBeenCalled()
  })
})
