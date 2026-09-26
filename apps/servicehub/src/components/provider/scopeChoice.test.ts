import { describe, expect, it } from 'vitest'
import { groupByCloudEnvironment, namespaceTag } from './scopeChoice'
import type { Namespace } from '../../lib/api/namespaces'

const ns = (id: string, provider: 'azure' | 'aws' | 'gcp', environment: 'dev' | 'uat' | 'prod') => ({ id, provider, environment })

describe('groupByCloudEnvironment', () => {
  it('orders clouds Azure, AWS, Google and environments Production first, keeping row order inside a group', () => {
    const tree = groupByCloudEnvironment([ns('g1', 'gcp', 'dev'), ns('a1', 'azure', 'dev'), ns('a2', 'azure', 'prod'), ns('a3', 'azure', 'dev'), ns('w1', 'aws', 'uat')])

    expect(tree.map((c) => c.provider)).toEqual(['azure', 'aws', 'gcp'])
    expect(tree[0].environments.map((e) => e.env)).toEqual(['prod', 'dev'])
    expect(tree[0].environments[1].items.map((i) => i.id)).toEqual(['a1', 'a3'])
  })

  it('leaves out clouds and environments that hold nothing', () => {
    const tree = groupByCloudEnvironment([ns('w1', 'aws', 'dev')])

    expect(tree).toHaveLength(1)
    expect(tree[0].environments).toHaveLength(1)
  })
})

describe('namespaceTag', () => {
  it('names a namespace with its environment, preferring the display name', () => {
    expect(namespaceTag({ name: 'orders-bus', displayName: 'Orders', environment: 'prod' } as Namespace)).toBe('Orders · Production')
    expect(namespaceTag({ name: 'orders-bus', displayName: null, environment: 'uat' } as Namespace)).toBe('orders-bus · UAT')
  })
})
