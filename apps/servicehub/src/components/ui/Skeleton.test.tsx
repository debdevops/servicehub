import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Skeleton } from './Skeleton'

describe('Skeleton (6.1)', () => {
  it('announces what is loading and draws the shape, never a number', () => {
    const { container } = render(<Skeleton label="Reading dead letters…" rows={3} />)
    expect(screen.getByRole('status')).toHaveTextContent('Reading dead letters…')
    expect(container.querySelectorAll('[aria-hidden="true"] .animate-pulse').length).toBe(12)
    expect(container.textContent).not.toMatch(/\d/)
  })
})
