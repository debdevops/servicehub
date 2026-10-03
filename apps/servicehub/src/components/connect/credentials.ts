import type {
  CloudProvider,
  ConnectNamespaceInput,
  EnvironmentKind,
  NamespaceStats,
  ProviderCapabilities,
} from '../../lib/api/namespaces'

/**
 * What the Add a cloud form collects — exactly the fields 4.0.0 asked for, nothing the provider
 * does not need. One credential per cloud.
 */
export interface CloudForm {
  readonly cloud: CloudProvider
  readonly displayName: string
  readonly environment: EnvironmentKind
  /** Azure: the Service Bus connection string. */
  readonly connectionString: string
  /** AWS. */
  readonly awsAccessKeyId: string
  readonly awsSecretAccessKey: string
  readonly awsRegion: string
  /** GCP. */
  readonly gcpProjectId: string
  readonly gcpServiceAccountJson: string
}

export const emptyForm = (cloud: CloudProvider): CloudForm => ({
  cloud,
  displayName: '',
  environment: 'dev',
  connectionString: '',
  awsAccessKeyId: '',
  awsSecretAccessKey: '',
  awsRegion: '',
  gcpProjectId: '',
  gcpServiceAccountJson: '',
})

export type BuiltInput = { readonly ok: true; readonly input: ConnectNamespaceInput } | { readonly ok: false; readonly problem: string }

/** The Service Bus namespace named in `Endpoint=sb://<name>.servicebus.windows.net/`. */
export function azureNamespaceName(connectionString: string): string | null {
  return /Endpoint=sb:\/\/([^.]+)\.servicebus\./i.exec(connectionString)?.[1] ?? null
}

/**
 * Turns the form into what the API takes, or says in one plain sentence what is missing.
 * The API validates again; this only spares a round trip for the obvious mistakes.
 */
export function buildConnectInput(form: CloudForm): BuiltInput {
  const displayName = form.displayName.trim()
  if (displayName === '') return { ok: false, problem: 'Give this cloud a name so you can tell it apart later.' }
  const common = { displayName, environment: form.environment }

  switch (form.cloud) {
    case 'azure': {
      const connectionString = form.connectionString.trim()
      if (connectionString === '') return { ok: false, problem: 'Paste the connection string for your Service Bus namespace.' }
      const name = azureNamespaceName(connectionString)
      if (name === null) {
        return {
          ok: false,
          problem: 'That does not look like a Service Bus connection string — it should contain Endpoint=sb://your-namespace.servicebus.windows.net/.',
        }
      }
      return { ok: true, input: { ...common, provider: 'azure', authType: 'connectionString', name, connectionString } }
    }
    case 'aws': {
      const key = form.awsAccessKeyId.trim()
      const secret = form.awsSecretAccessKey.trim()
      const region = form.awsRegion.trim()
      if (key === '' || secret === '' || region === '') {
        return { ok: false, problem: 'AWS needs an access key ID, a secret access key and a region.' }
      }
      return {
        ok: true,
        input: {
          ...common,
          provider: 'aws',
          authType: 'awsAccessKey',
          name: `sqs.${region}.amazonaws.com`,
          // The pair travels as one credential; the region is kept apart so it never sits inside a secret.
          connectionString: `${key}:${secret}`,
          awsRegion: region,
        },
      }
    }
    case 'gcp': {
      const projectId = form.gcpProjectId.trim()
      const key = form.gcpServiceAccountJson.trim()
      if (projectId === '' || key === '') {
        return { ok: false, problem: 'Google Cloud needs a project ID and a service-account key file.' }
      }
      return {
        ok: true,
        input: { ...common, provider: 'gcp', authType: 'gcpServiceAccount', name: projectId, connectionString: key, gcpProjectId: projectId },
      }
    }
  }
}

/** What this cloud can prove, in words — read from capabilities, never from the provider's name (R4). */
export function describeProof(capabilities: ProviderCapabilities | null): string {
  if (capabilities === null) return 'This build of ServiceHub has no adapter for this cloud, so it cannot say what it can prove.'
  return capabilities.canProveDlqAbsence
    ? 'This cloud can prove a replayed message stayed fixed, so ServiceHub can say Verified here.'
    : "ServiceHub can replay here but can't yet prove a fix held — it will say so honestly, and tell you how to unlock it."
}

/** What was found, in words. Message totals are null when the cloud cannot count — said, never shown as 0 (R5). */
export function describeFound(stats: NamespaceStats): string {
  const plural = (n: number, one: string) => `${n} ${one}${n === 1 ? '' : 's'}`
  const kinds = stats.entities
    .filter((e) => e.count > 0)
    .map((e) => plural(e.count, e.kind))
  const found = kinds.length > 0 ? `Found ${joinWords(kinds)}.` : 'Connected, but no queues or topics were found yet.'
  if (!stats.messageCountsSupported || stats.deadLetterMessages === null) {
    return `${found} This cloud does not report message counts.`
  }
  return `${found} ${plural(stats.deadLetterMessages, 'message')} ${stats.deadLetterMessages === 1 ? 'is' : 'are'} dead-lettered right now.`
}

function joinWords(items: readonly string[]): string {
  if (items.length <= 1) return items.join('')
  return `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`
}
