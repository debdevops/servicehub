/** The "Try again" that follows every read-error sentence (unit 6.1): what failed in words, then a way to retry it. */
export function RetryLink({ onRetry }: { onRetry: () => void }) {
  return (
    <button type="button" onClick={onRetry} className="font-medium text-[var(--color-primary-700)] hover:underline">
      Try again
    </button>
  )
}
