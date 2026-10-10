import type { BackupList, RestoreCheck } from '../../api/backup'
import { refuse, type Route } from './http'

const NO_DATABASE = 'The demo has no database of its own, so there is nothing to back up or restore. Your own ServiceHub does this in Settings.'

export const backup: readonly Route[] = [
  // Two made-up backups, so the screen shows what a real one looks like.
  ['get', /^\/admin\/backup$/, (_m, { w }): BackupList => ({
    backups: [1, 2].map((days) => {
      const at = new Date(w.builtAt - days * 86_400_000)
      return { backupId: at.toISOString().slice(0, 13).replace(/[-T]/g, '') + '00Z', createdAtUtc: at.toISOString(), totalSizeBytes: 4_812_800 - days * 96_256, integrityCheck: 'ok', namespaceStorePresent: true }
    }),
    pending: null,
  })],
  ['get', /^\/admin\/backup\/([^/]+)\/check$/, (m): RestoreCheck => ({ backupId: decodeURIComponent(m[1]), canRestore: false, checks: [{ name: 'Demo', passed: false, detail: NO_DATABASE }] })],
  ['get', /^\/admin\/backup\/([^/]+)\/download$/, () => refuse(409, 'demo_no_database', NO_DATABASE)],
  ['post', /^\/admin\/backup$/, () => refuse(409, 'demo_no_database', NO_DATABASE)],
  ['post', /^\/admin\/backup\/([^/]+)\/restore$/, () => refuse(409, 'demo_no_database', NO_DATABASE)],
  ['delete', /^\/admin\/backup\/pending$/, () => refuse(409, 'demo_no_database', NO_DATABASE)],
]
