import { useEmergencyStop } from '../../hooks/useSettings'
import { stopState, type StopState } from './words'

/** The stop as one hook, so the header and the band read the same query and can never disagree. */
export function useStopState(): StopState {
  const stop = useEmergencyStop()
  return stopState(stop.data, stop.isError)
}
