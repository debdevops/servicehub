import type { RuleCondition, RuleAction } from './api/rules';

// Cross-checks a rule's entity references against the entities that actually
// exist in the connected namespaces. Rules pointing at deleted or misspelled
// entities never match (or fail on every replay), which reads as a broken app
// to end users — surface that before the rule is created and on the rule card.

export interface KnownEntities {
  queues: string[];
  topics: string[];
  /** False while entity lists are still loading — validation stays silent then. */
  loaded: boolean;
}

function isKnown(value: string, queueSet: Set<string>, topicSet: Set<string>): boolean {
  const v = value.trim().toLowerCase();
  if (!v) return true;
  if (queueSet.has(v) || topicSet.has(v)) return true;
  // Azure subscription entities are recorded as "topic/subscriptions/name" —
  // accept any value scoped under a known topic.
  const topicPart = v.split('/')[0];
  return topicSet.has(topicPart);
}

export function findRuleEntityWarnings(
  conditions: RuleCondition[],
  action: RuleAction,
  known: KnownEntities,
): string[] {
  if (!known.loaded) return [];

  const queueSet = new Set(known.queues.map((q) => q.toLowerCase()));
  const topicSet = new Set(known.topics.map((t) => t.toLowerCase()));
  const warnings: string[] = [];

  for (const condition of conditions) {
    if (condition.field !== 'EntityName' && condition.field !== 'TopicName') continue;
    if (condition.operator !== 'Equals' && condition.operator !== 'In') continue;

    const values =
      condition.operator === 'In'
        ? condition.value.split(',').map((v) => v.trim()).filter(Boolean)
        : [condition.value];

    for (const value of values) {
      if (!isKnown(value, queueSet, topicSet)) {
        warnings.push(
          `Entity "${value}" was not found in any connected namespace — this rule will never match.`,
        );
      }
    }
  }

  if (action.targetEntity && !isKnown(action.targetEntity, queueSet, topicSet)) {
    warnings.push(
      `Replay target "${action.targetEntity}" was not found in any connected namespace — replays will fail.`,
    );
  }

  return warnings;
}
