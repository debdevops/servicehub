import { describe, expect, it } from 'vitest'
import { traitsOf, viewKindOf } from './traits'
import type { Namespace, ProviderCapabilities } from '../api/namespaces'
import type { FleetCloud } from '../api/fleet'

const caps = (over: Partial<ProviderCapabilities> = {}): ProviderCapabilities => ({
  supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true,
  supportsRepeatablePeek: true, supportsRecoveryMarker: true, canProveDlqAbsence: true, supportsTopics: true, supportsSubscriptions: true,
  notes: '', ...over,
})
const ns = (provider: Namespace['provider'], capabilities: ProviderCapabilities | null): Namespace =>
  ({ id: `${provider}-1`, name: `${provider}-1`, provider, capabilities } as Namespace)
const cloud = (over: Partial<FleetCloud> = {}): FleetCloud => ({ provider: 'aws', namespaceCount: 1, capability: 'observerRequired', watched: false, active: null, newInWindow: 0, resolvedInWindow: 0, ...over })

// The three real presets (services/api .../ProviderCapabilities.cs) — the plan's rule 2 test (R4):
// what renders is decided by THESE, never by the string "azure" / "aws" / "gcp".
const azureCaps = caps()
const awsCaps = caps({ supportsRepeatablePeek: false, canProveDlqAbsence: false, supportsScheduledMessages: false, supportsPurge: true })
const gcpCaps = caps({ supportsMessageCounts: false, supportsManualDeadLetter: false, supportsRepeatablePeek: false, canProveDlqAbsence: false, supportsScheduledMessages: false, supportsPurge: true })

describe('traitsOf', () => {
  it('reads Azure’s preset as watched, counting and confirming', () => {
    const traits = traitsOf([ns('azure', azureCaps)])
    expect(traits).toEqual({ counts: true, watched: true, confirms: true, scheduled: true, listsSubscriptionsOnly: false })
    expect(viewKindOf(traits)).toBe('watched')
  })

  it('reads AWS’s preset as counting but not watched and not confirming', () => {
    const traits = traitsOf([ns('aws', awsCaps)])
    expect(traits).toEqual({ counts: true, watched: false, confirms: false, scheduled: false, listsSubscriptionsOnly: false })
    expect(viewKindOf(traits)).toBe('recordedWithCounts')
  })

  it('reads Google Cloud’s preset as not counting at all, and lists subscriptions instead', () => {
    const traits = traitsOf([ns('gcp', gcpCaps)])
    expect(traits.counts).toBe(false)
    expect(traits.listsSubscriptionsOnly).toBe(true)
    expect(viewKindOf(traits)).toBe('recordedNoCounts')
  })

  it('R4: a namespace labelled "azure" with AWS’s capabilities reads as AWS-shaped, and the reverse', () => {
    // The whole point of D48 — layout is decided by capability, never by the provider's name.
    const azureNamedButAws = traitsOf([ns('azure', awsCaps)])
    expect(azureNamedButAws.watched).toBe(false)
    expect(azureNamedButAws.confirms).toBe(false)

    const awsNamedButAzure = traitsOf([ns('aws', azureCaps)])
    expect(awsNamedButAzure.watched).toBe(true)
    expect(awsNamedButAzure.confirms).toBe(true)
  })

  it('a mixed scope reads as the more limited case, never the more capable one', () => {
    const traits = traitsOf([ns('azure', azureCaps), ns('azure', awsCaps)])
    expect(traits.watched).toBe(false)
    expect(traits.confirms).toBe(false)
  })

  it('an empty scope, or a namespace with no capabilities yet, reads as the limited case — never crashes', () => {
    expect(traitsOf([]).watched).toBe(false)
    expect(traitsOf([ns('azure', null)]).watched).toBe(false)
  })

  it('"confirms" prefers the server-computed FleetCloud.capability (the DLQ-observer attestation) over the static preset', () => {
    // The static preset can never say AWS confirms; the live fleet data can, once the observer is attested.
    const attested = traitsOf([ns('aws', awsCaps)], cloud({ provider: 'aws', capability: 'canConfirm' }))
    expect(attested.confirms).toBe(true)

    const notAttested = traitsOf([ns('aws', awsCaps)], cloud({ provider: 'aws', capability: 'observerRequired' }))
    expect(notAttested.confirms).toBe(false)
  })
})
