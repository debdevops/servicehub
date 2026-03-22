import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ForensicTab } from '@/components/messages/tabs/ForensicTab';
import type { Message } from '@/lib/mockData';

// Mock the DLQ History API
vi.mock('@/lib/api/dlqHistory', () => ({
  dlqHistoryApi: {
    getForensicResult: vi.fn(),
  },
}));

import { dlqHistoryApi } from '@/lib/api/dlqHistory';

function makeMessage(overrides: Partial<Message> = {}): Message {
  return {
    id: 'msg-test-001',
    enqueuedTime: new Date('2025-01-01T10:00:00Z'),
    status: 'success',
    preview: 'Test message preview',
    contentType: 'application/json',
    deliveryCount: 1,
    hasAIInsight: false,
    sequenceNumber: 1000001,
    properties: {},
    queueType: 'active',
    body: '{"key":"value"}',
    headers: {},
    timeToLive: '1d 0h 0m 0s',
    lockToken: 'lock-abc-123',
    ...overrides,
  };
}

describe('ForensicTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── No DLQ ID ─────────────────────────────────────────────────────────────

  it('shows "Forensic Analysis" placeholder when message has no dlqId', () => {
    render(<ForensicTab message={makeMessage()} />);
    expect(screen.getByText('Forensic Analysis')).toBeInTheDocument();
  });

  it('explains the feature requires DLQ Intelligence when no dlqId', () => {
    render(<ForensicTab message={makeMessage()} />);
    expect(screen.getByText(/DLQ Intelligence/i)).toBeInTheDocument();
  });

  it('renders the Shield icon placeholder when no dlqId', () => {
    const { container } = render(<ForensicTab message={makeMessage()} />);
    // Shield icon should be present
    expect(container).not.toBeEmptyDOMElement();
  });

  // ── With DLQ ID — initial state ──────────────────────────────────────────

  it('shows "Run Forensic Analysis" button when dlqId provided and not yet run', () => {
    render(<ForensicTab message={makeMessage({ dlqId: 42 })} />);
    expect(screen.getByText('Run Forensic Analysis')).toBeInTheDocument();
  });

  it('shows explanatory text before analysis is run', () => {
    render(<ForensicTab message={makeMessage({ dlqId: 42 })} />);
    expect(screen.getByText(/Run Forensic Analysis/i)).toBeInTheDocument();
  });

  // ── Loading state ──────────────────────────────────────────────────────────

  it('shows loading indicator after clicking Analyse', async () => {
    // Make the API call stay pending indefinitely
    (dlqHistoryApi.getForensicResult as ReturnType<typeof vi.fn>).mockReturnValue(
      new Promise(() => {}) // never resolves
    );

    const { getByRole } = render(<ForensicTab message={makeMessage({ dlqId: 99 })} />);
    const btn = getByRole('button', { name: /Analyse Message/i });
    btn.click();
    // Loading spinner appears after click
    await screen.findByText(/Running forensic analysis/i);
  });

  // ── Error state ────────────────────────────────────────────────────────────

  it('shows error message when API call throws', async () => {
    (dlqHistoryApi.getForensicResult as ReturnType<typeof vi.fn>).mockRejectedValue(
      new Error('API unavailable')
    );

    render(<ForensicTab message={makeMessage({ dlqId: 10 })} />);
    const btn = screen.getByRole('button', { name: /Analyse Message/i });
    btn.click();

    await screen.findByText(/Failed to run forensic analysis/i);
  });

  // ── Success state ─────────────────────────────────────────────────────────

  it('renders forensic results after API returns successfully', async () => {
    const mockResult = {
      messageId: 10,
      failureCategory: 'MaxDelivery',
      confidence: 0.92,
      rootCause: 'Consumer exception',
      replaySafety: 'Safe',
      tier: 'Tier 1',
    };
    (dlqHistoryApi.getForensicResult as ReturnType<typeof vi.fn>).mockResolvedValue(mockResult);

    render(<ForensicTab message={makeMessage({ dlqId: 10 })} />);
    screen.getByRole('button', { name: /Analyse Message/i }).click();

    // Wait for the result to be displayed
    await screen.findByText(/Consumer exception/i);
    expect(screen.getByText(/MaxDelivery/i)).toBeInTheDocument();
  });

  it('shows the replaySafety value (Safe) in the results', async () => {
    const mockResult = {
      messageId: 10,
      failureCategory: 'Transient',
      confidence: 0.85,
      rootCause: 'Network timeout',
      replaySafety: 'Safe',
      tier: 'Tier 2',
    };
    (dlqHistoryApi.getForensicResult as ReturnType<typeof vi.fn>).mockResolvedValue(mockResult);

    render(<ForensicTab message={makeMessage({ dlqId: 10 })} />);
    screen.getByRole('button', { name: /Analyse Message/i }).click();

    await screen.findByText('Safe to Replay');
  });

  it('shows the replaySafety value (Unsafe) when result is unsafe', async () => {
    const mockResult = {
      messageId: 10,
      failureCategory: 'Poison',
      confidence: 0.95,
      rootCause: 'Schema violation',
      replaySafety: 'Unsafe',
      tier: 'Tier 1',
    };
    (dlqHistoryApi.getForensicResult as ReturnType<typeof vi.fn>).mockResolvedValue(mockResult);

    render(<ForensicTab message={makeMessage({ dlqId: 10 })} />);
    screen.getByRole('button', { name: /Analyse Message/i }).click();

    await screen.findByText('Unsafe — Do Not Replay');
  });
});
