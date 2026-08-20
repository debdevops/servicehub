import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route, Link, useNavigate } from 'react-router-dom';
import { useQuickAccessHistory } from '@/hooks/useQuickAccessHistory';

function Probe({ label }: { label: string }) {
  const { canGoBack, canGoForward, goBack, goForward } = useQuickAccessHistory();
  const navigate = useNavigate();
  return (
    <div>
      <span data-testid="label">{label}</span>
      <span data-testid="can-go-back">{String(canGoBack)}</span>
      <span data-testid="can-go-forward">{String(canGoForward)}</span>
      <button onClick={goBack}>back</button>
      <button onClick={goForward}>forward</button>
      <Link to="/b">to-b</Link>
      <button onClick={() => navigate('/c')}>to-c-push</button>
      <button onClick={() => navigate('/a', { replace: true })}>to-a-replace</button>
    </div>
  );
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/a" element={<Probe label="A" />} />
        <Route path="/b" element={<Probe label="B" />} />
        <Route path="/c" element={<Probe label="C" />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('useQuickAccessHistory', () => {
  it('starts with both directions disabled on a fresh mount', () => {
    renderAt('/a');
    expect(screen.getByTestId('can-go-back')).toHaveTextContent('false');
    expect(screen.getByTestId('can-go-forward')).toHaveTextContent('false');
  });

  it('enables Back after navigating forward, and Back returns to the previous entry', () => {
    renderAt('/a');
    fireEvent.click(screen.getByText('to-b'));
    expect(screen.getByTestId('label')).toHaveTextContent('B');
    expect(screen.getByTestId('can-go-back')).toHaveTextContent('true');

    fireEvent.click(screen.getByText('back'));
    expect(screen.getByTestId('label')).toHaveTextContent('A');
    expect(screen.getByTestId('can-go-back')).toHaveTextContent('false');
  });

  it('Forward restores the entry that was left via Back', () => {
    renderAt('/a');
    fireEvent.click(screen.getByText('to-b'));
    fireEvent.click(screen.getByText('back'));
    expect(screen.getByTestId('can-go-forward')).toHaveTextContent('true');

    fireEvent.click(screen.getByText('forward'));
    expect(screen.getByTestId('label')).toHaveTextContent('B');
    expect(screen.getByTestId('can-go-forward')).toHaveTextContent('false');
  });

  it('a new navigation after Back discards the stale Forward entry', () => {
    renderAt('/a');
    fireEvent.click(screen.getByText('to-b'));
    fireEvent.click(screen.getByText('back'));
    expect(screen.getByTestId('can-go-forward')).toHaveTextContent('true');

    fireEvent.click(screen.getByText('to-c-push'));
    expect(screen.getByTestId('label')).toHaveTextContent('C');
    expect(screen.getByTestId('can-go-forward')).toHaveTextContent('false');
    expect(screen.getByTestId('can-go-back')).toHaveTextContent('true');
  });

  it('a replace navigation does not create a new Back-able entry', () => {
    renderAt('/a');
    fireEvent.click(screen.getByText('to-a-replace'));
    expect(screen.getByTestId('can-go-back')).toHaveTextContent('false');
  });
});
