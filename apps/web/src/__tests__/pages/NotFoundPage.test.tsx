import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { NotFoundPage } from '@/pages/NotFoundPage';

function renderAt(path: string) {
  const router = createMemoryRouter([{ path: '*', element: <NotFoundPage /> }], {
    initialEntries: [path],
  });
  return render(<RouterProvider router={router} />);
}

describe('NotFoundPage', () => {
  it('renders the not-found heading', () => {
    renderAt('/does/not/exist');
    expect(screen.getByText('Page not found')).toBeInTheDocument();
  });

  it('displays the actual unmatched path, preserving the URL rather than redirecting', () => {
    renderAt('/nonsense/path');
    expect(screen.getByText('/nonsense/path')).toBeInTheDocument();
  });

  it('renders a Go Home link to /welcome', () => {
    renderAt('/whatever');
    expect(screen.getByText('Go Home').closest('a')).toHaveAttribute('href', '/welcome');
  });

  it('renders a Go Back button', () => {
    renderAt('/whatever');
    expect(screen.getByText('Go Back')).toBeInTheDocument();
  });
});
