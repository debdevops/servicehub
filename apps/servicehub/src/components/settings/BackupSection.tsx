import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, DatabaseBackup, Download, X } from 'lucide-react'
import { useState } from 'react'
import { useMe } from '../../hooks/useIdentity'
import * as api from '../../lib/api/backup'
import { toProblem } from '../../lib/api/client'
import { formatBytes, formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'

const keys = { all: ['backups'] as const }
const btn = 'inline-flex items-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-sm font-semibold hover:bg-[var(--color-surface-muted)] disabled:opacity-50'
const primary = 'rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-semibold text-white hover:bg-[var(--color-primary-700)] disabled:opacity-50'

/**
 * Settings → Backup (unit 6.13). Take one, download one, restore one. A restore is checked first and then waits for the next
 * start — the running database is never swapped. The encryption key is not in a backup, and the section says where it must be.
 */
export function BackupSection({ keyFingerprint }: { keyFingerprint: string }) {
  const me = useMe().data
  const may = permission(me, 'Admin', 'manage backups')
  const list = useQuery({ queryKey: keys.all, queryFn: api.fetchBackups, enabled: !!me && may.allowed, retry: false })
  const client = useQueryClient()
  const refresh = () => void client.invalidateQueries({ queryKey: keys.all })
  const create = useMutation({ mutationFn: api.createBackup, onSettled: refresh })
  const cancel = useMutation({ mutationFn: api.cancelRestore, onSettled: refresh })
  const [open, setOpen] = useState<string | null>(null)

  return (
    <section id="settings-backup" aria-label="Backup" className="space-y-3">
      <h2 className="text-[17px] font-bold text-[var(--color-text)]">Backup</h2>
      <p className="text-sm text-[var(--color-text-muted)]">
        A backup is ServiceHub’s whole database — connections, dead letters, the evidence ledger — checked and kept in the data
        directory’s <code>backups</code> folder. It does <b>not</b> contain the encryption key (fingerprint <code>{keyFingerprint}</code>):
        keep that key somewhere else, or a restored backup cannot open its saved connections.
      </p>
      <NotAllowed reason={may.reason} />
      {me && may.allowed && (
        <>
          {list.data?.pending && (
            <div role="status" className="flex flex-wrap items-center gap-3 rounded-xl border border-[#fcd34d] bg-[#fffbeb] px-4 py-3 text-sm">
              <span className="min-w-0 flex-1">Backup <b>{list.data.pending.backupId}</b> will replace the database at the next start. The current one is kept beside it.</span>
              <button type="button" className={btn} disabled={cancel.isPending} onClick={() => cancel.mutate()}>Cancel the restore</button>
            </div>
          )}
          <div>
            <button type="button" className={`${primary} inline-flex items-center gap-2`} disabled={create.isPending} onClick={() => create.mutate()}>
              <DatabaseBackup className="h-4 w-4" aria-hidden="true" /> {create.isPending ? 'Taking a backup…' : 'Take a backup now'}
            </button>
            {create.isError && <p role="alert" className="mt-2 text-sm text-[#b91c1c]">{toProblem(create.error).message}</p>}
            {create.data && <p className="mt-2 text-sm">Backup <b>{create.data.backupId}</b> taken and checked.</p>}
          </div>
          {list.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading backups…</p>}
          {list.isError && <p role="alert" className="text-sm">ServiceHub couldn’t read its backups.</p>}
          {list.data && list.data.backups.length === 0 && <p className="text-sm text-[var(--color-text-muted)]">No backups yet.</p>}
          {list.data && list.data.backups.length > 0 && (
            <ul className="divide-y divide-[var(--color-border)] rounded-xl border border-[var(--color-border)]">
              {list.data.backups.map((b) => (
                <li key={b.backupId} className="px-4 py-3 text-sm">
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="min-w-0 flex-1">
                      <b>{formatWhen(b.createdAtUtc, new Date())}</b>
                      <span className="text-[var(--color-text-muted)]"> · {formatBytes(b.totalSizeBytes)} · {b.integrityCheck === 'ok' ? 'checked' : `check: ${b.integrityCheck}`}</span>
                    </span>
                    <button type="button" className={btn} aria-label={`Download backup ${b.backupId}`} onClick={() => void api.downloadBackup(b.backupId)}><Download className="h-4 w-4" aria-hidden="true" /> Download</button>
                    <button type="button" className={btn} aria-expanded={open === b.backupId} onClick={() => setOpen(open === b.backupId ? null : b.backupId)}>Restore…</button>
                  </div>
                  {open === b.backupId && <RestorePanel id={b.backupId} onDone={() => { setOpen(null); refresh() }} />}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  )
}

function RestorePanel({ id, onDone }: { id: string; onDone: () => void }) {
  const check = useQuery({ queryKey: [...keys.all, 'check', id], queryFn: () => api.checkBackup(id), retry: false, staleTime: 0 })
  const restore = useMutation({ mutationFn: (confirm: string) => api.restoreBackup(id, confirm), onSuccess: onDone })
  const [confirm, setConfirm] = useState('')
  return (
    <div className="mt-3 rounded-lg bg-[var(--color-surface-muted)] p-3">
      {check.isPending && <p role="status">Checking this backup…</p>}
      {check.isError && <p role="alert">ServiceHub couldn’t check this backup.</p>}
      {check.data && (
        <>
          <ul aria-label="Checks" className="space-y-1.5">
            {check.data.checks.map((c) => (
              <li key={c.name} className="flex gap-2">
                {c.passed ? <Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-success)]" aria-label="Passed" /> : <X className="mt-0.5 h-4 w-4 shrink-0 text-[#dc2626]" aria-label="Failed" />}
                <span><b>{c.name}.</b> {c.detail}</span>
              </li>
            ))}
          </ul>
          {check.data.canRestore ? (
            <form className="mt-3 space-y-2" onSubmit={(e) => { e.preventDefault(); restore.mutate(confirm) }}>
              <p>Everything recorded since this backup will be replaced when ServiceHub next starts. Nothing changes until then, and you can cancel.</p>
              <input value={confirm} onChange={(e) => setConfirm(e.target.value)} placeholder="Type RESTORE to confirm" aria-label="Type RESTORE to confirm" className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 font-mono" />
              <button type="submit" className="rounded-lg bg-[#dc2626] px-3.5 py-2 font-semibold text-white disabled:opacity-50" disabled={confirm.trim() !== 'RESTORE' || restore.isPending}>Restore at the next start</button>
              {restore.isError && <p role="alert" className="text-[#b91c1c]">{toProblem(restore.error).message}</p>}
            </form>
          ) : (
            <p className="mt-3 font-medium">This backup can’t be restored here, for the reason above.</p>
          )}
        </>
      )}
    </div>
  )
}
