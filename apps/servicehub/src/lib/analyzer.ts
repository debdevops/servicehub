/**
 * A plain-English reading of why one message failed, worked out in the browser from what the message
 * itself recorded. It is a SUGGESTION: whoever shows it must badge it as one (R3), because it is a
 * reading of the recorded reason, not a fact the cloud reported.
 *
 * 4.0.0's analyser looked for patterns across many messages; a drawer needs the answer for one, so
 * this is deliberately smaller and nothing is copied from it.
 */
export interface FailureExplanation {
  /** False when there was nothing recorded to read, so the text is guidance rather than a reading — and is not badged as a suggestion. */
  readonly recorded?: boolean
  /** One line for a table row: what went wrong, in plain words. The longer `summary` is for the drawer. */
  readonly headline: string
  readonly summary: string
  /** A field the error names (`customerId`), when it names one. */
  readonly failingField: string | null
}

const fieldPatterns = [
  /required (?:field|property)\s+['"`]?([A-Za-z_][\w.]*)['"`]?\s+(?:is\s+)?(?:missing|not (?:set|provided)|null|empty)/i,
  /missing required (?:field|property)[:\s]+['"`]?([A-Za-z_][\w.]*)/i,
  /['"`]?([A-Za-z_][\w.]*)['"`]? (?:is|was) (?:required|missing)/i,
  /(?:invalid|null|empty) (?:value )?(?:for|in) (?:field |property )?['"`]?([A-Za-z_][\w.]*)/i,
]

export function findFailingField(description: string | null): string | null {
  if (!description) return null
  for (const pattern of fieldPatterns) {
    const found = pattern.exec(description)?.[1]
    if (found) return found
  }
  return null
}

export function explainFailure(reason: string | null, description: string | null, deliveryCount: number): FailureExplanation {
  const failingField = findFailingField(description)
  const text = `${reason ?? ''} ${description ?? ''}`.toLowerCase()
  if (!reason && !description) {
    // A cloud that dead-letters by policy records nothing. Say what is true and where the answer usually is.
    return {
      failingField,
      headline: 'The cloud recorded no reason — read the message itself',
      summary: 'This cloud set the message aside automatically and does not record why. The message body is the best place to look: whatever the receiver could not handle is usually visible there.',
      recorded: false,
    }
  }
  const times = `${deliveryCount} ${deliveryCount === 1 ? 'time' : 'times'}`

  if (failingField) {
    return {
      failingField,
      headline: `Required field “${failingField}” is missing or wrong`,
      summary: `The message does not have what the receiver needs: “${failingField}” is missing or wrong. Retrying will fail the same way until the message or the receiver changes.`,
    }
  }
  if (/unexpected token|json|parse|deserializ|malformed/.test(text)) {
    return { failingField, headline: 'The message body could not be read — it looks malformed', summary: 'The body could not be read — it looks malformed. Retrying the same body will fail the same way.' }
  }
  if (/schema|validation|invalid/.test(text)) {
    return { failingField, headline: 'The message failed the receiver’s validation', summary: 'The receiver rejected the content of the message. Retrying the same message will be rejected the same way until the sender or the receiver changes.' }
  }
  if (/unauthori[sz]ed|authenticat|forbidden|\b40[13]\b|token expired|access denied/.test(text)) {
    return { failingField, headline: 'The receiver was not allowed to do this', summary: 'A credential or permission was rejected. Fix the credential first — replaying before that will fail again.' }
  }
  if (/ttl|expired|time to live/.test(text) && !/lock/.test(text)) {
    return { failingField, headline: 'Expired before anything processed it', summary: 'The message expired before anything processed it. It is usually safe to replay if the receiver is healthy now.' }
  }
  if (/lock/.test(text)) {
    return { failingField, headline: 'The receiver held it too long and lost its claim', summary: 'The receiver took too long and its claim on the message ran out, so the message went back to the queue. A replay usually works if the receiver is faster now.' }
  }
  if (/timeout|timed out|deadline/.test(text)) {
    return { failingField, headline: 'A downstream call took too long', summary: 'Something the receiver depends on did not answer in time. This usually clears once that dependency is healthy, so a replay often works.' }
  }
  if (/throttl|too many requests|\b429\b|rate limit/.test(text)) {
    return { failingField, headline: 'The receiver was being rate-limited', summary: 'A downstream service was refusing calls because of too much traffic. A replay usually works once traffic settles, and slowly is safest.' }
  }
  if (/not.?found|\b404\b/.test(text)) {
    return { failingField, headline: 'Something the message refers to was not found', summary: 'The message points at something that does not exist (yet). It may work after that thing exists.' }
  }
  if (/maxdeliverycount|max.?delivery|receive count|maxreceive/.test(text)) {
    return {
      failingField,
      headline: `Delivered ${times} and never completed`,
      summary: `It was delivered ${times} and the receiver never finished it, so it was set aside. Whether a replay helps depends on what was failing at the time.`,
    }
  }
  return { failingField, headline: 'No reading available — see the recorded reason', summary: 'ServiceHub has no reading of this one. The reason above is exactly what was recorded.' }
}
