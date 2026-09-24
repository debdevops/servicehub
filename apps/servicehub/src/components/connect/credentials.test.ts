import { describe, expect, it } from 'vitest'
import type { NamespaceStats, ProviderCapabilities } from '../../lib/api/namespaces'
import { azureNamespaceName, buildConnectInput, describeFound, describeProof, emptyForm } from './credentials'

const azureCs = 'Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=abc'
const filled = (over: Partial<ReturnType<typeof emptyForm>>) => ({ ...emptyForm('azure'), displayName: 'Orders', ...over })

describe('buildConnectInput', () => {
  it('needs a name first', () => {
    const r = buildConnectInput({ ...emptyForm('azure'), connectionString: azureCs })
    expect(r).toMatchObject({ ok: false })
  })

  it('builds Azure from the connection string alone, naming the namespace from its endpoint', () => {
    const r = buildConnectInput(filled({ connectionString: ` ${azureCs} `, environment: 'uat' }))
    expect(r).toEqual({
      ok: true,
      input: {
        provider: 'azure',
        authType: 'connectionString',
        name: 'orders-dev',
        connectionString: azureCs,
        displayName: 'Orders',
        environment: 'uat',
      },
    })
  })

  it('refuses a string that is not a Service Bus connection string, in plain words', () => {
    const r = buildConnectInput(filled({ connectionString: 'not a connection string' }))
    expect(r).toMatchObject({ ok: false })
    expect((r as { problem: string }).problem).toMatch(/Endpoint=sb:\/\//)
  })

  it('builds AWS with the region kept out of the secret', () => {
    const r = buildConnectInput({
      ...emptyForm('aws'), displayName: 'Prod', awsAccessKeyId: 'AKIA1', awsSecretAccessKey: 'sekret', awsRegion: 'eu-west-2',
    })
    expect(r).toMatchObject({
      ok: true,
      input: { provider: 'aws', authType: 'awsAccessKey', name: 'sqs.eu-west-2.amazonaws.com', connectionString: 'AKIA1:sekret', awsRegion: 'eu-west-2' },
    })
    expect((r as { input: { connectionString: string } }).input.connectionString).not.toContain('eu-west-2')
  })

  it('needs all three AWS fields', () => {
    expect(buildConnectInput({ ...emptyForm('aws'), displayName: 'x', awsAccessKeyId: 'a', awsSecretAccessKey: 'b' })).toMatchObject({ ok: false })
  })

  it('builds GCP from a project id and a key', () => {
    const r = buildConnectInput({ ...emptyForm('gcp'), displayName: 'G', gcpProjectId: 'my-proj-123', gcpServiceAccountJson: '{"a":1}' })
    expect(r).toMatchObject({ ok: true, input: { provider: 'gcp', authType: 'gcpServiceAccount', name: 'my-proj-123', gcpProjectId: 'my-proj-123' } })
  })

  it('needs both GCP fields', () => {
    expect(buildConnectInput({ ...emptyForm('gcp'), displayName: 'G', gcpProjectId: 'p' })).toMatchObject({ ok: false })
  })
})

describe('azureNamespaceName', () => {
  it('reads the namespace and tolerates case', () => {
    expect(azureNamespaceName('endpoint=SB://Foo-Bar.servicebus.windows.net/')).toBe('Foo-Bar')
    expect(azureNamespaceName('nothing')).toBeNull()
  })
})

const caps = (canProveDlqAbsence: boolean) => ({ canProveDlqAbsence }) as ProviderCapabilities

describe('describeProof', () => {
  it('follows the capability, never the provider', () => {
    expect(describeProof(caps(true))).toMatch(/can say Verified/)
    expect(describeProof(caps(false))).toMatch(/can't yet prove/)
    expect(describeProof(null)).toMatch(/no adapter/)
  })
})

describe('describeFound', () => {
  const stats = (over: Partial<NamespaceStats>): NamespaceStats => ({
    namespaceId: 'n', entities: [], activeMessages: 5, deadLetterMessages: 96, messageCountsSupported: true, observedAt: 'now', ...over,
  })

  it('says what was found and how many are dead-lettered', () => {
    const text = describeFound(stats({ entities: [{ kind: 'queue', count: 7 }, { kind: 'topic', count: 4 }, { kind: 'subscription', count: 9 }] }))
    expect(text).toBe('Found 7 queues, 4 topics and 9 subscriptions. 96 messages are dead-lettered right now.')
  })

  it('is singular for one', () => {
    expect(describeFound(stats({ entities: [{ kind: 'queue', count: 1 }], deadLetterMessages: 1 }))).toBe(
      'Found 1 queue. 1 message is dead-lettered right now.',
    )
  })

  it('never turns "cannot count" into zero', () => {
    const text = describeFound(stats({ entities: [{ kind: 'topic', count: 2 }], deadLetterMessages: null, messageCountsSupported: false }))
    expect(text).toContain('does not report message counts')
    expect(text).not.toMatch(/dead-lettered right now/)
  })

  it('says so when nothing was found', () => {
    expect(describeFound(stats({}))).toMatch(/no queues or topics/)
  })
})
