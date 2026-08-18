import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route, Link } from 'react-router-dom';
import { QuickAccessToolbar } from '@/components/layout/QuickAccessToolbar';

function Page({ path }: { path: string }) {
  return (
    <div>
      <span data-testid="current-path">{path}</span>
      <QuickAccessToolbar />
      <Link to="/messages-overview?tab=active">to-active</Link>
      <Link to="/messages-overview?tab=deadletter">to-deadletter</Link>
      <Link to="/live-tail?namespace=ns1">to-live-tail</Link>
      <Link to="/scheduled?namespace=ns1">to-scheduled</Link>
      <Link to="/dashboard">to-dashboard</Link>
    </div>
  );
}

function renderApp(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/messages-overview" element={<Page path="/messages-overview" />} />
        <Route path="/messages" element={<Page path="/messages" />} />
        <Route path="/live-tail" element={<Page path="/live-tail" />} />
        <Route path="/scheduled" element={<Page path="/scheduled" />} />
        <Route path="/dashboard" element={<Page path="/dashboard" />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('QuickAccessToolbar', () => {
  it('renders Back and Forward controls with accessible labels on a Quick Access workspace route', () => {
    renderApp('/messages-overview?tab=active');
    expect(screen.getByRole('button', { name: 'Go back' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go forward' })).toBeInTheDocument();
  });

  it('renders nothing outside the Quick Access workspace routes', () => {
    renderApp('/dashboard');
    expect(screen.queryByRole('button', { name: 'Go back' })).not.toBeInTheDocument();
  });

  it('disables Back and Forward on a fresh entry', () => {
    renderApp('/messages-overview?tab=active');
    expect(screen.getByRole('button', { name: 'Go back' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Go forward' })).toBeDisabled();
  });

  it('shows the Active Messages label for messages-overview?tab=active', () => {
    renderApp('/messages-overview?tab=active');
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
  });

  it('shows the Dead-Letter label for messages-overview?tab=deadletter', () => {
    renderApp('/messages-overview?tab=deadletter');
    expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
  });

  it('shows the Live Tail label', () => {
    renderApp('/live-tail?namespace=ns1');
    expect(screen.getByText('Live Tail')).toBeInTheDocument();
  });

  it('shows the Scheduled Messages label', () => {
    renderApp('/scheduled?namespace=ns1');
    expect(screen.getByText('Scheduled Messages')).toBeInTheDocument();
  });

  it('enables Back after a cross-section navigation, and Back restores the previous section', () => {
    renderApp('/messages-overview?tab=active');
    fireEvent.click(screen.getByText('to-live-tail'));
    expect(screen.getByText('Live Tail')).toBeInTheDocument();
    const backButton = screen.getByRole('button', { name: 'Go back' });
    expect(backButton).not.toBeDisabled();

    fireEvent.click(backButton);
    expect(screen.getByText('Active Messages')).toBeInTheDocument();
  });

  it('Forward restores the section left via Back, and Back is keyboard-activatable', () => {
    renderApp('/messages-overview?tab=active');
    fireEvent.click(screen.getByText('to-live-tail'));
    fireEvent.click(screen.getByRole('button', { name: 'Go back' }));

    const forwardButton = screen.getByRole('button', { name: 'Go forward' });
    expect(forwardButton).not.toBeDisabled();
    forwardButton.focus();
    fireEvent.click(forwardButton);
    expect(screen.getByText('Live Tail')).toBeInTheDocument();
  });

  it('a new navigation after Back clears the Forward entry', () => {
    renderApp('/messages-overview?tab=active');
    fireEvent.click(screen.getByText('to-live-tail'));
    fireEvent.click(screen.getByRole('button', { name: 'Go back' }));
    expect(screen.getByRole('button', { name: 'Go forward' })).not.toBeDisabled();

    fireEvent.click(screen.getByText('to-deadletter'));
    expect(screen.getByText('Dead-Letter')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go forward' })).toBeDisabled();
  });
});
