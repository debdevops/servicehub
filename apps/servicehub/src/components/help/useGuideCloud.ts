import { guideCloudFor } from '../../content/guides'
import { useProviderScope } from '../provider/providerScope'

/** The cloud whose guide a help link should open — the one in scope. */
export function useGuideCloud(): string {
  return guideCloudFor(useProviderScope().selected)
}
