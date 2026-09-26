import { useEffect, useSyncExternalStore } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { getStreamStatus, onStreamEvent, startEventStream, stopEventStream, subscribeStreamStatus, type StreamStatus } from '../lib/eventStream'
import { deadLetterKeys } from './useDeadLetters'
import { namespaceKeys } from './useNamespaces'
import { recoveryKeys } from './useRecoverySummary'
import { replayKeys } from './useReplay'
import { pendingKeys } from './usePendingWork'

/**
 * Keeps the app's data fresh while it is open: opens the one stream and, on any event, asks the affected queries
 * to look again. It never puts event data on screen — the durable tables do that. Mounted once, by the app frame.
 */
export function useEventStream(): void {
  const client = useQueryClient()
  useEffect(() => {
    startEventStream()
    const off = onStreamEvent(() => {
      void client.invalidateQueries({ queryKey: deadLetterKeys.all })
      void client.invalidateQueries({ queryKey: replayKeys.all })
      void client.invalidateQueries({ queryKey: recoveryKeys.all })
      void client.invalidateQueries({ queryKey: namespaceKeys.all })
      void client.invalidateQueries({ queryKey: ['audit'] })
      void client.invalidateQueries({ queryKey: pendingKeys.all })
      void client.invalidateQueries({ queryKey: ['settings'] })
    })
    return () => {
      off()
      stopEventStream()
    }
  }, [client])
}

/** Whether the stream is open right now. */
export function useStreamStatus(): StreamStatus {
  return useSyncExternalStore(subscribeStreamStatus, getStreamStatus, () => 'offline' as const)
}

