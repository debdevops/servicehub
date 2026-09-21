/** Small pill for a namespace/audit-entry environment (Prod/UAT/Dev). Safety-critical —
 * used to distinguish production from non-production wherever destructive actions are gated. */
export function EnvironmentBadge({ env }: { env?: string | null }) {
  const normalized = env?.toLowerCase();

  if (normalized === 'prod') {
    return (
      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-red-100 text-red-700 border border-red-200">
        PROD
      </span>
    );
  }
  if (normalized === 'uat') {
    return (
      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-amber-100 text-amber-700 border border-amber-200">
        UAT
      </span>
    );
  }
  if (normalized === 'dev') {
    return (
      <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-emerald-100 text-emerald-700 border border-emerald-200">
        DEV
      </span>
    );
  }
  return (
    <span className="px-2 py-0.5 text-xs font-bold rounded-full bg-gray-100 text-gray-500 border border-gray-200">
      —
    </span>
  );
}
