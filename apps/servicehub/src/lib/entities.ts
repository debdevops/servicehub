/**
 * How a place messages can be stuck is named on screen.
 *
 * A dead letter lives either in a QUEUE, or under a TOPIC's SUBSCRIPTION (a topic itself holds nothing; each subscription has its
 * own queue). Every cloud has both, so every screen names them the same way. The stored name differs by cloud
 * (`topic/subscriptions/sub` on Azure, `topic/sub` on Google and AWS) — parsed here, once.
 */
export interface EntityLabel {
  readonly kind: 'queue' | 'subscription'
  /** The queue, or the subscription. */
  readonly name: string
  /** The topic, for a subscription. */
  readonly topic: string | null
}

const SUB = '/subscriptions/'

export function describeEntity(entityName: string, entityType: string, topicName?: string | null): EntityLabel {
  const isSub = entityType.toLowerCase() === 'subscription' || topicName != null
  if (!isSub) return { kind: 'queue', name: entityName, topic: null }
  const i = entityName.indexOf(SUB)
  if (i >= 0) return { kind: 'subscription', topic: topicName ?? entityName.slice(0, i), name: entityName.slice(i + SUB.length) }
  const slash = entityName.indexOf('/')
  if (slash >= 0) return { kind: 'subscription', topic: topicName ?? entityName.slice(0, slash), name: entityName.slice(slash + 1) }
  return { kind: 'subscription', topic: topicName ?? null, name: entityName }
}

/** The two things a peek needs to look inside a subscription: its topic, and its own name. */
export function subscriptionParts(entityName: string): { entity: string; subscription: string | undefined } {
  const label = describeEntity(entityName, entityName.includes('/') ? 'subscription' : 'queue')
  return label.kind === 'subscription' && label.topic ? { entity: label.topic, subscription: label.name } : { entity: entityName, subscription: undefined }
}

export const kindWord = (kind: EntityLabel['kind']): string => (kind === 'queue' ? 'Queue' : 'Subscription')
