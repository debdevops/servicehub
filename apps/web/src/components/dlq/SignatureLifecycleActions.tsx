interface SignatureLifecycleActionsProps {
  status: string;
  onResolve: () => void;
  onReopen: () => void;
  onSuppress: () => void;
  onArchive: () => void;
  pending?: boolean;
}

/**
 * Renders only the lifecycle actions valid for the current status — a client-side mirror of
 * the backend's transition matrix, for UX purposes only. The backend remains the source of
 * truth/validator for every transition.
 */
export function SignatureLifecycleActions({
  status,
  onResolve,
  onReopen,
  onSuppress,
  onArchive,
  pending = false,
}: SignatureLifecycleActionsProps) {
  if (status === 'Archived') {
    return (
      <span
        className="px-2.5 py-1 text-xs font-medium rounded-md bg-gray-100 text-gray-500 border border-gray-200"
        title="Archived is terminal — this signature is closed out and no longer tracked operationally"
      >
        Archived
      </span>
    );
  }

  return (
    <div className="flex items-center gap-2 flex-wrap" role="group" aria-label="Lifecycle actions">
      {(status === 'Active' || status === 'Reopened') && (
        <button
          onClick={onResolve}
          disabled={pending}
          className="px-2.5 py-1 text-xs font-medium rounded-md bg-emerald-50 text-emerald-700 border border-emerald-200 hover:bg-emerald-100 disabled:opacity-50"
        >
          Mark Resolved
        </button>
      )}
      {(status === 'Resolved' || status === 'Suppressed') && (
        <button
          onClick={onReopen}
          disabled={pending}
          className="px-2.5 py-1 text-xs font-medium rounded-md bg-amber-50 text-amber-700 border border-amber-200 hover:bg-amber-100 disabled:opacity-50"
        >
          Reopen
        </button>
      )}
      {status !== 'Suppressed' && (
        <button
          onClick={onSuppress}
          disabled={pending}
          className="px-2.5 py-1 text-xs font-medium rounded-md bg-purple-50 text-purple-700 border border-purple-200 hover:bg-purple-100 disabled:opacity-50"
        >
          Suppress
        </button>
      )}
      <button
        onClick={onArchive}
        disabled={pending}
        className="px-2.5 py-1 text-xs font-medium rounded-md bg-blue-50 text-blue-700 border border-blue-200 hover:bg-blue-100 disabled:opacity-50"
      >
        Archive
      </button>
    </div>
  );
}
