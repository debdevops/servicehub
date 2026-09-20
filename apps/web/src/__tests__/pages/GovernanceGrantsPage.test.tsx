import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import GovernanceGrantsPage from '@/pages/GovernanceGrantsPage';
import {
  useGovernanceGrants, useGrantGovernanceRole, useRevokeGovernanceGrant,
} from '@servicehub/ui-shared/hooks/useGovernanceGrants';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

vi.mock('@servicehub/ui-shared/hooks/useGovernanceGrants', () => ({
  useGovernanceGrants: vi.fn(),
  useGrantGovernanceRole: vi.fn(),
  useRevokeGovernanceGrant: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({ useNamespaces: vi.fn() }));
vi.mock('@servicehub/ui-shared/hooks/useMe', () => ({ useMe: vi.fn() }));
vi.mock('@servicehub/ui-shared/lib/demo/DemoContext', () => ({ useDemoContext: vi.fn() }));

const m = <T,>(fn: T) => fn as unknown as ReturnType<typeof vi.fn>;

const adminGrant = {
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

const scopedOperator = {
  ...adminGrant,
  id: 'grant-2',
  granteeIdentity: 'ApiKey:ci',
  granteeKind: 'ApiKey' as const,
  role: 'Operator' as const,
  namespaceId: 'ns-1',
  pillarKind: 'Recover' as const,
};

const grantMutate = vi.fn();
const revokeMutate = vi.fn();

function setup(data: object[] | undefined, extra: object = {}) {
  m(useGovernanceGrants).mockReturnValue({ data, isLoading: false, isError: false, error: null, refetch: vi.fn(), isFetching: false, ...extra });
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/governance']}>
      <GovernanceGrantsPage />
    </MemoryRouter>,
  );
}

describe('GovernanceGrantsPage', () => {
  beforeEach(() => {
    grantMutate.mockReset();
    revokeMutate.mockReset();
    m(useDemoContext).mockReturnValue({ isDemoMode: false, cloudProvider: null });
    m(useGrantGovernanceRole).mockReturnValue({ mutate: grantMutate, isPending: false });
    m(useRevokeGovernanceGrant).mockReturnValue({ mutate: revokeMutate, isPending: false });
    m(useNamespaces).mockReturnValue({ data: [{ id: 'ns-1', name: 'contoso-prod', displayName: 'Contoso Prod' }] });
    m(useMe).mockReturnValue({ data: { ownerId: 'owner-a', authMethod: 'Spa', governanceRole: 'Admin' } });
  });

  it('shows a loading state and the governance principle', () => {
    setup(undefined, { isLoading: true, isFetching: true });
    renderPage();
    expect(screen.getByRole('heading', { name: 'Governance' })).toBeInTheDocument();
    expect(screen.getByText('Governance does not grant autonomy.')).toBeInTheDocument();
  });

  it('warns that Governance is inactive when there are no grants', () => {
    setup([]);
    renderPage();
    expect(screen.getByText('No grants configured')).toBeInTheDocument();
    expect(screen.getByText('Governance is not active')).toBeInTheDocument();
  });

  it('groups grants into people, with role and scope', () => {
    setup([adminGrant, scopedOperator]);
    renderPage();
    const people = screen.getByRole('table', { name: 'People and access' });
    expect(within(people).getByText('entra:owner-a')).toBeInTheDocument();
    expect(within(people).getByText('Admin')).toBeInTheDocument();
    expect(within(people).getByText('Fleet-wide')).toBeInTheDocument();
    expect(within(people).getByText('Contoso Prod')).toBeInTheDocument(); // namespace name, not the raw id
    expect(within(people).getByText('Recover')).toBeInTheDocument();
  });

  it('shows what a selected person can and cannot do — including that no role can set autonomy', () => {
    setup([scopedOperator]);
    renderPage();
    fireEvent.click(within(screen.getByRole('table', { name: 'People and access' })).getByText('ApiKey:ci'));
    const panel = screen.getByRole('complementary', { name: 'Details' });
    expect(within(panel).getByText(/Replay messages, including approving from the Approval Queue/)).toBeInTheDocument();
    // Replay, write-off and elevation requests are all Operator/Recover actions, scoped to the one namespace.
    expect(within(panel).getAllByText(/— 1 namespace/)).toHaveLength(3);
    expect(within(panel).queryByText(/Grant and revoke Governance roles —/)).not.toBeInTheDocument();
    expect(within(panel).getByText("Set or raise a signature's autonomy level")).toBeInTheDocument();
    expect(within(panel).getAllByText(/no role can/).length).toBeGreaterThan(0);
  });

  it('asks for confirmation before revoking, then revokes', () => {
    setup([adminGrant]);
    renderPage();
    fireEvent.click(screen.getByRole('tab', { name: /Grants/ }));
    fireEvent.click(screen.getByRole('button', { name: /Revoke/ }));
    expect(revokeMutate).not.toHaveBeenCalled();
    expect(screen.getByText('Revoke this grant?')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Revoke grant' }));
    expect(revokeMutate).toHaveBeenCalledWith('grant-1', expect.anything());
  });

  it('shows "Revoked" instead of the revoke button for a revoked grant', () => {
    setup([{ ...adminGrant, revokedAt: '2026-08-29T10:00:00Z', revokedByIdentity: 'entra:owner-a' }]);
    renderPage();
    fireEvent.click(screen.getByRole('tab', { name: /Grants/ }));
    expect(screen.getByText('Revoked')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Revoke/ })).not.toBeInTheDocument();
  });

  it('explains a 403 as a permission state, not a failure, and still offers the role matrix', () => {
    m(useMe).mockReturnValue({ data: { ownerId: 'o', authMethod: 'ApiKey', governanceRole: 'Operator' } });
    setup(undefined, { isError: true, error: { response: { status: 403 } } });
    renderPage();
    expect(screen.getByText('Only a Governance Admin can see and manage grants')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /New grant/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'See what each role can do' }));
    expect(screen.getByRole('table', { name: 'Role permissions' })).toBeInTheDocument();
  });

  it('shows a generic error state for other failures', () => {
    setup(undefined, { isError: true, error: { response: { status: 500 } } });
    renderPage();
    expect(screen.getByText('Failed to load Governance grants')).toBeInTheDocument();
  });

  it('opens the new-grant dialog and submits a grant', () => {
    setup([adminGrant]);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /New grant/ }));
    const dialog = screen.getByRole('dialog');
    const create = within(dialog).getByRole('button', { name: /Create grant/ });
    expect(create).toBeDisabled();
    fireEvent.change(within(dialog).getByPlaceholderText('entra:oid, ApiKey:name, or an OwnerId'), { target: { value: 'entra:owner-b' } });
    fireEvent.click(create);
    expect(grantMutate).toHaveBeenCalledWith(
      { granteeIdentity: 'entra:owner-b', granteeKind: 'User', role: 'Viewer', namespaceId: null, pillarKind: null },
      expect.anything(),
    );
    expect(within(dialog).getByText(/never grants autonomy/)).toBeInTheDocument();
  });

  it('warns that the first grant turns Governance on and can lock you out', () => {
    setup([]);
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /Create first grant/ }));
    expect(screen.getByText('This first grant turns Governance on')).toBeInTheDocument();
  });

  it('shows the permission matrix with the actions nobody can take', () => {
    setup([adminGrant]);
    renderPage();
    fireEvent.click(screen.getByRole('tab', { name: 'Roles & permissions' }));
    const matrix = screen.getByRole('table', { name: 'Role permissions' });
    expect(within(matrix).getAllByText('Nobody, by design')).toHaveLength(2);
  });
});
