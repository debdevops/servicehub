import { api } from './client'
import { Intent, withIntent } from './intentHeaders'
import type { CloudKind } from './coverage'

/**
 * Add a component (5.0.0, docs-private/future/PLAN-ADD-A-COMPONENT-5.0.0.md). What can be brought in on one connection, a read-only access
 * check, and ticking. A connection's credential is never in any of these shapes.
 */
export type ComponentAccess = 'sameConnection' | 'ownCredential' | 'notBuildable' | 'included'

/** `alwaysOn`: watched on every connection and cannot be unticked (removing the connection removes it). */
export type ComponentState = 'on' | 'off' | 'alwaysOn' | 'included' | 'unavailable'

export interface SetupSnippet {
  readonly label: string
  readonly text: string
}

export interface ComponentEntry {
  readonly componentKey: string
  readonly name: string
  readonly stuckPlace: string
  readonly status: 'supported' | 'beingValidated' | 'onRequest' | 'notSupported'
  readonly statusWords: string
  readonly access: ComponentAccess
  readonly state: ComponentState
  readonly canToggle: boolean
  readonly note: string | null
  readonly what: string | null
  readonly appears: string | null
  readonly needs: string | null
  readonly steps: readonly string[]
  readonly snippets: readonly SetupSnippet[]
  readonly permissions: readonly string[]
  readonly includedIn: string | null
}

export interface ComponentList {
  readonly namespaceId: string
  readonly connectionName: string
  readonly cloud: string
  readonly components: readonly ComponentEntry[]
}

export interface ComponentCheck {
  readonly componentKey: string
  readonly ok: boolean
  /** Names what is missing when `ok` is false. */
  readonly reason: string | null
  /** The rights the check tests. */
  readonly permissions: readonly string[]
}

export interface EnabledComponent {
  readonly namespaceId: string
  readonly cloud: CloudKind
  readonly componentKey: string
}

export async function fetchComponentList(namespaceId: string): Promise<ComponentList> {
  return (await api.get<ComponentList>(`/namespaces/${namespaceId}/components`)).data
}

/** Read-only: list and describe calls only. Saves nothing. */
export async function checkComponent(namespaceId: string, componentKey: string): Promise<ComponentCheck> {
  return (await api.post<ComponentCheck>(`/namespaces/${namespaceId}/enabled-components/${componentKey}/check`)).data
}

/** Ticks or unticks. Ticking checks access first on the server and saves nothing when the check fails (the problem's message says why). */
export async function setComponent(namespaceId: string, componentKey: string, enabled: boolean): Promise<ComponentEntry> {
  return (await api.put<ComponentEntry>(`/namespaces/${namespaceId}/enabled-components/${componentKey}`, { enabled }, { headers: withIntent(Intent.ManageComponents) })).data
}

export async function fetchEnabledComponents(): Promise<readonly EnabledComponent[]> {
  return (await api.get<readonly EnabledComponent[]>('/namespaces/components/enabled')).data
}
