import { Fragment, type ReactNode } from 'react'

/**
 * A failure reason is usually one CamelCase word (`MaxDeliveryCountExceeded`). Table cells may wrap anywhere so long ids never push the
 * page sideways, and that cut such a word in the middle of a syllable ("Deserializatio / nError"). Putting a break opportunity before each
 * capital that starts a new word makes the cell wrap between words ("Max / Delivery / Count / Exceeded") and only cut a word as a last resort.
 * The text itself is unchanged (a `<wbr>` adds no character), so copying it and searching for it still work.
 */
export function camelBreaks(text: string): ReactNode {
  const parts = text.split(/(?<=[a-z0-9])(?=[A-Z])/)
  return parts.map((p, i) => <Fragment key={i}>{i > 0 && <wbr />}{p}</Fragment>)
}
