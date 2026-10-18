import { useLocation } from 'react-router-dom'
import BulkReplayModal from '../../components/message/BulkReplayModal'
import { ProviderScopeContext } from '../../components/provider/providerScope'
import { cloudOf, parseScope } from '../lib/clouds'
import type { NextOverlayBodyProps } from './registry'

/**
 * Bulk replay (build-plan unit F3; design `d03-bulk-replay`), at `?dialog=bulk-replay`: the PREVIEW first, then the run, then watching
 * it. It IS the 4.2.0 Bulk Replay dialog, moved not rewritten: there is no way to the run without the stored preview, the same safety
 * checks apply to every message as to a single replay, and closing it does not stop a run (progress returns from `?job=`). What the
 * list handed it lives in `bulkSelection`, not the address. The one thing it asks the shell is which cloud it is looking at, so that
 * "Purge instead" is offered only where THAT cloud can delete one message; under "all clouds" it is offered where any can.
 */
export default function BulkReplayDialog({ close }: NextOverlayBodyProps) {
  const cloud = cloudOf(parseScope(useLocation().pathname.split('/')[2]))
  return (
    <ProviderScopeContext.Provider value={{ selected: cloud, select: () => {} }}>
      <BulkReplayModal close={close} />
    </ProviderScopeContext.Provider>
  )
}
