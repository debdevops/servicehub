/** Shared job-status → badge style map, reused by every panel that renders a replay job's status. */
export const JOB_STATUS_STYLES: Record<string, { bg: string; text: string }> = {
  Completed: { bg: 'bg-green-100', text: 'text-green-700' },
  CompletedWithErrors: { bg: 'bg-orange-100', text: 'text-orange-700' },
  Failed: { bg: 'bg-red-100', text: 'text-red-700' },
  Cancelled: { bg: 'bg-gray-100', text: 'text-gray-600' },
  Running: { bg: 'bg-blue-100', text: 'text-blue-700' },
  Pending: { bg: 'bg-amber-100', text: 'text-amber-700' },
};
