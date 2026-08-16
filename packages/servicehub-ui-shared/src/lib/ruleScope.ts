import type { RuleCondition } from './api/rules';
import type { CloudProviderType, EnvironmentType, Namespace } from './api/types';

// Auto-replay rules carry no namespace/cloud/entity field of their own — they are pure
// condition→action matchers evaluated against DLQ messages from every connected namespace
// (see RuleEngine.cs / AutoReplayRule.cs). This resolver infers the rule's effective target
// from an optional EntityName/TopicName condition, cross-referenced against the entities
// ServiceHub already knows about, so the UI can show real scope instead of implying one that
// doesn't exist in the data model.

export interface NamespaceEntityIndex {
  namespaceId: string;
  namespace: Namespace;
  queues: string[];
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

    const queueHit = ns.queues.find((q) => q.toLowerCase() === v);
    if (queueHit) {
      matches.push({
        namespaceId: ns.namespaceId,
        namespaceName: nsName,
        cloudProvider,
        environment,
        entityName: queueHit,
        entityKind: 'queue',
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
      });
      continue;
    }

    // Azure subscription entities are recorded as "topic/subscriptions/name" — matches
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
