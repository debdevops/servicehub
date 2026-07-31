import { describe, it, expect } from 'vitest';
import {
  getMockNamespaces,
  getMockQueues,
  getMockTopics,
  getMockSubscriptions,
  getMockMessages,
  getMockStats,
  getMockCapabilities,
  getMockCapabilitiesMap,
  DEMO_NAMESPACE_IDS,
} from '../../../lib/demo/mockProviders';

const PROVIDERS = ['azure', 'aws', 'gcp'] as const;

describe('mockProviders', () => {
  describe('DEMO_NAMESPACE_IDS', () => {
    it('has stable IDs for all three providers', () => {
      expect(DEMO_NAMESPACE_IDS.azure).toBe('demo-azure-contoso-prod');
      expect(DEMO_NAMESPACE_IDS.aws).toBe('demo-aws-acme-prod');
      expect(DEMO_NAMESPACE_IDS.gcp).toBe('demo-gcp-medstream-prod');
    });
  });

  describe('getMockNamespaces', () => {
    PROVIDERS.forEach((provider) => {
      it(`returns a valid Namespace[] for ${provider}`, () => {
        const namespaces = getMockNamespaces(provider);
        expect(Array.isArray(namespaces)).toBe(true);
        expect(namespaces.length).toBeGreaterThan(0);
        const ns = namespaces[0];
        expect(ns.id).toBe(DEMO_NAMESPACE_IDS[provider]);
        expect(ns.cloudProvider).toBe(provider);
        expect(ns.isActive).toBe(true);
        expect(typeof ns.name).toBe('string');
        expect(typeof ns.createdAt).toBe('string');
      });
    });

    it('Azure namespace has prod environment', () => {
      const [ns] = getMockNamespaces('azure');
      expect(ns.environment).toBe('prod');
    });

    it('AWS namespace has awsRegion set', () => {
      const [ns] = getMockNamespaces('aws');
      expect(ns.awsRegion).toBeTruthy();
    });

    it('GCP namespace has gcpProjectId set', () => {
      const [ns] = getMockNamespaces('gcp');
      expect(ns.gcpProjectId).toBeTruthy();
    });
  });

  describe('getMockQueues', () => {
    it('Azure returns queues array', () => {
      const queues = getMockQueues('azure');
      expect(Array.isArray(queues)).toBe(true);
      expect(queues.length).toBeGreaterThan(0);
    });

    it('AWS returns queues array with positive counts', () => {
      const queues = getMockQueues('aws');
      expect(queues.length).toBeGreaterThan(0);
      queues.forEach((q) => {
        expect(typeof q.name).toBe('string');
        expect(typeof q.activeMessageCount).toBe('number');
        expect(typeof q.deadLetterMessageCount).toBe('number');
      });
    });

    it('GCP returns empty queues (Pub/Sub uses topics)', () => {
      const queues = getMockQueues('gcp');
      expect(queues).toEqual([]);
    });
  });

  describe('getMockTopics', () => {
    PROVIDERS.forEach((provider) => {
      it(`${provider} returns topics with subscriptionCount`, () => {
        const topics = getMockTopics(provider);
        expect(Array.isArray(topics)).toBe(true);
        if (provider === 'gcp') {
          expect(topics.length).toBeGreaterThan(0);
        }
        topics.forEach((t) => {
          expect(typeof t.name).toBe('string');
          expect(typeof t.subscriptionCount).toBe('number');
        });
      });
    });
  });

  describe('getMockSubscriptions', () => {
    it('Azure: order-events topic has subscriptions', () => {
      const subs = getMockSubscriptions('azure', 'order-events');
      expect(subs.length).toBeGreaterThan(0);
      subs.forEach((s) => {
        expect(s.topicName).toBe('order-events');
        expect(typeof s.name).toBe('string');
      });
    });

    it('GCP: lab-results topic has subscriptions', () => {
      const subs = getMockSubscriptions('gcp', 'lab-results');
      expect(subs.length).toBeGreaterThan(0);
    });

    it('returns empty array for unknown topic', () => {
      const subs = getMockSubscriptions('azure', 'nonexistent-topic');
      expect(subs).toEqual([]);
    });
  });

  describe('getMockMessages', () => {
    PROVIDERS.forEach((provider) => {
      it(`${provider} returns PaginatedResponse shape`, () => {
        const result = getMockMessages(provider, 'test-queue', 'active', 0, 10);
        expect(Array.isArray(result.items)).toBe(true);
        expect(typeof result.totalCount).toBe('number');
        expect(typeof result.page).toBe('number');
        expect(typeof result.pageSize).toBe('number');
        expect(typeof result.hasNextPage).toBe('boolean');
        expect(typeof result.hasPreviousPage).toBe('boolean');
      });

      it(`${provider} returns active messages`, () => {
        const result = getMockMessages(provider, 'test-queue', 'active', 0, 50);
        expect(result.items.length).toBeGreaterThan(0);
        result.items.forEach((m) => {
          expect(typeof m.messageId).toBe('string');
          expect(typeof m.sequenceNumber).toBe('number');
        });
      });

      it(`${provider} returns deadletter messages`, () => {
        const result = getMockMessages(provider, 'test-queue', 'deadletter', 0, 50);
        expect(result.items.length).toBeGreaterThan(0);
      });
    });

    it('pagination works correctly (skip=10, take=5)', () => {
      const first10 = getMockMessages('azure', 'orders-queue', 'active', 0, 10);
      const next5 = getMockMessages('azure', 'orders-queue', 'active', 10, 5);
      // Pages should not overlap
      const firstIds = new Set(first10.items.map((m) => m.messageId));
      const nextIds = new Set(next5.items.map((m) => m.messageId));
      const overlap = [...firstIds].filter((id) => nextIds.has(id));
      expect(overlap.length).toBe(0);
    });

    it('hasPreviousPage=false for first page', () => {
      const result = getMockMessages('azure', 'test', 'active', 0, 10);
      expect(result.hasPreviousPage).toBe(false);
    });

    it('hasPreviousPage=true for second page', () => {
      const result = getMockMessages('azure', 'test', 'active', 10, 10);
      expect(result.hasPreviousPage).toBe(true);
    });
  });

  describe('getMockCapabilities', () => {
    // Field-for-field mirror of services/api/src/ServiceHub.Core/Models/ProviderCapabilities.cs.
    // These are literal expected values, not derived from the same source as the implementation,
    // so a change to either side that silently drifts from the other is caught here.

    it('azure matches ProviderCapabilities.Azure', () => {
      expect(getMockCapabilities('azure')).toEqual({
        supportsMessageCounts: true,
        supportsManualDeadLetter: true,
        supportsPurge: false,
        supportsScheduledMessages: true,
        supportsRepeatablePeek: true,
        notes: 'Purge is not supported — the SDK has no reliable single-message delete by sequence number.',
      });
    });

    it('aws matches ProviderCapabilities.Aws', () => {
      expect(getMockCapabilities('aws')).toEqual({
        supportsMessageCounts: true,
        supportsManualDeadLetter: true,
        supportsPurge: true,
        supportsScheduledMessages: false,
        supportsRepeatablePeek: false,
        notes:
          "Scheduled messages are not supported — SQS only offers DelaySeconds (max 15 minutes) at send time. Repeated/live polling is also not supported — SQS has no non-destructive peek, so every call is a receive that counts toward the queue's maxReceiveCount.",
      });
    });

    it('gcp matches ProviderCapabilities.Gcp', () => {
      expect(getMockCapabilities('gcp')).toEqual({
        supportsMessageCounts: false,
        supportsManualDeadLetter: false,
        supportsPurge: true,
        supportsScheduledMessages: false,
        supportsRepeatablePeek: true,
        notes:
          'Message counts and manual dead-lettering are not supported — Pub/Sub has no count API and dead-lettering is policy-driven via MaxDeliveryAttempts. Scheduled messages are not supported either.',
      });
    });
  });

  describe('getMockCapabilitiesMap', () => {
    it('returns all three providers keyed PascalCase, matching the real API shape', () => {
      const map = getMockCapabilitiesMap();
      expect(map.Azure).toEqual(getMockCapabilities('azure'));
      expect(map.Aws).toEqual(getMockCapabilities('aws'));
      expect(map.Gcp).toEqual(getMockCapabilities('gcp'));
    });
  });

  describe('getMockStats', () => {
    PROVIDERS.forEach((provider) => {
      it(`${provider} returns valid stats`, () => {
        const stats = getMockStats(provider);
        expect(typeof stats.totalQueues).toBe('number');
        expect(typeof stats.totalTopics).toBe('number');
        expect(typeof stats.totalActive).toBe('number');
        expect(typeof stats.totalDlq).toBe('number');
        expect(typeof stats.totalScheduled).toBe('number');
        expect(stats.totalDlq).toBeGreaterThan(0);
      });
    });
  });
});
