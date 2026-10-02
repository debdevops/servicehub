import { awsGuide } from './aws.generated'
import { azureGuide } from './azure.generated'
import type { Guide } from './types'

/** The step-by-step guides, one per cloud that has one. Step numbers match across guides, so a screen's (?) lands on the same step in each. */
export const guides: Readonly<Record<string, Guide>> = { azure: azureGuide, aws: awsGuide }

/** `?topic=` value that opens a cloud's guide in Help. */
export const guideTopic = (cloud: string) => `guide-${cloud}`

/** Which guide a screen's help link opens: the cloud you are working in if it has a guide, else the first one. */
export function guideCloudFor(selected: string | null): string {
  return selected !== null && selected in guides ? selected : Object.keys(guides)[0]
}
