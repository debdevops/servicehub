import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

/**
 * The router is given the demo prefix as its basename (router.tsx), so react-router strips `/demo` before matching:
 * `/demo` is `/` and `/demo/aws/dlq` is `/dlq`. That is why the page routes need no `/demo` copy and never fall to "not found".
 */
describe('a basename that is the demo prefix', () => {
  const routes = [
    { path: '/', element: <p>home</p> },
    { path: '/dlq', element: <p>dlq</p> },
    { path: '*', element: <p>not found</p> },
  ]
  it.each([
    ['/demo', '/demo', 'home'],
    ['/demo/aws', '/demo/aws', 'home'],
    ['/demo/aws/dlq', '/demo/aws', 'dlq'],
  ])('%s opens the page beneath it, not the not-found route', (url, basename, text) => {
    render(<RouterProvider router={createMemoryRouter(routes, { basename, initialEntries: [url] })} />)
    expect(screen.getByText(text)).toBeTruthy()
  })
})
