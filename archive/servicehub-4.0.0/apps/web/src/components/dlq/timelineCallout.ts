import type { DlqTimelineEvent } from '@servicehub/ui-shared/lib/api/dlqHistory';

export interface TimelineCallout {
  message: string;
  tone: 'attention' | 'calm';
}

/**
 * §6.5's single most-recent-event callout — a fixed lookup over the EventType (and, for
 * StatusChanged, the transition target) the panel already renders, never a new inference over
 * the timeline's contents. Returns null when the latest event doesn't warrant a callout.
 */
export function getTimelineCallout(events: DlqTimelineEvent[]): TimelineCallout | null {
  if (events.length === 0) return null;
  const latest = events[events.length - 1];

  if (latest.eventType === 'ReplayJobFailed') {
    return { message: 'Investigate in Replay Safety — the most recent replay attempt failed.', tone: 'attention' };
  }
  if (latest.eventType === 'StatusChanged' && latest.details?.To === 'Resolved') {
    return { message: 'No action needed — monitor for recurrence.', tone: 'calm' };
  }
  return null;
}
