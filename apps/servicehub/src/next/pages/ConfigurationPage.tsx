import { useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useLocation, useSearchParams } from 'react-router-dom'
import { BackupSection } from '../../components/settings/BackupSection'
import { Access, Connections, Notifications, Preferences } from '../../components/settings/SettingsModal'
import { Skeleton } from '../../components/ui/Skeleton'
import { useMe } from '../../hooks/useIdentity'
import { useSettings } from '../../hooks/useSettings'
import { permission } from '../../lib/permissions'
import { ToolPageHeader } from '../components/ToolPageHeader'
import { nextEntry, nextHref } from '../nav/navigation'

const tabs = [
  { id: 'connections', label: 'Connections' }, { id: 'notifications', label: 'Notifications' }, { id: 'access', label: 'Access' },
  { id: 'preferences', label: 'Preferences' }, { id: 'backup', label: 'Backup' },
] as const
type Tab = (typeof tabs)[number]['id']
const asTab = (v: string | null): Tab => tabs.find((t) => t.id === v)?.id ?? 'connections'

/**
 * Configuration (build-plan unit E9; design `p14-configuration`): how ServiceHub is set up — what it is connected to, whom it tells,
 * who may do what, how you see things, and its backups. It is not scoped to a cloud. Each tab is the 4.2.0 Settings section of the
 * same name, **moved, not rewritten** (`components/settings/SettingsModal` now exports them), so every control keeps its own
 * permission and its own confirmation (removing a connection, the emergency stop's typed confirmation, a restore check). The
 * server still decides every one of them. Only "Add a cloud" differs: it opens this shell's Connect dialog.
 */
export default function ConfigurationPage() {
  const { pathname } = useLocation()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const me = useMe().data
  const settings = useSettings()
  const tab = asTab(params.get('tab'))
  const connect = nextHref(nextEntry('connect'), {}, pathname)

  return (
    <div className="space-y-4 px-6 py-5">
      <ToolPageHeader scope="all" title="Configuration" updatedAtMs={settings.dataUpdatedAt} onRefresh={() => void client.invalidateQueries({ queryKey: ['settings'] })}
        lookingAt="How ServiceHub is set up: what it is connected to, whom it tells, who may do what, and how you see things." />

      <div className="flex flex-wrap items-end justify-between gap-3 border-b border-[var(--color-border)]">
        <div role="tablist" aria-label="Configuration sections" className="flex flex-wrap gap-5">
          {tabs.map((t) => (
            <button key={t.id} role="tab" type="button" aria-selected={tab === t.id} onClick={() => setParams((p) => { const n = new URLSearchParams(p); if (t.id === 'connections') n.delete('tab'); else n.set('tab', t.id); return n }, { replace: true })}
              className={`-mb-px border-b-2 px-1 py-2.5 text-sm font-semibold ${tab === t.id ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)]'}`}>{t.label}</button>
          ))}
        </div>
      </div>

      <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-5">
        {tab === 'connections' && <Connections admin={permission(me, 'Admin', 'remove a namespace')} addHref={connect} />}
        {tab === 'preferences' && <Preferences />}
        {tab !== 'connections' && tab !== 'preferences' && (
          settings.isPending ? <Skeleton label="Reading settings…" rows={4} />
            : settings.isError || !settings.data
              ? <p role="alert" className="text-sm">ServiceHub couldn’t read its settings. <button type="button" className="font-medium text-[var(--color-primary-700)] hover:underline" onClick={() => void settings.refetch()}>Try again</button></p>
              : tab === 'notifications' ? <Notifications data={settings.data} admin={permission(me, 'Admin', 'set up notifications')} />
              : tab === 'access' ? <Access data={settings.data} />
              : <BackupSection keyFingerprint={settings.data.security.keyFingerprint} />
        )}
      </div>
      {tab === 'connections' && <p className="flex items-center gap-1.5 text-xs text-[var(--color-text-muted)]"><Plus className="h-3.5 w-3.5" aria-hidden="true" /> Credentials are stored encrypted and never shown again. Removing a connection stops the watching and keeps the history.</p>}
    </div>
  )
}
