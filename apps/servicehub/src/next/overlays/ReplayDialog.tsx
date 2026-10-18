import { useLocation } from 'react-router-dom'
import ReplayModal from '../../components/message/ReplayModal'
import { nextEntry, nextHref } from '../nav/navigation'
import type { NextOverlayBodyProps } from './registry'

/**
 * Replay (build-plan unit F2; design `d02-replay`), at `?dialog=replay&id=<dead letter>`: what will happen, why it may fail again,
 * the safety checks, what comes after — and only then the one button. It IS the 4.2.0 Replay dialog, moved not rewritten (rule 6):
 * the same proposal, the same gated route, the same refusal that stays visible beside a disabled button, the same three honest
 * endings. Only where its result links go differs: to this shell's Active messages and Replay history, in the scope you are in.
 */
export default function ReplayDialog({ close }: NextOverlayBodyProps) {
  const { pathname } = useLocation()
  const segment = pathname.split('/')[2]
  const cloud = ['azure', 'aws', 'gcp'].includes(segment) ? segment : 'all'
  return <ReplayModal close={close} links={{ active: nextHref(nextEntry('active'), { cloud }), replayed: nextHref(nextEntry('replays'), { cloud }) }} />
}
