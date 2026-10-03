import { within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect } from 'vitest'

/** Pick a row in the app's `Select` dropdown: open it, then click the option whose value (or visible name) matches. */
export async function choose(trigger: HTMLElement, valueOrName: string | RegExp) {
  await userEvent.click(trigger)
  const list = document.getElementById(trigger.getAttribute('aria-controls') ?? '') as HTMLElement
  const option =
    typeof valueOrName === 'string'
      ? (list.querySelector(`[data-value="${valueOrName}"]`) as HTMLElement | null) ?? within(list).getByRole('option', { name: valueOrName })
      : within(list).getByRole('option', { name: valueOrName })
  await userEvent.click(option)
}

/** Open the `Select` dropdown with this accessible name once it is enabled, so its options can be read. */
export async function openSelect(name: string | RegExp) {
  const { screen, waitFor } = await import('@testing-library/react')
  const trigger = await screen.findByRole('button', { name })
  await waitFor(() => expect(trigger).toBeEnabled())
  await userEvent.click(trigger)
  return trigger
}
