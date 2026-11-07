import { useLocation } from 'react-router-dom'
import AddCloudModal from '../../components/connect/AddCloudModal'
import { cloudOf, parseScope } from '../lib/clouds'
import { nextEntry, nextHref } from '../nav/navigation'
import type { NextOverlayBodyProps } from './registry'

/**
 * Connect (build-plan unit F1, second half; design `d01-connect-a-component`, `d12-connect-test`), at `?dialog=connect`. It IS the 4.2.0
 * Add a cloud dialog, moved not rewritten: one connection at a time, one credential per cloud, saved then TESTED, with what ServiceHub
 * can see said plainly; a failed test is never left behind by surprise (remove it and retry, or keep it and fix it later). Admin only.
 *
 * The choice of *read-only or read-and-send* is the Add a cloud dialog's own radio (build-plan C3, migration 0017, `Namespaces.CanSend`);
 * the Access tab can change it later.
 */
export default function ConnectDialog({ close }: NextOverlayBodyProps) {
  const { pathname } = useLocation()
  const cloud = cloudOf(parseScope(pathname.split('/')[2])) ?? undefined
  return <AddCloudModal close={close} initialCloud={cloud} homeHref={(provider) => nextHref(nextEntry('home'), { cloud: provider })} />
}
