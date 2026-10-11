import type { Role } from '../../api/identity'
import type { ChannelFormat, EmergencyStop, Grant, NotificationChannel, Settings } from '../../api/settings'
import { audit } from '../world/actions'
import { people } from '../world/seed'
import { emit, save } from '../world/store'
import { notFound, refuse, type Route } from './http'

export const settings: readonly Route[] = [
  ['get', /^\/settings$/, (_m, { w }): Settings => ({
    notifications: { bellAlwaysOn: true, serverChannel: null, channels: w.channels },
    security: { apiKeysConfigured: w.grants.filter((g) => g.granteeKind === 'ApiKey').length, credentialsEncryptedAtRest: true, keyFingerprint: 'demo' },
    emergencyStop: w.emergencyStop,
  })],
  ['post', /^\/settings\/channels$/, (_m, { w, body, now }): NotificationChannel => {
    const label = String(body.label ?? '').trim() || refuse(400, 'VALIDATION_FAILED', 'Give the channel a name.')
    // The address is deliberately not kept: a demo never contacts anything.
    const channel: NotificationChannel = { id: `demo-channel-${w.next.channel++}`, format: (body.format as ChannelFormat) ?? 'generic', label, enabled: true, createdAt: new Date(now).toISOString(), createdBy: people.you.label, lastDeliveredAt: null, lastError: null }
    w.channels = [...w.channels, channel]
    save()
    return channel
  }],
  ['post', /^\/settings\/channels\/([^/]+)\/enabled$/, (m, { w, body }) => {
    const found = w.channels.find((c) => c.id === m[1]) ?? notFound('channel')
    const changed = { ...found, enabled: body.enabled === true }
    w.channels = w.channels.map((c) => (c.id === found.id ? changed : c))
    save()
    return changed
  }],
  ['post', /^\/settings\/channels\/([^/]+)\/test$/, (m, { w }) => {
    if (!w.channels.some((c) => c.id === m[1])) notFound('channel')
    return { delivered: false, error: 'Demo — nothing is sent, so this channel was not contacted.' }
  }],
  ['delete', /^\/settings\/channels\/([^/]+)$/, (m, { w }) => {
    w.channels = w.channels.filter((c) => c.id !== m[1])
    save()
    return null
  }],
  ['get', /^\/settings\/emergency-stop$/, (_m, { w }) => w.emergencyStop],
  ['post', /^\/settings\/emergency-stop$/, (_m, { w, body, now }): EmergencyStop => {
    const active = body.active === true
    const word = active ? 'STOP' : 'LIFT'
    if (!String(body.reason ?? '').trim()) refuse(400, 'VALIDATION_FAILED', 'Say why, so the next person knows.')
    if (body.confirm !== word) refuse(400, 'VALIDATION_FAILED', `Type ${word} to confirm.`)
    w.emergencyStop = active ? { active: true, by: people.you.label, at: new Date(now).toISOString(), reason: String(body.reason) } : { active: false, by: null, at: null, reason: null }
    save()
    emit('EmergencyStopChanged', 'Safety', null)
    return w.emergencyStop
  }],
  ['get', /^\/governance\/grants$/, (_m, { w }) => w.grants],
  ['post', /^\/governance\/grants$/, (_m, { w, body, now }): Grant => {
    const identity = String(body.granteeIdentity ?? '').trim() || refuse(400, 'VALIDATION_FAILED', 'Say who the role is for.')
    const grant: Grant = { id: `demo-grant-${w.next.grant++}`, granteeIdentity: identity, granteeKind: body.granteeKind === 'ApiKey' ? 'ApiKey' : 'User', role: (body.role as Role) ?? 'Viewer', namespaceId: typeof body.namespaceId === 'string' ? body.namespaceId : null, pillarKind: null, grantedAt: new Date(now).toISOString(), grantedByIdentity: people.you.identity }
    w.grants = [...w.grants, grant]
    audit(w, people.you, 'Governance.Grant', grant.namespaceId, identity, now)
    save()
    return grant
  }],
  ['post', /^\/governance\/grants\/([^/]+)\/revoke$/, (m, { w, now }) => {
    const grant = w.grants.find((g) => g.id === m[1]) ?? notFound('role')
    if (grant.granteeIdentity === people.you.identity) refuse(409, 'WOULD_LOCK_OUT', 'That is your own role. Removing it would lock you out.')
    w.grants = w.grants.filter((g) => g.id !== grant.id)
    audit(w, people.you, 'Governance.Revoke', grant.namespaceId, grant.granteeIdentity, now)
    save()
    return null
  }],
]
