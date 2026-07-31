import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { RouteErrorPage } from '@/pages/RouteErrorPage';

function ThrowError({ message }: { message: string }): never {
  throw new Error(message);
}

function ThrowNonError(): never {
  throw 'a plain string error';
}

function renderWithThrow(element: React.ReactElement) {
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element,
        errorElement: <RouteErrorPage />,
      },
    ],
    { initialEntries: ['/'] }
  );
  return render(<RouterProvider router={router} />);
}

describe('RouteErrorPage', () => {
  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the application-error heading for a thrown Error', () => {
    renderWithThrow(<ThrowError message="boom" />);
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });

  it('shows an error identifier for an application error', () => {
    renderWithThrow(<ThrowError message="boom" />);
    expect(screen.getByText(/Error ID:/i)).toBeInTheDocument();
  });

  it('links "Report Issue" to the real GitHub repo', () => {
    renderWithThrow(<ThrowError message="boom" />);
    const href = screen.getByText('Report Issue').closest('a')?.getAttribute('href');
    expect(href).toContain('github.com/debdevops/servicehub/issues/new');
  });

  it('renders a working reload button', () => {
    renderWithThrow(<ThrowError message="boom" />);
    expect(screen.getByText('Reload Page')).toBeInTheDocument();
  });

  it('renders the chunk-load variant for a failed dynamic import, without an error id or issue link', () => {
    renderWithThrow(<ThrowError message="Failed to fetch dynamically imported module: /assets/x.js" />);
    expect(screen.getByText('Update available')).toBeInTheDocument();
    expect(screen.queryByText(/Error ID:/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Report Issue')).not.toBeInTheDocument();
  });

  it('still renders reload for the chunk-load variant', () => {
    renderWithThrow(<ThrowError message="error loading dynamically imported module" />);
    expect(screen.getByText('Reload Page')).toBeInTheDocument();
  });

  it('does not itself crash when a non-Error value is thrown', () => {
    renderWithThrow(<ThrowNonError />);
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });
});
