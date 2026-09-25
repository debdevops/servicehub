import { describe, expect, it } from 'vitest'
import { describeEntity, subscriptionParts } from './entities'

describe('describeEntity', () => {
  it('names a queue by itself', () => {
    expect(describeEntity('orders', 'queue')).toEqual({ kind: 'queue', name: 'orders', topic: null })
  })

  it('reads Azure’s topic/subscriptions/sub', () => {
    expect(describeEntity('orders-topic/subscriptions/billing', 'subscription', 'orders-topic')).toEqual({ kind: 'subscription', topic: 'orders-topic', name: 'billing' })
  })

  it('reads Google’s and AWS’s topic/sub', () => {
    expect(describeEntity('orders-topic/orders-subscription', 'subscription')).toEqual({ kind: 'subscription', topic: 'orders-topic', name: 'orders-subscription' })
  })

  it('takes the topic from the record when the stored name has none', () => {
    expect(describeEntity('billing', 'subscription', 'orders-topic')).toEqual({ kind: 'subscription', topic: 'orders-topic', name: 'billing' })
  })
})

describe('subscriptionParts', () => {
  it('gives a peek the topic and the subscription, on every cloud’s spelling', () => {
    expect(subscriptionParts('orders-topic/subscriptions/billing')).toEqual({ entity: 'orders-topic', subscription: 'billing' })
    expect(subscriptionParts('orders-topic/billing')).toEqual({ entity: 'orders-topic', subscription: 'billing' })
    expect(subscriptionParts('orders')).toEqual({ entity: 'orders', subscription: undefined })
  })
})
