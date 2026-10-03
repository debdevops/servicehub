import { describe, it, expect } from 'vitest';
import { getTimelineCallout } from '@/components/dlq/timelineCallout';
import type { DlqTimelineEvent } from '@servicehub/ui-shared/lib/api/dlqHistory';

function event(eventType: string, details: Record<string, string> | null = null): DlqTimelineEvent {
  return { eventType, description: eventType, timestamp: '2026-01-01T00:00:00Z', details };
}

describe('getTimelineCallout', () => {
  it('returns null for an empty event list', () => {
    expect(getTimelineCallout([])).toBeNull();
  });

  it('returns an "attention" callout when the most recent event is ReplayJobFailed', () => {
    const result = getTimelineCallout([event('SignatureFirstObserved'), event('ReplayJobFailed')]);
    expect(result).toEqual({
      message: 'Investigate in Replay Safety — the most recent replay attempt failed.',
      tone: 'attention',
    });
  });

  it('returns a "calm" callout when the most recent event is a StatusChanged transition to Resolved', () => {
    const result = getTimelineCallout([
      event('SignatureFirstObserved'),
      event('StatusChanged', { From: 'Active', To: 'Resolved', Notes: '' }),
    ]);
    expect(result).toEqual({ message: 'No action needed — monitor for recurrence.', tone: 'calm' });
  });

  it('returns null when the most recent StatusChanged event is not a transition to Resolved', () => {
    const result = getTimelineCallout([
      event('SignatureFirstObserved'),
      event('StatusChanged', { From: 'Active', To: 'Suppressed', Notes: '' }),
    ]);
    expect(result).toBeNull();
  });

  it('returns null for a merely-informational latest event like KnowledgeRecorded', () => {
    const result = getTimelineCallout([event('SignatureFirstObserved'), event('KnowledgeRecorded')]);
    expect(result).toBeNull();
  });

  it('only looks at the most recent event, ignoring an earlier ReplayJobFailed', () => {
    const result = getTimelineCallout([event('ReplayJobFailed'), event('ReplayJobCompleted')]);
    expect(result).toBeNull();
  });
});
