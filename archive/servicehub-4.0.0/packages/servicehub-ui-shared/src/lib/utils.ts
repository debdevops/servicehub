/**
 * Format a date to relative time (e.g., "2m ago", "5h ago", "3d ago")
 */
export function formatRelativeTime(date: Date): string {
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffSec < 60) {
    return 'just now';
  } else if (diffMin < 60) {
    return `${diffMin}m ago`;
  } else if (diffHour < 24) {
    return `${diffHour}h ago`;
  } else {
    return `${diffDay}d ago`;
  }
}

/**
 * Format number with thousands separator (e.g., 3892 → "3,892")
 */
export function formatNumber(num: number): string {
  return num.toLocaleString('en-US');
}

/**
 * Convert a Date into the string a <input type="datetime-local"> expects, expressed
 * in the browser's local time. `date.toISOString()` alone renders UTC digits, which a
 * datetime-local input then (mis)interprets as local time — this offsets first so the
 * digits are actually local.
 */
export function toDatetimeLocalValue(date: Date): string {
  const offsetMs = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16);
}
