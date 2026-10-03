/**
 * §6.1's header "recommended next action" — a one-line pointer keyed off the signature's
 * already-computed TrendBadge value. A static lookup, not a new signal: it points at where to
 * act (Root Cause & Knowledge, Timeline, Replay Safety) rather than acting itself, since the
 * mutating actions already live in the Actions Bar.
 */
export function getTrendRecommendation(trend: string): string | null {
  switch (trend) {
    case 'New':
      return 'No knowledge on file yet — start with Root Cause & Knowledge below.';
    case 'Recurring':
      return 'Check the Timeline for prior attempts before acting again.';
    case 'Escalating':
      return 'Review Replay Safety before replaying — occurrence rate is rising.';
    default:
      return null;
  }
}
