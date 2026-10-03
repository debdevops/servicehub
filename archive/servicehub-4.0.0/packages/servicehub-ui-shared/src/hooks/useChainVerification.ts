import { useMutation } from '@tanstack/react-query';
import { recoveryApi, type ChainVerificationResult } from '../lib/api/recovery';
import { useDemoContext } from '../lib/demo/DemoContext';
import toast from 'react-hot-toast';

/**
 * Hook for recomputing and comparing the caller's Recovery Evidence Ledger hash chain. The chain
 * is partitioned per owner, not per operation (roadmap §5.5), so a "Verify chain" click from any
 * one operation's detail page necessarily verifies the owner's entire chain. Tamper-EVIDENT, not
 * tamper-PROOF — see `RECOVERY_LIMITATION_SENTENCE` and the chain result's own fields.
 */
export function useVerifyChain() {
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: async (operationId: string): Promise<ChainVerificationResult> => {
      if (isDemoMode) {
        // Demo Mode makes no backend calls — the curated fixture's chain is always presented as
        // intact, since inventing a broken-chain fixture without real events to point at would
        // be exactly the kind of unearned claim this feature exists to prevent.
        return {
          ownerId: 'demo-owner',
          isValid: true,
          eventsChecked: 214 * 3 + 1,
          firstDivergentSeq: null,
          reason: null,
        };
      }
      return recoveryApi.verifyChain(operationId);
    },
    onSuccess: (result) => {
      if (result.isValid) {
        toast.success(`Chain verified — ${result.eventsChecked} events intact`);
      } else {
        toast.error(`Chain divergence detected at Seq ${result.firstDivergentSeq}: ${result.reason}`);
      }
    },
    onError: () => toast.error('Chain verification failed. Please try again.'),
  });
}
