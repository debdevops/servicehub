import { renderHook } from '@testing-library/react'
import { useReplayAll } from '../../../../apps/servicehub/src/lib/replayAll'

/** The store's current state, read the way a component reads it. */
export const useReplayAllSnapshot = () => renderHook(useReplayAll).result.current
