import { Bell, Cloud, DatabaseBackup, KeyRound, Link2, Lock, OctagonAlert, SlidersHorizontal, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useMe } from '../../hooks/useIdentity'
import { useNamespaces, useRemoveNamespace, useTestConnection } from '../../hooks/useNamespaces'
import { useGrants, useSettings, useSettingsMutation } from '../../hooks/useSettings'
import { toProblem } from '../../lib/api/client'
import type { Role } from '../../lib/api/identity'
import type { Namespace } from '../../lib/api/namespaces'
import * as api from '../../lib/api/settings'
import { formatAge, formatWhen } from '../../lib/format'
import { permission } from '../../lib/permissions'
import { browserTimeZone, usePreferences, writePreferences } from '../../lib/preferences'
import { providerLabel } from '../../lib/providers'
import { environmentMeta } from '../provider/scopeChoice'
import { NotAllowed } from '../ui/NotAllowed'
import { BackupSection } from './BackupSection'

const sections = [
  { id: 'connections', label: 'Connections', Icon: Cloud },
  { id: 'notifications', label: 'Notifications', Icon: Bell },
  { id: 'preferences', label: 'Preferences', Icon: SlidersHorizontal },
  { id: 'access', label: 'Access & security', Icon: KeyRound },
  { id: 'backup', label: 'Backup', Icon: DatabaseBackup },
] as const

const btn = 'rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 text-sm font-semibold hover:bg-[var(--color-surface-muted)] disabled:opacity-50'
const primary = 'rounded-lg bg-[var(--color-primary-600)] px-3.5 py-2 text-sm font-semibold text-white hover:bg-[var(--color-primary-700)] disabled:opacity-50'
const h2 = 'text-[17px] font-bold text-[var(--color-text)]'

/**
 * Settings (unit 6.3, `?modal=settings`): connections, notifications, preferences, access and backup (6.13) — one scrolling modal, nothing
 * else. The bell is shown locked on (R7). Emergency stop lives in Access & security, Admin only, with a typed confirmation
 * (owner decision O3). No users section (deferred). Every control does something; there is no "coming soon" switch that works.
 */
export default function SettingsModal() {
  const me = useMe().data
  const settings = useSettings()
  return (
    <div className="grid gap-6 md:grid-cols-[180px_minmax(0,1fr)]">
      <nav aria-label="Settings sections" className="hidden md:block">
        <ul className="sticky top-0 space-y-1">
          {sections.map(({ id, label, Icon }) => (
            <li key={id}>
              <a href={`#settings-${id}`} className="flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-[var(--color-text)] hover:bg-[var(--color-surface-muted)]">
                <Icon className="h-4 w-4 text-[var(--color-text-muted)]" aria-hidden="true" /> {label}
              </a>
            </li>
          ))}
        </ul>
      </nav>
      <div className="min-w-0 space-y-8">
        <Connections admin={permission(me, 'Admin', 'remove a namespace')} />
        {settings.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading settings…</p>}
        {settings.isError && <p role="alert" className="text-sm">ServiceHub couldn’t read its settings. <button type="button" className="font-medium text-[var(--color-primary-700)] hover:underline" onClick={() => void settings.refetch()}>Try again</button></p>}
        {settings.data && <Notifications data={settings.data} admin={permission(me, 'Admin', 'set up notifications')} />}
        <Preferences />
        {settings.data && <Access data={settings.data} />}
        {settings.data && <BackupSection keyFingerprint={settings.data.security.keyFingerprint} />}
      </div>
    </div>
  )
}

function Connections({ admin }: { admin: { allowed: boolean; reason: string | null } }) {
  const namespaces = useNamespaces()
  const { pathname, search } = useLocation()
  const addHref = (() => { const q = new URLSearchParams(search); q.set('modal', 'add-cloud'); return `${pathname}?${q}` })()
  return (
    <section id="settings-connections" aria-label="Connections">
      <div className="mb-3 flex items-center justify-between">
        <h2 className={h2}>Connections</h2>
        <Link to={addHref} className={primary}>+ Add a cloud</Link>
      </div>
      {namespaces.isPending && <p role="status" className="text-sm text-[var(--color-text-muted)]">Reading connections…</p>}
      {namespaces.data && namespaces.data.length === 0 && <p className="text-sm text-[var(--color-text-muted)]">Nothing is connected yet. Add a cloud to start.</p>}
      <ul className="divide-y divide-[var(--color-border)]">
        {(namespaces.data ?? []).map((n) => <ConnectionRow key={n.id} ns={n} admin={admin} />)}
      </ul>
      <NotAllowed reason={admin.reason} />
    </section>
  )
}

function ConnectionRow({ ns, admin }: { ns: Namespace; admin: { allowed: boolean } }) {
  const test = useTestConnection()
  const remove = useRemoveNamespace()
  const [confirming, setConfirming] = useState(false)
  const now = new Date()
  const ok = ns.lastConnectionTestSucceeded
  return (
    <li className="flex flex-wrap items-center gap-3 py-3">
      <div className="min-w-0 flex-1">
        <p className="flex items-center gap-2 font-semibold">
          {ns.displayName ?? ns.name}
          <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${environmentMeta[ns.environment].chip}`}>{environmentMeta[ns.environment].label}</span>
        </p>
        <p className="text-xs text-[var(--color-text-muted)]">
          {providerLabel[ns.provider]}{ns.lastConnectionTestAt ? ` · checked ${formatAge(ns.lastConnectionTestAt, now)} ago` : ' · not checked yet'}
        </p>
      </div>
      <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${ok ? 'bg-[var(--color-success-light)] text-[#047857]' : ok === false ? 'bg-[var(--color-warning-light)] text-[#92400e]' : 'bg-[var(--color-surface-muted)] text-[var(--color-text-muted)]'}`}>
        {ok ? 'Connected' : ok === false ? 'Could not connect' : 'Not tested yet'}
      </span>
      <button type="button" className={btn} disabled={test.isPending} onClick={() => test.mutate(ns.id)}>{test.isPending ? 'Testing…' : 'Test'}</button>
      {confirming ? (
        <span className="flex items-center gap-2 text-sm">
          Remove {ns.displayName ?? ns.name}? Its recorded history stays in the ledger.
          <button type="button" className="rounded-lg bg-[#dc2626] px-3 py-1.5 font-semibold text-white" disabled={remove.isPending} onClick={() => remove.mutate(ns.id)}>Remove</button>
          <button type="button" className={btn} onClick={() => setConfirming(false)}>Keep</button>
        </span>
      ) : (
        <button type="button" className={btn} disabled={!admin.allowed} aria-label={`Remove ${ns.displayName ?? ns.name}`} onClick={() => setConfirming(true)}><Trash2 className="h-4 w-4" aria-hidden="true" /></button>
      )}
      {remove.isError && <p role="alert" className="w-full text-xs text-[#b91c1c]">{toProblem(remove.error).message}</p>}
    </li>
  )
}

const formatName: Readonly<Record<api.ChannelFormat, string>> = { slack: 'Slack', teams: 'Microsoft Teams', generic: 'Any other system' }

function Notifications({ data, admin }: { data: api.Settings; admin: { allowed: boolean; reason: string | null } }) {
  const [adding, setAdding] = useState<api.ChannelFormat | null>(null)
  return (
    <section id="settings-notifications" aria-label="Notifications">
      <h2 className={h2}>Notifications</h2>
      <p className="mb-3 text-sm text-[var(--color-text-muted)]">Sent only when the Agent stops and needs a person — never for routine activity.</p>
      <ul className="divide-y divide-[var(--color-border)]">
        <li className="flex items-center gap-3 py-3">
          <Bell className="h-5 w-5 text-[var(--color-primary-600)]" aria-hidden="true" />
          <div className="flex-1">
            <p className="font-semibold">In-app bell and pop-up</p>
            <p className="text-xs text-[var(--color-text-muted)]">Always on. It can’t be switched off, so it can never be switched off by mistake.</p>
          </div>
          <span role="switch" aria-checked="true" aria-disabled="true" aria-label="In-app bell is always on" className="flex h-6 w-11 items-center rounded-full bg-[var(--color-primary-600)] px-0.5 opacity-70"><span className="ml-auto h-5 w-5 rounded-full bg-white" /></span>
        </li>
        {data.notifications.serverChannel && (
          <li className="flex items-center gap-3 py-3">
            <Link2 className="h-5 w-5 text-[var(--color-text-muted)]" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">{formatName[data.notifications.serverChannel.format]}</p>
              <p className="text-xs text-[var(--color-text-muted)]">Set by {data.notifications.serverChannel.setBy} — change it there.</p>
            </div>
          </li>
        )}
        {data.notifications.channels.map((c) => <ChannelRow key={c.id} channel={c} admin={admin.allowed} />)}
        {(['slack', 'teams', 'generic'] as const).map((f) => (
          <li key={f} className="flex items-center gap-3 py-3">
            <Link2 className="h-5 w-5 text-[var(--color-text-muted)]" aria-hidden="true" />
            <div className="flex-1">
              <p className="font-semibold">{formatName[f]}</p>
              <p className="text-xs text-[var(--color-text-muted)]">
                {f === 'generic' ? 'A generic webhook — JSON to a URL you choose. Private addresses are refused.' : `${data.notifications.channels.filter((c) => c.format === f).length || 'No'} channel${data.notifications.channels.filter((c) => c.format === f).length === 1 ? '' : 's'} set up`}
              </p>
            </div>
            <button type="button" className="text-sm font-semibold text-[var(--color-primary-700)] hover:underline disabled:opacity-50" disabled={!admin.allowed} onClick={() => setAdding(f)}>+ Add webhook</button>
          </li>
        ))}
      </ul>
      {adding && <AddChannel format={adding} onDone={() => setAdding(null)} />}
      <NotAllowed reason={admin.reason} />
    </section>
  )
}

function ChannelRow({ channel: c, admin }: { channel: api.NotificationChannel; admin: boolean }) {
  const test = useSettingsMutation(() => api.testChannel(c.id))
  const toggle = useSettingsMutation((on: boolean) => api.setChannelEnabled(c.id, on))
  const remove = useSettingsMutation(() => api.removeChannel(c.id))
  const now = new Date()
  return (
    <li className="flex flex-wrap items-center gap-3 py-3">
      <Link2 className="h-5 w-5 text-[var(--color-primary-600)]" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="font-semibold">{formatName[c.format]} · {c.label}</p>
        <p className="text-xs text-[var(--color-text-muted)]">
          {c.lastError ? <span className="text-[#b91c1c]">Last delivery failed: {c.lastError}</span> : c.lastDeliveredAt ? `last delivered ${formatAge(c.lastDeliveredAt, now)} ago` : 'nothing delivered yet'}
        </p>
        {test.data && <p className="text-xs" role="status">{test.data.delivered ? 'Test delivered — check the channel.' : `Test not delivered: ${test.data.error ?? 'unknown reason'}`}</p>}
      </div>
      <button type="button" className={btn} disabled={test.isPending} onClick={() => test.mutate(undefined)}>{test.isPending ? 'Sending…' : 'Send a test'}</button>
      <button type="button" role="switch" aria-checked={c.enabled} aria-label={`${c.label} is ${c.enabled ? 'on' : 'off'}`} disabled={!admin || toggle.isPending}
        onClick={() => toggle.mutate(!c.enabled)} className={`h-6 w-11 rounded-full px-0.5 disabled:opacity-50 ${c.enabled ? 'bg-[var(--color-success)]' : 'bg-[#d1d5db]'}`}>
        <span className={`block h-5 w-5 rounded-full bg-white transition-transform ${c.enabled ? 'translate-x-[20px]' : ''}`} />
      </button>
      <button type="button" className={btn} disabled={!admin || remove.isPending} aria-label={`Remove ${c.label}`} onClick={() => remove.mutate(undefined)}><Trash2 className="h-4 w-4" aria-hidden="true" /></button>
    </li>
  )
}

function AddChannel({ format, onDone }: { format: api.ChannelFormat; onDone: () => void }) {
  const [label, setLabel] = useState('')
  const [url, setUrl] = useState('')
  const add = useSettingsMutation(api.addChannel)
  return (
    <form
      className="mt-3 space-y-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-surface-muted)] p-4"
      onSubmit={(e) => { e.preventDefault(); add.mutate({ format, label, url }, { onSuccess: onDone }) }}
    >
      <p className="font-semibold">Add a {formatName[format]} webhook</p>
      <label className="block text-sm">Name (what you’ll recognise it by)
        <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder={format === 'slack' ? '#ops-alerts' : format === 'teams' ? 'Ops channel' : 'Incident tool'} className="mt-1 w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2" />
      </label>
      <label className="block text-sm">Webhook address
        <input type="password" autoComplete="off" required value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://…" className="mt-1 w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 font-mono" />
        <span className="mt-1 block text-xs text-[var(--color-text-muted)]">Stored encrypted and never shown again. Private and internal addresses are refused when sending.</span>
      </label>
      {add.isError && <p role="alert" className="text-sm text-[#b91c1c]">{toProblem(add.error).message}</p>}
      <div className="flex justify-end gap-2">
        <button type="button" className={btn} onClick={onDone}>Cancel</button>
        <button type="submit" className={primary} disabled={add.isPending || !url.trim()}>Add</button>
      </div>
    </form>
  )
}

function Preferences() {
  const prefs = usePreferences()
  const zones = (() => { try { return (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? [] } catch { return [] } })()
  const seg = (active: boolean) => `flex-1 rounded-lg px-3 py-2 text-sm font-semibold ${active ? 'bg-[var(--color-surface)] shadow' : 'text-[var(--color-text-muted)]'}`
  return (
    <section id="settings-preferences" aria-label="Preferences">
      <h2 className={`${h2} mb-3`}>Preferences</h2>
      <div className="grid gap-4 md:grid-cols-2">
        <div>
          <p className="mb-1 text-sm font-semibold">Theme</p>
          <div className="flex rounded-xl bg-[var(--color-surface-muted)] p-1">
            <span className={seg(true)} aria-current="true">Light</span>
            <span className={`${seg(false)} cursor-not-allowed`} aria-disabled="true" title="Dark mode is not built yet">Dark <span className="text-xs font-normal">soon</span></span>
          </div>
        </div>
        <label className="block text-sm font-semibold">Times shown in
          <select value={prefs.timeZone} onChange={(e) => writePreferences({ timeZone: e.target.value })} className="mt-1 w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2 font-normal">
            <option value="browser">Your browser — {browserTimeZone()}</option>
            {zones.map((z) => <option key={z} value={z}>{z}</option>)}
          </select>
        </label>
        <div>
          <p className="mb-1 text-sm font-semibold">Open on</p>
          <div className="flex rounded-xl bg-[var(--color-surface-muted)] p-1" role="radiogroup" aria-label="Open on">
            <button type="button" role="radio" aria-checked={prefs.openOn === 'simple'} className={seg(prefs.openOn === 'simple')} onClick={() => writePreferences({ openOn: 'simple' })}>Simple</button>
            <button type="button" role="radio" aria-checked={prefs.openOn === 'last'} className={seg(prefs.openOn === 'last')} onClick={() => writePreferences({ openOn: 'last' })}>Last used</button>
          </div>
        </div>
      </div>
      <p className="mt-2 text-xs text-[var(--color-text-muted)]">Kept in this browser only.</p>
    </section>
  )
}

function Access({ data }: { data: api.Settings }) {
  const me = useMe().data
  const isAdmin = permission(me, 'Admin', 'manage roles')
  return (
    <section id="settings-access" aria-label="Access and security" className="space-y-4">
      <h2 className={h2}>Access &amp; security</h2>
      <div className="rounded-xl border border-[var(--color-border)] p-4 text-sm">
        <p>
          You are <b>{me?.actor.label ?? '…'}</b>
          {me?.governanceActive ? <> with the <b>{me.effectiveRole ?? 'no'}</b> role.</> : <> — roles are not switched on, so everyone can do everything.</>}
        </p>
        <p className="mt-1">Credentials are <b>encrypted at rest</b> · key <span className="font-mono">{data.security.keyFingerprint}</span></p>
        <p className="mt-1">API keys configured on this server: <b>{data.security.apiKeysConfigured}</b></p>
      </div>
      <Roles canManage={isAdmin.allowed} reason={isAdmin.reason} governanceActive={!!me?.governanceActive} />
      <EmergencyStopControl stop={data.emergencyStop} />
    </section>
  )
}

function Roles({ canManage, reason, governanceActive }: { canManage: boolean; reason: string | null; governanceActive: boolean }) {
  const grants = useGrants(canManage)
  const namespaces = useNamespaces()
  const grant = useSettingsMutation(api.grantRole)
  const revoke = useSettingsMutation(api.revokeRole)
  const [who, setWho] = useState('')
  const [kind, setKind] = useState<'ApiKey' | 'User'>('ApiKey')
  const [role, setRole] = useState<Role>('Viewer')
  const [ns, setNs] = useState('')
  const nsName = (id: string | null) => (id ? namespaces.data?.find((n) => n.id === id)?.name ?? 'a namespace' : 'every namespace')
  return (
    <div className="rounded-xl border border-[var(--color-border)] p-4 text-sm">
      <p className="font-semibold">Roles</p>
      <p className="text-xs text-[var(--color-text-muted)]">
        Viewer sees · Operator replays · Approver answers the Agent and switches rules on · Admin connects clouds and manages roles.
        {!governanceActive && ' Granting the first role switches roles on; everyone not given a role keeps full access.'}
      </p>
      {canManage && grants.data && grants.data.length > 0 && (
        <ul className="mt-2 divide-y divide-[var(--color-border)]">
          {grants.data.map((g) => (
            <li key={g.id} className="flex items-center gap-2 py-2">
              <span className="min-w-0 flex-1"><b>{g.granteeIdentity}</b> — {g.role} on {nsName(g.namespaceId)}</span>
              <button type="button" className={btn} disabled={revoke.isPending} onClick={() => revoke.mutate(g.id)}>Revoke</button>
            </li>
          ))}
        </ul>
      )}
      {canManage && (
        <form className="mt-3 flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); grant.mutate({ granteeIdentity: who, granteeKind: kind, role, namespaceId: ns || null }, { onSuccess: () => setWho('') }) }}>
          <label className="flex flex-col text-xs">Who
            <input value={who} onChange={(e) => setWho(e.target.value)} required placeholder={kind === 'ApiKey' ? 'API key name' : 'sign-in name'} className="mt-1 rounded-lg border border-[var(--color-border)] px-2 py-1.5 text-sm" />
          </label>
          <label className="flex flex-col text-xs">Kind
            <select value={kind} onChange={(e) => setKind(e.target.value as 'ApiKey' | 'User')} className="mt-1 rounded-lg border border-[var(--color-border)] px-2 py-1.5 text-sm"><option value="ApiKey">API key</option><option value="User">Signed-in user</option></select>
          </label>
          <label className="flex flex-col text-xs">Role
            <select value={role} onChange={(e) => setRole(e.target.value as Role)} className="mt-1 rounded-lg border border-[var(--color-border)] px-2 py-1.5 text-sm">{(['Viewer', 'Operator', 'Approver', 'Admin'] as const).map((r) => <option key={r}>{r}</option>)}</select>
          </label>
          <label className="flex flex-col text-xs">Where
            <select value={ns} onChange={(e) => setNs(e.target.value)} className="mt-1 rounded-lg border border-[var(--color-border)] px-2 py-1.5 text-sm">
              <option value="">Every namespace</option>
              {(namespaces.data ?? []).map((n) => <option key={n.id} value={n.id}>{n.displayName ?? n.name}</option>)}
            </select>
          </label>
          <button type="submit" className={primary} disabled={grant.isPending || !who.trim()}>Grant</button>
        </form>
      )}
      {(grant.isError || revoke.isError) && <p role="alert" className="mt-2 text-[#b91c1c]">{toProblem(grant.error ?? revoke.error).message}</p>}
      <NotAllowed reason={reason} />
      <p className="mt-2 text-xs text-[var(--color-text-muted)]">People and a user directory arrive in a later version; today access is by this server’s sign-in and API keys.</p>
    </div>
  )
}

function EmergencyStopControl({ stop }: { stop: api.EmergencyStop }) {
  const me = useMe().data
  const may = permission(me, 'Admin', stop.active ? 'switch emergency stop off' : 'switch emergency stop on')
  const set = useSettingsMutation(api.setEmergencyStop)
  const [reason, setReason] = useState('')
  const [confirm, setConfirm] = useState('')
  return (
    <div className={`rounded-xl border p-4 text-sm ${stop.active ? 'border-[#fecaca] bg-[#fef2f2]' : 'border-[var(--color-border)]'}`}>
      <p className="flex items-center gap-2 font-semibold"><OctagonAlert className="h-4 w-4 text-[#dc2626]" aria-hidden="true" /> Emergency stop</p>
      {stop.active ? (
        <>
          <p className="mt-1">On since {stop.at ? formatWhen(stop.at, new Date()) : '—'}{stop.by ? `, by ${stop.by}` : ''}{stop.reason ? ` — “${stop.reason}”` : ''}. ServiceHub will not act on its own; replays a person starts still go through their checks.</p>
          <button type="button" className={`${primary} mt-2`} disabled={!may.allowed || set.isPending} onClick={() => set.mutate({ active: false })}>Switch emergency stop off</button>
        </>
      ) : (
        <form className="mt-1 space-y-2" onSubmit={(e) => { e.preventDefault(); set.mutate({ active: true, reason, confirm }, { onSuccess: () => { setReason(''); setConfirm('') } }) }}>
          <p className="text-[var(--color-text-muted)]">Stops every rule and agent acting on its own — at once, for every cloud. ServiceHub keeps watching and recording, and nothing already done is undone.</p>
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why? (recorded with your name)" aria-label="Why" className="w-full rounded-lg border border-[var(--color-border)] px-3 py-2" />
          <input value={confirm} onChange={(e) => setConfirm(e.target.value)} placeholder="Type STOP to confirm" aria-label="Type STOP to confirm" className="w-full rounded-lg border border-[var(--color-border)] px-3 py-2 font-mono" />
          <button type="submit" className="rounded-lg bg-[#dc2626] px-3.5 py-2 text-sm font-semibold text-white disabled:opacity-50" disabled={!may.allowed || set.isPending || !reason.trim() || confirm.trim() !== 'STOP'}>
            <Lock className="mr-1 inline h-3.5 w-3.5" aria-hidden="true" />Switch emergency stop on
          </button>
        </form>
      )}
      {set.isError && <p role="alert" className="mt-2 text-[#b91c1c]">{toProblem(set.error).message}</p>}
      <NotAllowed reason={may.reason} />
    </div>
  )
}

