import { describe, it, expect } from 'vitest';
import { resolveRuleScope, type NamespaceEntityIndex } from '../../lib/ruleScope';
import type { RuleCondition } from '../../lib/api/rules';
import type { Namespace } from '../../lib/api/types';

function ns(overrides: Partial<Namespace> & { id: string }): Namespace {
  return {
    name: overrides.id,
    isActive: true,
    createdAt: '2024-01-01T00:00:00Z',
    ...overrides,
  };
}

const awsNs: NamespaceEntityIndex = {
  namespaceId: 'ns-aws',
  namespace: ns({ id: 'ns-aws', displayName: 'AWS DEV', cloudProvider: 'aws', environment: 'dev' }),
  queues: ['orders'],
  topics: [],
};

const azureNs: NamespaceEntityIndex = {
  namespaceId: 'ns-azure',
  namespace: ns({ id: 'ns-azure', displayName: 'Azure DEV', cloudProvider: 'azure', environment: 'prod' }),
  queues: [],
  topics: ['orders-topic'],
};

describe('resolveRuleScope', () => {
  it('returns global when there is no EntityName/TopicName condition', () => {
    const conditions: RuleCondition[] = [{ field: 'DeadLetterReason', operator: 'Equals', value: 'Timeout' }];
    expect(resolveRuleScope(conditions, [awsNs], true)).toEqual({ kind: 'global' });
  });

  it('returns pattern for a non-exact operator on EntityName', () => {
    const conditions: RuleCondition[] = [{ field: 'EntityName', operator: 'Contains', value: 'order' }];
    expect(resolveRuleScope(conditions, [awsNs], true)).toEqual({
      kind: 'pattern',
      field: 'EntityName',
      operator: 'Contains',
      value: 'order',
    });
  });

  it('returns loading while entity lists are not yet loaded', () => {
    const conditions: RuleCondition[] = [{ field: 'EntityName', operator: 'Equals', value: 'orders' }];
    expect(resolveRuleScope(conditions, [awsNs], false)).toEqual({ kind: 'loading' });
  });

  it('returns unresolved when the value matches no known entity', () => {
    const conditions: RuleCondition[] = [{ field: 'EntityName', operator: 'Equals', value: 'ghost-queue' }];
    expect(resolveRuleScope(conditions, [awsNs], true)).toEqual({ kind: 'unresolved', value: 'ghost-queue' });
  });

  it('resolves a single match to its namespace/cloud/entity', () => {
    const conditions: RuleCondition[] = [{ field: 'EntityName', operator: 'Equals', value: 'orders' }];
    const result = resolveRuleScope(conditions, [awsNs], true);
    expect(result).toEqual({
      kind: 'resolved',
      matches: [
        {
          namespaceId: 'ns-aws',
          namespaceName: 'AWS DEV',
          cloudProvider: 'aws',
          environment: 'dev',
          entityName: 'orders',
          entityKind: 'queue',
        },
      ],
    });
  });

  it('flags an ambiguous match when the same entity name exists in two namespaces', () => {
    const namesake: NamespaceEntityIndex = {
      namespaceId: 'ns-azure-2',
      namespace: ns({ id: 'ns-azure-2', displayName: 'Azure UAT', cloudProvider: 'azure', environment: 'uat' }),
      queues: ['orders'],
      topics: [],
    };
    const conditions: RuleCondition[] = [{ field: 'EntityName', operator: 'Equals', value: 'orders' }];
    const result = resolveRuleScope(conditions, [awsNs, namesake], true);
    expect(result.kind).toBe('resolved');
    if (result.kind === 'resolved') {
      expect(result.matches).toHaveLength(2);
      expect(result.matches.map((m) => m.namespaceId).sort()).toEqual(['ns-aws', 'ns-azure-2'].sort());
    }
  });

  it('resolves an Azure subscription value ("topic/subscriptions/name") to its parent topic namespace', () => {
    const conditions: RuleCondition[] = [
      { field: 'EntityName', operator: 'Equals', value: 'orders-topic/subscriptions/billing-sub' },
    ];
    const result = resolveRuleScope(conditions, [azureNs], true);
    expect(result).toEqual({
      kind: 'resolved',
      matches: [
        {
          namespaceId: 'ns-azure',
          namespaceName: 'Azure DEV',
          cloudProvider: 'azure',
          environment: 'prod',
          entityName: 'orders-topic/subscriptions/billing-sub',
          entityKind: 'subscription',
          topicName: 'orders-topic',
        },
      ],
    });
  });

  it('resolves each value of an In condition independently', () => {
    const conditions: RuleCondition[] = [{ field: 'EntityName', operator: 'In', value: 'orders, orders-topic' }];
    const result = resolveRuleScope(conditions, [awsNs, azureNs], true);
    expect(result.kind).toBe('resolved');
    if (result.kind === 'resolved') {
      expect(result.matches).toHaveLength(2);
      expect(result.matches.map((m) => m.entityName).sort()).toEqual(['orders', 'orders-topic']);
    }
  });
});
