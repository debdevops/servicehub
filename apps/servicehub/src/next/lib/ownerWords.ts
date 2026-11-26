import { useNamespaces } from '../../hooks/useNamespaces'
import { useSettings } from '../../hooks/useSettings'

/** Said when no channel was chosen, or the choice cannot be followed (the channel is gone or switched off): every enabled one is told. */
export const ALL_CHANNELS = 'All channels'

/**
 * Who is told about one connection (plan G7): the channel an Admin chose for it, else every enabled channel. A choice that points at a
 * channel that is gone or switched off is shown as "All channels" because that is what the server does then; it never shows silence.
 * Until the two reads arrive, or if either fails, the answer is "All channels".
 */
export function useOwnerWords(): (namespaceId: string | null) => string {
  const namespaces = useNamespaces()
  const settings = useSettings()
  return (namespaceId) => {
    const chosen = namespaces.data?.find((n) => n.id === namespaceId)?.ownerChannelId
    const channel = chosen ? settings.data?.notifications.channels.find((c) => c.id === chosen && c.enabled) : undefined
    return channel ? channel.label : ALL_CHANNELS
  }
}
