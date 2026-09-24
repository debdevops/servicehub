import type { CloudProvider, Namespace } from './api/namespaces'

/** How a provider is named on screen. Presentation only — what a provider CAN do comes from capabilities (R4). */
export const providerLabel: Readonly<Record<CloudProvider, string>> = {
  azure: 'Azure',
  aws: 'AWS',
  gcp: 'Google Cloud',
}

/** A fixed order, so the sidebar does not reshuffle depending on which namespace was added first. */
const providerOrder: readonly CloudProvider[] = ['azure', 'aws', 'gcp']

export interface ConnectedProvider {
  readonly provider: CloudProvider
  readonly label: string
  readonly namespaceCount: number
  /** True only when every namespace's last connection test failed. Untested counts as not failed. */
  readonly needsAttention: boolean
}

/**
 * The Cloud Providers section — derived, never configured (IA §3):
 * `providers = distinct(namespaces.map(n => n.provider))`. A provider with no namespace is not
 * returned, so it cannot be rendered greyed out.
 */
export function connectedProviders(namespaces: readonly Namespace[]): readonly ConnectedProvider[] {
  return providerOrder.flatMap((provider) => {
    const mine = namespaces.filter((n) => n.provider === provider)
    if (mine.length === 0) return []
    return [
      {
        provider,
        label: providerLabel[provider],
        namespaceCount: mine.length,
        needsAttention: mine.every((n) => n.lastConnectionTestSucceeded === false),
      },
    ]
  })
}

/** The messaging service each cloud is known for — used in sentences, never to decide behaviour (R4). */
export const providerService: Readonly<Record<CloudProvider, string>> = {
  azure: 'Azure Service Bus',
  aws: 'AWS SQS / SNS',
  gcp: 'Google Pub/Sub',
}

/** What "the scope" of a cloud is called, and what this installation actually knows of it. */
export interface ScopeChip {
  readonly label: string
  readonly value: string
}

/**
 * The scope chips on a cloud's Home. The label is the cloud's own word for the thing (Azure has
 * namespaces, AWS has regions, Google has projects) and the value is only ever what the namespace
 * record holds — a Subscription or an Account is not stored, so it is not shown (R5).
 */
export function scopeChips(provider: CloudProvider, namespaces: readonly Namespace[]): readonly ScopeChip[] {
  const distinct = (values: readonly (string | null)[]) => [...new Set(values.filter((v): v is string => !!v))]
  const chip = (label: string, values: readonly string[]): readonly ScopeChip[] =>
    values.length === 0 ? [] : [{ label: values.length > 1 ? `${label}s` : label, value: values.join(', ') }]

  switch (provider) {
    case 'azure':
      return chip('Namespace', distinct(namespaces.map((n) => n.displayName ?? n.name)))
    case 'aws':
      return chip('Region', distinct(namespaces.map((n) => n.awsRegion)))
    case 'gcp':
      return chip('Project', distinct(namespaces.map((n) => n.gcpProjectId)))
  }
}
