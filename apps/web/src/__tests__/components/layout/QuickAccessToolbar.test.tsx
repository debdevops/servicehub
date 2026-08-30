import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route, Link } from 'react-router-dom';
import { QuickAccessToolbar } from '@/components/layout/QuickAccessToolbar';

// Every base path the Quick Access menu (QuickAccessPanel + IconRail) can navigate to,
// mapped to the label QuickAccessToolbar should show for it. `signatures` isn't a direct
// Quick Access panel item — it's reached by drilling into DLQ Intelligence/Incident
// Center — but it's still part of the workspace area, so it's covered here too.
const WORKSPACE_ROUTES: Array<{ path: string; label: string }> = [
  { path: '/messages-overview?tab=active', label: 'Active Messages' },
  { path: '/messages-overview?tab=deadletter', label: 'Dead-Letter' },
  { path: '/messages?queueType=deadletter', label: 'Dead-Letter' },
  { path: '/live-tail?namespace=ns1', label: 'Live Tail' },
  { path: '/scheduled?namespace=ns1', label: 'Scheduled Messages' },
  { path: '/dashboard', label: 'Namespace Overview' },
  { path: '/incidents', label: 'Incident Center' },
  { path: '/fleet', label: 'Fleet Health' },
  { path: '/cloud-bridge', label: 'Cloud Bridge' },
  { path: '/dlq-history', label: 'DLQ Intelligence' },
  { path: '/signatures', label: 'Failure Signatures' },
  { path: '/rules', label: 'Auto-Replay Rules' },
  { path: '/approval-queue', label: 'Approval Queue' },
  { path: '/autonomy', label: 'Autonomy' },
  { path: '/insights', label: 'Proactive Insights' },
  { path: '/cross-cloud-trace', label: 'Multi-Cloud Trace' },
  { path: '/health', label: 'System Health' },
  { path: '/audit', label: 'Audit Trail' },
  { path: '/recovery', label: 'Recovery Evidence' },
  { path: '/playbook', label: 'Playbook Ledger' },
  { path: '/governance', label: 'Governance' },
  { path: '/security', label: 'Security & Privacy' },
  { path: '/help', label: 'Help & Guide' },
  { path: '/advanced-servicehub', label: 'Advanced ServiceHub' },
];

const ALL_BASE_PATHS = [...new Set(WORKSPACE_ROUTES.map((r) => r.path.split('?')[0])), '/connect'];

function Page({ path }: { path: string }) {
  return (
    <div>
      <span data-testid="current-path">{path}</span>
      <QuickAccessToolbar />
      <Link to="/messages-overview?tab=active">to-active</Link>
      <Link to="/messages-overview?tab=deadletter">to-deadletter</Link>
      <Link to="/live-tail?namespace=ns1">to-live-tail</Link>
    </div>
  );
}

function renderApp(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        {ALL_BASE_PATHS.map((path) => (
          <Route key={path} path={path} element={<Page path={path} />} />
        ))}
      </Routes>
    </MemoryRouter>
  );
}

describe('QuickAccessToolbar', () => {
  it.each(WORKSPACE_ROUTES)('shows Back/Forward and the "$label" label on $path', ({ path, label }) => {
    renderApp(path);
    expect(screen.getByText(label)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go back' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go forward' })).toBeInTheDocument();
  });

  it('renders nothing outside the Quick Access workspace routes', () => {
    renderApp('/connect');
    expect(screen.queryByRole('button', { name: 'Go back' })).not.toBeInTheDocument();
  });

  it('disables Back and Forward on a fresh entry', () => {
    renderApp('/messages-overview?tab=active');
    expect(screen.getByRole('button', { name: 'Go back' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Go forward' })).toBeDisabled();
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
