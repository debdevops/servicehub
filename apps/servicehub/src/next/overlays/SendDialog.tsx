import { useLocation } from 'react-router-dom'
import SendMessageModal from '../../components/message/SendMessageModal'
import { ProviderScopeContext } from '../../components/provider/providerScope'
import { cloudOf, parseScope } from '../lib/clouds'
import type { NextOverlayBodyProps } from './registry'

/**
 * Send a test message (build-plan unit F8; design `d11-send-test-message`), at `?dialog=send&ns=<connection>`: one test message onto a
 * queue or topic. It IS the 4.2.0 Send dialog, moved not rewritten, and keeps every protection it has: Operator only, the intent header,
 * the audit record, and **Production connections are not offered** (decision D46). Manual Send is the one door outside the emergency stop
 * and the replay gate by design; the kernel guard test pins its protections. The dialog asks the shell only which cloud it is looking
 * at, so the picker lists that cloud's connections.
 */
export default function SendDialog({ close }: NextOverlayBodyProps) {
  const cloud = cloudOf(parseScope(useLocation().pathname.split('/')[2]))
  return (
    <ProviderScopeContext.Provider value={{ selected: cloud, select: () => {} }}>
      <SendMessageModal close={close} />
    </ProviderScopeContext.Provider>
  )
}
