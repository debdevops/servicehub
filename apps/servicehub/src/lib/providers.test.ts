import { describe, expect, it } from 'vitest'
import type { CloudProvider, Namespace } from './api/namespaces'
import { connectedProviders } from './providers'

const ns = (provider: CloudProvider, lastConnectionTestSucceeded: boolean | null = null): Namespace =>
  ({ id: `${provider}-${Math.random()}`, provider, lastConnectionTestSucceeded }) as Namespace

describe('connectedProviders', () => {
  it('lists nothing when nothing is connected', () => {
    expect(connectedProviders([])).toEqual([])
  })

  it('lists only providers that have a namespace — never an unconnected one', () => {
    expect(connectedProviders([ns('azure'), ns('azure')]).map((p) => p.provider)).toEqual(['azure'])
  })

  it('counts namespaces per provider and keeps a fixed order', () => {
    const result = connectedProviders([ns('gcp'), ns('aws'), ns('azure'), ns('aws')])
    expect(result.map((p) => [p.provider, p.namespaceCount])).toEqual([
      ['azure', 1],
      ['aws', 2],
      ['gcp', 1],
    ])
  })

  it('flags attention only when every namespace of that provider failed its last test', () => {
    const result = connectedProviders([ns('azure', false), ns('azure', true), ns('aws', false), ns('gcp', null)])
    expect(result.map((p) => [p.provider, p.needsAttention])).toEqual([
      ['azure', false],
      ['aws', true],
      ['gcp', false],
    ])
  })
})
