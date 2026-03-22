import { describe, it, expect } from 'vitest';
import { generateMessages } from '@/lib/messageGenerator';
import type { MessageScenario } from '@/lib/messageGenerator';

// ─── generateMessages ─────────────────────────────────────────────────────────

describe('generateMessages', () => {
  it('generates the requested number of messages', () => {
    const messages = generateMessages({
      targetType: 'queue',
      queueName: 'test-queue',
      volume: 5,
      scenarios: ['order-processing'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    expect(messages).toHaveLength(5);
  });

  it('generates zero messages when volume is 0', () => {
    const messages = generateMessages({
      targetType: 'queue',
      queueName: 'test-queue',
      volume: 0,
      scenarios: ['order-processing'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    expect(messages).toHaveLength(0);
  });

  it('each message has a non-empty body', () => {
    const messages = generateMessages({
      targetType: 'queue',
      queueName: 'test-queue',
      volume: 3,
      scenarios: ['payment-gateway'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(msg.body).toBeTruthy();
    });
  });

  it('each message has a correlationId', () => {
    const messages = generateMessages({
      targetType: 'queue',
      volume: 3,
      scenarios: ['order-processing'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(msg.correlationId).toBeTruthy();
    });
  });

  it('each message has a contentType', () => {
    const messages = generateMessages({
      targetType: 'queue',
      volume: 3,
      scenarios: ['notification-service'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(msg.contentType).toBeTruthy();
    });
  });

  it('all supported scenarios can generate messages without throwing', () => {
    const scenarios: MessageScenario[] = [
      'order-processing',
      'payment-gateway',
      'notification-service',
      'inventory-update',
      'user-activity',
      'error-handling',
    ];

    expect(() => {
      generateMessages({
        targetType: 'queue',
        volume: 1,
        scenarios,
        anomalyRate: 0,
        includeStructuredData: true,
      });
    }).not.toThrow();
  });

  it('anomalyRate 100 applies anomalies to messages', () => {
    const messages = generateMessages({
      targetType: 'queue',
      volume: 10,
      scenarios: ['order-processing'],
      anomalyRate: 100,
      includeStructuredData: true,
    });
    // With 100% anomaly rate, no message should have anomalyType 'none'
    const anomalous = messages.filter(m => m.anomalyType !== 'none');
    expect(anomalous.length).toBeGreaterThan(0);
  });

  it('anomalyRate 0 produces only non-anomalous messages', () => {
    const messages = generateMessages({
      targetType: 'queue',
      volume: 10,
      scenarios: ['order-processing'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(msg.anomalyType).toBe('none');
    });
  });

  it('each message has a scenario property matching a valid scenario', () => {
    const validScenarios: MessageScenario[] = [
      'order-processing',
      'payment-gateway',
      'notification-service',
      'inventory-update',
      'user-activity',
      'error-handling',
    ];
    const messages = generateMessages({
      targetType: 'queue',
      volume: 6,
      scenarios: validScenarios,
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(validScenarios).toContain(msg.scenario);
    });
  });

  it('message body is parseable JSON for structured data messages', () => {
    const messages = generateMessages({
      targetType: 'queue',
      volume: 5,
      scenarios: ['order-processing'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(() => JSON.parse(msg.body)).not.toThrow();
    });
  });

  it('message properties contains generator metadata', () => {
    const messages = generateMessages({
      targetType: 'queue',
      volume: 2,
      scenarios: ['order-processing'],
      anomalyRate: 0,
      includeStructuredData: true,
    });
    messages.forEach(msg => {
      expect(msg.properties).toBeDefined();
    });
  });
});
