import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { camelBreaks } from '@/next/lib/camelBreaks'

describe('camelBreaks', () => {
  it.each([
    ['MaxDeliveryCountExceeded', 3], ['DeserializationError', 1], ['NO_PERMISSIONS', 0], ['Delivery error', 0], ['x', 0],
  ])('%s gets %i break opportunities and keeps its exact text', (text, breaks) => {
    const { container } = render(<span>{camelBreaks(text)}</span>)
    expect(container.querySelectorAll('wbr')).toHaveLength(breaks)
    expect(container).toHaveTextContent(text)
  })
})
