import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import GovernanceGrantsPage from '@/pages/GovernanceGrantsPage';
import {
  useGovernanceGrants,
  useGrantGovernanceRole,
  useRevokeGovernanceGrant,
} from '@servicehub/ui-shared/hooks/useGovernanceGrants';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';

vi.mock('@servicehub/ui-shared/hooks/useGovernanceGrants', () => ({
  useGovernanceGrants: vi.fn(),
  useGrantGovernanceRole: vi.fn(() => ({ mutate: vi.fn(), isPending: false })),
  useRevokeGovernanceGrant: vi.fn(() => ({ mutate: vi.fn(), isPending: false })),
}));

vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(() => ({ data: [] })),
}));

const mockUseGovernanceGrants = useGovernanceGrants as ReturnType<typeof vi.fn>;
const mockUseGrant = useGrantGovernanceRole as ReturnType<typeof vi.fn>;
const mockUseRevoke = useRevokeGovernanceGrant as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/governance']}>
      <GovernanceGrantsPage />
    </MemoryRouter>,
  );
}

const sampleGrant = {
  id: 'grant-1',
  granteeIdentity: 'entra:owner-a',
  granteeKind: 'User' as const,
  role: 'Admin' as const,
  namespaceId: null,
  pillarKind: null,
  grantedAt: '2026-08-29T09:00:00Z',
  grantedByIdentity: 'System:GovernanceGrantSeed',
  revokedAt: null,
  revokedByIdentity: null,
};

describe('GovernanceGrantsPage', () => {
  it('shows a loading state', () => {
    mockUseGovernanceGrants.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn(), isFetching: true });
    renderPage();
    expect(screen.getByText('Governance')).toBeInTheDocument();
  });

  it('shows the empty state when there are no grants', () => {
    mockUseGovernanceGrants.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('No grants configured')).toBeInTheDocument();
  });

  it('renders a grant row with grantee, role, and scope', () => {
    mockUseGovernanceGrants.mockReturnValue({ data: [sampleGrant], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('entra:owner-a')).toBeInTheDocument();
    expect(screen.getByText('Admin')).toBeInTheDocument();
    expect(screen.getByText('Fleet-wide')).toBeInTheDocument();
    expect(screen.getByText('All pillars')).toBeInTheDocument();
  });

  it('shows "Revoked" instead of the revoke button for a revoked grant', () => {
    mockUseGovernanceGrants.mockReturnValue({
      data: [{ ...sampleGrant, revokedAt: '2026-08-29T10:00:00Z', revokedByIdentity: 'entra:owner-a' }],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
      isFetching: false,
    });
    renderPage();
    expect(screen.getByText('Revoked')).toBeInTheDocument();
    expect(screen.queryByText('Revoke')).not.toBeInTheDocument();
  });

  it('shows an error state with a retry option', () => {
    mockUseGovernanceGrants.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn(), isFetching: false });
    renderPage();
    expect(screen.getByText('Failed to load Governance grants')).toBeInTheDocument();
  });

  it('calls the revoke mutation when Revoke is clicked', () => {
    const mutate = vi.fn();
    mockUseGovernanceGrants.mockReturnValue({ data: [sampleGrant], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUseRevoke.mockReturnValue({ mutate, isPending: false });

    renderPage();
    fireEvent.click(screen.getByText('Revoke'));

    expect(mutate).toHaveBeenCalledWith('grant-1');
  });

  it('opens the new-grant form and submits a grant', () => {
    const mutate = vi.fn();
    mockUseGovernanceGrants.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    mockUseGrant.mockReturnValue({ mutate, isPending: false });
    mockUseNamespaces.mockReturnValue({ data: [{ id: 'ns-1', name: 'contoso-prod', displayName: 'Contoso Prod' }] });

    renderPage();
    fireEvent.click(screen.getByText('New grant'));
    fireEvent.change(screen.getByPlaceholderText('entra:oid, ApiKey:name, or an OwnerId'), {
      target: { value: 'entra:owner-b' },
    });
    fireEvent.click(screen.getByText('Create grant'));

    expect(mutate).toHaveBeenCalledWith(
      { granteeIdentity: 'entra:owner-b', granteeKind: 'User', role: 'Viewer', namespaceId: null, pillarKind: null },
      expect.anything(),
    );
  });

  it('disables the "Create grant" button until a grantee identity is entered', () => {
    mockUseGovernanceGrants.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn(), isFetching: false });
    renderPage();
    fireEvent.click(screen.getByText('New grant'));
    expect(screen.getByText('Create grant')).toBeDisabled();
  });
});
