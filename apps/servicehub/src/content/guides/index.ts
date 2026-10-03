import { awsGuide } from './aws.generated'
import { azureGuide } from './azure.generated'
import { gcpGuide } from './gcp.generated'
import type { Guide } from './types'

export { guideCloudFor, guideTopic } from './links'

/** The step-by-step guides, one per cloud that has one. Step numbers match across guides, so a screen's (?) lands on the same step in each. */
export const guides: Readonly<Record<string, Guide>> = { azure: azureGuide, aws: awsGuide, gcp: gcpGuide }
