import type { RuleCondition } from './api/rules';
import type { CloudProviderType, EnvironmentType, Namespace } from './api/types';

// Auto-replay rules carry no namespace/cloud/entity field of their own — they are pure
// condition→action matchers evaluated against DLQ messages from every connected namespace
// (see RuleEngine.cs / AutoReplayRule.cs). This resolver infers the rule's effective target
// from an optional EntityName/TopicName condition, cross-referenced against the entities
// ServiceHub already knows about, so the UI can show real scope instead of implying one that
// doesn't exist in the data model.

export interface NamespaceQueueEntity {
  name: string;
  /** AWS SQS only — RedrivePolicy target queue. Real per-queue data, already fetched for the
   * Namespaces panel; carried through here instead of being discarded. */
  deadLetterTargetQueue?: string | null;
}

export interface NamespaceEntityIndex {
  namespaceId: string;
  namespace: Namespace;
  queues: NamespaceQueueEntity[];
  topics: string[];
}

export interface ScopedEntity {
  namespaceId: string;
  namespaceName: string;
  cloudProvider: CloudProviderType;
  environment?: EnvironmentType;
  entityName: string;
  entityKind: 'queue' | 'topic' | 'subscription';
  topicName?: string;
  /** Short subscription name (without the parent topic prefix) — set only when entityKind === 'subscription'. */
  subscriptionName?: string;
  /**
   * The dead-letter destination, when it can be derived without fabrication:
   * AWS — the queue's own RedrivePolicy target (`deadLetterTargetQueue`), only when the API
   * actually returned one. Azure — the fixed `$DeadLetterQueue` sub-queue path, which is a
   * protocol constant (not per-instance data), so it's always derivable for a resolved
   * queue/subscription. GCP subscriptions' dead-letter *topic* is not fetched anywhere in this
   * app today (see useSubscriptions.ts's Subscription type) — null rather than guessed.
   */
  dlqLabel: string | null;
}

export type RuleScope =
  /** No EntityName/TopicName condition — the rule can match DLQ messages in any connected namespace. */
  | { kind: 'global' }
  /** EntityName/TopicName condition uses a non-exact operator (Contains, StartsWith, Regex, ...) — not resolvable to a specific entity. */
  | { kind: 'pattern'; field: string; operator: string; value: string }
  /** Entity/namespace lists are still loading — avoid rendering a wrong scope while data settles. */
  | { kind: 'loading' }
  /** Exact value matches no known queue/topic/subscription in any connected namespace. */
  | { kind: 'unresolved'; value: string }
  /** Exact value resolved to one (precise) or more (ambiguous — same name across namespaces/clouds) entities. */
  | { kind: 'resolved'; matches: ScopedEntity[] };

function buildDlqLabel(
  cloudProvider: CloudProviderType,
  entityKind: 'queue' | 'topic' | 'subscription',
  entityName: string,
  queueDlq: string | null | undefined,
): string | null {
  if (entityKind === 'topic') return null; // A topic itself never dead-letters — its subscriptions do.

  if (entityKind === 'queue') {
    if (cloudProvider === 'aws') return queueDlq ?? null;
    if (cloudProvider === 'azure') return `${entityName}/$DeadLetterQueue`;
    return null;
  }

  // entityKind === 'subscription'
  if (cloudProvider === 'azure') return `${entityName}/$DeadLetterQueue`;
  return null; // AWS has no subscription concept; GCP's dead-letter topic isn't fetched anywhere today.
}

function findEntity(
  value: string,
  index: NamespaceEntityIndex[],
): ScopedEntity[] {
  const v = value.trim().toLowerCase();
  const matches: ScopedEntity[] = [];

  for (const ns of index) {
    const nsName = ns.namespace.displayName || ns.namespace.name;
    const cloudProvider = ns.namespace.cloudProvider ?? 'azure';
    const environment = ns.namespace.environment;

    const queueHit = ns.queues.find((q) => q.name.toLowerCase() === v);
    if (queueHit) {
      matches.push({
        namespaceId: ns.namespaceId,
        namespaceName: nsName,
        cloudProvider,
        environment,
        entityName: queueHit.name,
        entityKind: 'queue',
        dlqLabel: buildDlqLabel(cloudProvider, 'queue', queueHit.name, queueHit.deadLetterTargetQueue),
      });
      continue;
    }

    const topicHit = ns.topics.find((t) => t.toLowerCase() === v);
    if (topicHit) {
      matches.push({
        namespaceId: ns.namespaceId,
        namespaceName: nsName,
        cloudProvider,
        environment,
        entityName: topicHit,
        entityKind: 'topic',
        dlqLabel: null,
      });
      continue;
    }

    // Azure/GCP subscription entities are recorded as "topic/subscriptions/name" — matches
    // findRuleEntityWarnings' isKnown() convention: scope to the topic when the topic is known.
    const topicPart = v.split('/')[0];
    const parentTopic = ns.topics.find((t) => t.toLowerCase() === topicPart);
    if (parentTopic && v !== topicPart) {
      matches.push({
        namespaceId: ns.namespaceId,
        namespaceName: nsName,
        cloudProvider,
        environment,
        entityName: value,
        entityKind: 'subscription',
        topicName: parentTopic,
        subscriptionName: value.slice(value.lastIndexOf('/') + 1),
        dlqLabel: buildDlqLabel(cloudProvider, 'subscription', value, null),
      });
    }
  }

  return matches;
}

export function resolveRuleScope(
  conditions: RuleCondition[],
  index: NamespaceEntityIndex[],
  loaded: boolean,
): RuleScope {
  const scopeCondition = conditions.find(
    (c) => c.field === 'EntityName' || c.field === 'TopicName',
  );

  if (!scopeCondition) return { kind: 'global' };

  if (scopeCondition.operator !== 'Equals' && scopeCondition.operator !== 'In') {
    return {
      kind: 'pattern',
      field: scopeCondition.field,
      operator: scopeCondition.operator,
      value: scopeCondition.value,
    };
  }

  if (!loaded) return { kind: 'loading' };

  const values =
    scopeCondition.operator === 'In'
      ? scopeCondition.value.split(',').map((v) => v.trim()).filter(Boolean)
      : [scopeCondition.value];

  const matches = values.flatMap((value) => findEntity(value, index));

  if (matches.length === 0) {
    return { kind: 'unresolved', value: values[0] ?? scopeCondition.value };
  }

  return { kind: 'resolved', matches };
}
