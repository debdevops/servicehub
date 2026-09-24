/**
 * A plain-English reading of why one message failed, worked out in the browser from what the message
 * itself recorded. It is a SUGGESTION: whoever shows it must badge it as one (R3), because it is a
 * reading of the recorded reason, not a fact the cloud reported.
 *
 * 4.0.0's analyser looked for patterns across many messages; a drawer needs the answer for one, so
 * this is deliberately smaller and nothing is copied from it.
 */
export interface FailureExplanation {
  readonly summary: string
  /** A field the error names (`customerId`), when it names one. */
  readonly failingField: string | null
}

const fieldPatterns = [
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

  if (failingField) {
    return { failingField, summary: `The message does not have what the receiver needs: “${failingField}” is missing or wrong. Retrying will fail the same way until the message or the receiver changes.` }
  }
  if (/unexpected token|json|parse|deserializ|malformed/.test(text)) {
    return { failingField, summary: 'The body could not be read — it looks malformed. Retrying the same body will fail the same way.' }
  }
  if (/ttl|expired/.test(text)) {
    return { failingField, summary: 'The message expired before anything processed it. It is usually safe to replay if the receiver is healthy now.' }
  }
  if (/maxdeliverycount|max.?delivery|receive count|maxreceive/.test(text)) {
    return {
      failingField,
      summary: `It was delivered ${deliveryCount} ${deliveryCount === 1 ? 'time' : 'times'} and the receiver never finished it, so it was set aside. Whether a replay helps depends on what was failing at the time.`,
    }
  }
  return { failingField, summary: 'ServiceHub has no reading of this one. The reason above is exactly what was recorded.' }
}
