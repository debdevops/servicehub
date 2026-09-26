import { api } from './client'
import { Intent, withIntent } from './intentHeaders'

/** One backup bundle (unit 6.13) — 4.0.0's summary, unchanged. */
export interface BackupSummary {
  readonly backupId: string
  readonly createdAtUtc: string
  readonly totalSizeBytes: number
  readonly integrityCheck: string
  readonly namespaceStorePresent: boolean
}

export interface PendingRestore {
  readonly backupId: string
  readonly stagedAtUtc: string
}

export interface BackupList {
  readonly backups: readonly BackupSummary[]
  readonly pending: PendingRestore | null
}

export interface RestoreCheck {
  readonly backupId: string
  readonly canRestore: boolean
  readonly checks: readonly { readonly name: string; readonly passed: boolean; readonly detail: string }[]
}

export async function fetchBackups(): Promise<BackupList> {
  return (await api.get<BackupList>('/admin/backup')).data
}

export async function createBackup(): Promise<{ backupId: string }> {
  return (await api.post<{ backupId: string }>('/admin/backup', undefined, { headers: withIntent(Intent.CreateBackup) })).data
}

export async function checkBackup(id: string): Promise<RestoreCheck> {
  return (await api.get<RestoreCheck>(`/admin/backup/${encodeURIComponent(id)}/check`)).data
}

/** Stages it for the next start. A refused bundle comes back as 409 with the failed checks. */
export async function restoreBackup(id: string, confirm: string): Promise<RestoreCheck> {
  return (await api.post<RestoreCheck>(`/admin/backup/${encodeURIComponent(id)}/restore`, { confirm }, { headers: withIntent(Intent.RestoreBackup) })).data
}

export async function cancelRestore(): Promise<void> {
  await api.delete('/admin/backup/pending', { headers: withIntent(Intent.RestoreBackup) })
}

/** Saves the backup's database file through the browser. */
export async function downloadBackup(id: string): Promise<void> {
  const res = await api.get<Blob>(`/admin/backup/${encodeURIComponent(id)}/download`, { responseType: 'blob' })
  const url = URL.createObjectURL(res.data)
  Object.assign(document.createElement('a'), { href: url, download: `servicehub-backup-${id}.db` }).click()
  URL.revokeObjectURL(url)
}
