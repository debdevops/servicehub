import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { FailureIntelligenceCenterPage } from '@/pages/FailureIntelligenceCenterPage';
import { DemoModeProvider } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type { IncidentListItem, IncidentListResponse } from '@servicehub/ui-shared/hooks/useIncidentsList';

vi.mock('@servicehub/ui-shared/hooks/useIncidentsList', () => ({
  useIncidentsList: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(),
}));
vi.mock('@servicehub/ui-shared/hooks/useDlqSignatures', () => ({
  useDlqSignatureDetail: vi.fn(),
  useRootCauseMatches: vi.fn(),
}));

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

import { useIncidentsList } from '@servicehub/ui-shared/hooks/useIncidentsList';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useDlqSignatureDetail, useRootCauseMatches } from '@servicehub/ui-shared/hooks/useDlqSignatures';

const mockUseIncidentsList = useIncidentsList as ReturnType<typeof vi.fn>;
const mockUseNamespaces = useNamespaces as ReturnType<typeof vi.fn>;
const mockUseDlqSignatureDetail = useDlqSignatureDetail as ReturnType<typeof vi.fn>;
const mockUseRootCauseMatches = useRootCauseMatches as ReturnType<typeof vi.fn>;

const EMPTY_METRICS = {
  totalSignatures: 0,
  activeSignatures: 0,
  resolvedSignatures: 0,
  suppressedSignatures: 0,
  archivedSignatures: 0,
  requiresAction: 0,
};

function item(overrides: Partial<IncidentListItem> = {}): IncidentListItem {
  return {
    signatureHash: 'hash-1',
    namespaceId: 'ns-1',
    namespaceName: 'DEVAWS',
    cloudProvider: 'aws',
    environment: 'dev',
    displayName: 'ProcessingError (ID: hash-1)',
    category: 'ProcessingError',
    severity: 'High',
    status: 'Active',
    isEscalating: false,
    messageCount: 12,
    firstSeenAt: new Date().toISOString(),
    lastSeenAt: new Date().toISOString(),
    hasKnowledge: true,
    owner: null,
    recommendedNextAction: 'Investigate',
    ...overrides,
  };
}

function makeResponse(items: IncidentListItem[]): IncidentListResponse {
  const metrics = {
    ...EMPTY_METRICS,
    totalSignatures: items.length,
    activeSignatures: items.filter((i) => i.status === 'Active').length,
    requiresAction: items.filter((i) => i.status === 'Active' || i.status === 'Reopened').length,
  };
  return {
    metrics,
    trend: [],
    topCategories: [],
    items,
    generatedAt: new Date().toISOString(),
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <FailureIntelligenceCenterPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseNamespaces.mockReturnValue({ data: [] });
  mockUseDlqSignatureDetail.mockReturnValue({ data: undefined, isLoading: false });
  mockUseRootCauseMatches.mockReturnValue({ data: undefined, isLoading: false });
});

describe('FailureIntelligenceCenterPage — empty state', () => {
  it('shows an empty message when there are no incidents in the Active tab', () => {
    mockUseIncidentsList.mockReturnValue({ data: makeResponse([]), isLoading: false, isFetching: false, isError: false, refetch: vi.fn() });

    renderPage();

    expect(screen.getByText('No incidents match the current filters.')).toBeInTheDocument();
  });
});

// Regression coverage for the same class of drift bug the pre-redesign page's own tests
// guarded against: every "Investigate"/"Knowledge"/"Replay" destination must land on the
// Incident Workspace or Signature Details page, never a dead-end, and must respect Demo Mode's
// /demo/{provider} prefix.
describe('FailureIntelligenceCenterPage — action destinations', () => {
  it('sends the row\'s Investigate action to the Incident Workspace', () => {
    mockUseIncidentsList.mockReturnValue({ data: makeResponse([item()]), isLoading: false, isFetching: false, isError: false, refetch: vi.fn() });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Investigate ProcessingError (ID: hash-1)' }));
    expect(mockNavigate).toHaveBeenCalledWith('/incidents/hash-1?namespace=ns-1');
  });

  it('sends the row menu\'s Knowledge and Replay actions to the right destinations', () => {
    mockUseIncidentsList.mockReturnValue({ data: makeResponse([item({ hasKnowledge: false })]), isLoading: false, isFetching: false, isError: false, refetch: vi.fn() });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'More actions for ProcessingError (ID: hash-1)' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add knowledge for ProcessingError (ID: hash-1)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/signatures/hash-1?namespace=ns-1');

    fireEvent.click(screen.getByRole('button', { name: 'More actions for ProcessingError (ID: hash-1)' }));
    fireEvent.click(screen.getByRole('button', { name: 'Replay preview for ProcessingError (ID: hash-1)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-1?namespace=ns-1&tab=recovery');
  });

  it('sends the detail panel\'s Investigate/Knowledge/Replay actions to the same destinations', () => {
    mockUseIncidentsList.mockReturnValue({ data: makeResponse([item()]), isLoading: false, isFetching: false, isError: false, refetch: vi.fn() });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: 'Investigate (detail panel)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-1?namespace=ns-1');

    fireEvent.click(screen.getByRole('button', { name: 'Update knowledge (detail panel)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/signatures/hash-1?namespace=ns-1');

    fireEvent.click(screen.getByRole('button', { name: 'Replay (detail panel)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/incidents/hash-1?namespace=ns-1&tab=recovery');
  });

  it('applies the /demo/{provider} prefix to every destination in Demo Mode', () => {
    mockUseIncidentsList.mockReturnValue({ data: makeResponse([item()]), isLoading: false, isFetching: false, isError: false, refetch: vi.fn() });

    render(
      <MemoryRouter initialEntries={['/demo/aws/incidents']}>
        <DemoModeProvider cloudProvider="aws">
          <FailureIntelligenceCenterPage />
        </DemoModeProvider>
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Investigate ProcessingError (ID: hash-1)' }));
    expect(mockNavigate).toHaveBeenLastCalledWith('/demo/aws/incidents/hash-1?namespace=ns-1');
  });
});

describe('FailureIntelligenceCenterPage — status tabs', () => {
  it('moves a resolved signature out of the Active tab and into the Resolved tab', () => {
    mockUseIncidentsList.mockReturnValue({
      data: makeResponse([item({ signatureHash: 'hash-resolved', status: 'Resolved', displayName: 'Resolved (ID: hash-resolved)' })]),
      isLoading: false,
      isFetching: false,
      isError: false,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('No incidents match the current filters.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Resolved/ }));
    expect(screen.getAllByText('Resolved (ID: hash-resolved)').length).toBeGreaterThan(0);
  });
});
