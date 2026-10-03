import { api } from './client'
import { Intent, withIntent } from './intentHeaders'
import type { Role } from './identity'

export type ChannelFormat = 'slack' | 'teams' | 'generic'

export interface NotificationChannel {
  readonly id: string
  readonly format: ChannelFormat
  readonly label: string
  readonly enabled: boolean
  readonly createdAt: string
  readonly createdBy: string
  readonly lastDeliveredAt: string | null
  readonly lastError: string | null
}

export interface EmergencyStop {
  readonly active: boolean
  readonly by: string | null
  readonly at: string | null
  readonly reason: string | null
}

export interface Settings {
  readonly notifications: {
    readonly bellAlwaysOn: true
    readonly serverChannel: { readonly format: ChannelFormat; readonly setBy: string } | null
    readonly channels: readonly NotificationChannel[]
  }
  readonly security: { readonly apiKeysConfigured: number; readonly credentialsEncryptedAtRest: boolean; readonly keyFingerprint: string }
  readonly emergencyStop: EmergencyStop
}

export async function fetchSettings(): Promise<Settings> {
  return (await api.get<Settings>('/settings')).data
}

export async function addChannel(input: { format: ChannelFormat; label: string; url: string }): Promise<NotificationChannel> {
  return (await api.post<NotificationChannel>('/settings/channels', input, { headers: withIntent(Intent.AddChannel) })).data
}

export async function setChannelEnabled(id: string, enabled: boolean): Promise<NotificationChannel> {
  return (await api.post<NotificationChannel>(`/settings/channels/${id}/enabled`, { enabled })).data
}

export async function removeChannel(id: string): Promise<void> {
  await api.delete(`/settings/channels/${id}`, { headers: withIntent(Intent.RemoveChannel) })
}

export async function testChannel(id: string): Promise<{ delivered: boolean; error: string | null }> {
  return (await api.post<{ delivered: boolean; error: string | null }>(`/settings/channels/${id}/test`)).data
}

export async function fetchEmergencyStop(): Promise<EmergencyStop> {
  return (await api.get<EmergencyStop>('/settings/emergency-stop')).data
}

export async function setEmergencyStop(input: { active: boolean; reason?: string; confirm?: string }): Promise<EmergencyStop> {
  return (await api.post<EmergencyStop>('/settings/emergency-stop', input, { headers: withIntent(Intent.EmergencyStop) })).data
}

export interface Grant {
  readonly id: string
  readonly granteeIdentity: string
  readonly granteeKind: 'User' | 'ApiKey'
  readonly role: Role
  readonly namespaceId: string | null
  readonly pillarKind: string | null
  readonly grantedAt: string
  readonly grantedByIdentity: string
}

export async function fetchGrants(): Promise<Grant[]> {
  return (await api.get<Grant[]>('/governance/grants')).data
}

export async function grantRole(input: { granteeIdentity: string; granteeKind: 'User' | 'ApiKey'; role: Role; namespaceId: string | null }): Promise<Grant> {
  return (await api.post<Grant>('/governance/grants', { ...input, pillarKind: null }, { headers: withIntent(Intent.GrantRole) })).data
}

export async function revokeRole(id: string): Promise<void> {
  await api.post(`/governance/grants/${id}/revoke`, undefined, { headers: withIntent(Intent.RevokeRole) })
}
