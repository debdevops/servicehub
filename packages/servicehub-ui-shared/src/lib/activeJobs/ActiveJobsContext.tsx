/**
 * ActiveJobsProvider — background survival for long-running operations.
 *
 * ServiceHub had no mechanism for a long-running job (e.g. a signature replay) to keep being
 * watched once the page that started it is navigated away from: polling and the completion
 * toast both lived inside a hook only mounted by that page's progress panel, so unmounting the
 * page silently orphaned the job — it kept running server-side, but the user never learned it
 * finished.
 *
 * Mounted once at the app root (inside QueryClientProvider), this tracks job ids in a small
 * list — persisted to localStorage so a full page reload/browser restart resumes watching too —
 * and polls each one independently of whatever page is currently showing. A page's own
 * progress panel (e.g. SignatureReplayProgressPanel) is unaffected and keeps working exactly as
 * before for its richer in-page UI; this only adds a second, page-independent watcher of the
 * same job so the toast still fires after the user has moved on. See
 * useSignatureReplay.ts's module-level notified-job-ids set for why that doesn't double-toast.
 */
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { useSignatureReplayJob } from '../../hooks/useSignatureReplay';
import { isTerminalBulkOperationStatus } from '../api/bulkOperations';

interface TrackedSignatureReplayJob {
  kind: 'signature-replay';
  jobId: string;
  namespaceId: string;
  signatureHash: string;
}

type TrackedJob = TrackedSignatureReplayJob;

const STORAGE_KEY = 'servicehub.activeJobs.v1';

interface ActiveJobsContextValue {
  /** Starts watching a signature-replay job in the background, independent of any page. */
  trackSignatureReplay: (jobId: string, namespaceId: string, signatureHash: string) => void;
}

const ActiveJobsContext = createContext<ActiveJobsContextValue | null>(null);

function loadTrackedJobs(): TrackedJob[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as TrackedJob[]) : [];
  } catch {
    return [];
  }
}

function saveTrackedJobs(jobs: TrackedJob[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(jobs));
  } catch {
    // Best-effort — private browsing or storage disabled. The in-memory list for this tab still
    // works; only cross-reload persistence is lost.
  }
}

export function ActiveJobsProvider({ children }: { children: ReactNode }) {
  const [jobs, setJobs] = useState<TrackedJob[]>(loadTrackedJobs);

  useEffect(() => {
    saveTrackedJobs(jobs);
  }, [jobs]);

  const untrack = useCallback((jobId: string) => {
    setJobs((prev) => prev.filter((job) => job.jobId !== jobId));
  }, []);

  const trackSignatureReplay = useCallback((jobId: string, namespaceId: string, signatureHash: string) => {
    setJobs((prev) =>
      prev.some((job) => job.jobId === jobId)
        ? prev
        : [...prev, { kind: 'signature-replay', jobId, namespaceId, signatureHash }],
    );
  }, []);

  return (
    <ActiveJobsContext.Provider value={{ trackSignatureReplay }}>
      {children}
      {jobs.map((job) => (
        <SignatureReplayJobWatcher key={job.jobId} job={job} onSettled={untrack} />
      ))}
    </ActiveJobsContext.Provider>
  );
}

/**
 * Renders nothing — exists purely to keep useSignatureReplayJob's polling (and, via that hook,
 * the completion toast + cache invalidation) alive for one tracked job regardless of which page
 * is mounted, then removes the job from tracking once it reaches a terminal status.
 */
function SignatureReplayJobWatcher({
  job,
  onSettled,
}: {
  job: TrackedSignatureReplayJob;
  onSettled: (jobId: string) => void;
}) {
  const { data } = useSignatureReplayJob(job.jobId, job.namespaceId, job.signatureHash);

  useEffect(() => {
    if (data && isTerminalBulkOperationStatus(data.status)) {
      onSettled(job.jobId);
    }
  }, [data, job.jobId, onSettled]);

  return null;
}

export function useActiveJobs(): ActiveJobsContextValue {
  const ctx = useContext(ActiveJobsContext);
  if (!ctx) {
    throw new Error('useActiveJobs must be used within an ActiveJobsProvider');
  }
  return ctx;
}
