import { useLocation } from 'react-router-dom'
import ApproveModal from '../../components/approve/ApproveModal'
import { nextEntry, nextHref } from '../nav/navigation'
import type { NextOverlayBodyProps } from './registry'

/**
 * Review (build-plan unit E2; design `d05-review`), at `?dialog=review&entry=<id>` for one replay or `&entries=a,b,c` for the ones a
 * person ticked on Pending approvals. It IS the 4.2.0 Approve dialog — the same gated route, the same "Not now" that changes nothing,
 * the same Decline that needs a reason and deletes nothing — moved, not rewritten (rule 6). Only where it sends you afterwards differs:
 * to this shell's Replay history, in the scope you are in.
 */
export default function ReviewDialog({ close }: NextOverlayBodyProps) {
  const { pathname } = useLocation()
  // `/next/<cloud>/…` — the scope is the first segment after the root; anything else falls back to all clouds.
  const cloud = pathname.split('/')[2]
  return <ApproveModal close={close} replayedHref={nextHref(nextEntry('replays'), { cloud: ['azure', 'aws', 'gcp'].includes(cloud) ? cloud : 'all' })} />
}
