import { Link } from 'react-router-dom';
import { Route } from 'lucide-react';

interface CrossCloudTraceLinkProps {
  correlationId?: string | null;
}

/**
 * §6.2's "View cross-cloud path" link — renders nothing when no member message carries a
 * CorrelationId. Deep-links into the existing CrossCloudTracePage rather than duplicating its
 * provider-registration/capability checks here.
 */
export function CrossCloudTraceLink({ correlationId }: CrossCloudTraceLinkProps) {
  if (!correlationId) return null;

  return (
    <Link
      to={`/cross-cloud-trace?traceId=${encodeURIComponent(correlationId)}`}
      className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-md bg-indigo-50 text-indigo-700 border border-indigo-200 hover:bg-indigo-100"
    >
      <Route className="w-3.5 h-3.5" />
      View cross-cloud path
    </Link>
  );
}
