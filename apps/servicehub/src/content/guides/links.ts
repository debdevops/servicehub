/** Kept apart from `index.ts` so screens can link to a guide without pulling the guide text into the initial chunk. */
export const guideClouds = ['azure', 'aws', 'gcp'] as const

/** `?topic=` value that opens a cloud's guide in Help. */
export const guideTopic = (cloud: string) => `guide-${cloud}`

/** Which guide a screen's help link opens: the cloud you are working in if it has a guide, else the first one. */
export function guideCloudFor(selected: string | null): string {
  return selected !== null && (guideClouds as readonly string[]).includes(selected) ? selected : guideClouds[0]
}
