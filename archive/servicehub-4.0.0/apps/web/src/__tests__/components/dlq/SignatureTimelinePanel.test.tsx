import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SignatureTimelinePanel } from '@/components/dlq/SignatureTimelinePanel';
import type { DlqTimelineEvent } from '@servicehub/ui-shared/lib/api/dlqHistory';

describe('SignatureTimelinePanel', () => {
  it('renders a message when there are no events', () => {
    render(<SignatureTimelinePanel events={[]} />);
    expect(screen.getByText('No timeline events available.')).toBeInTheDocument();
  });

  it('renders each event with its type and description', () => {
    const events: DlqTimelineEvent[] = [
      { eventType: 'SignatureFirstObserved', description: 'Signature first observed', timestamp: '2026-01-01T00:00:00Z', details: null },
      { eventType: 'StatusChanged', description: 'Status changed from Active to Resolved', timestamp: '2026-01-02T00:00:00Z', details: { From: 'Active', To: 'Resolved' } },
    ];
    render(<SignatureTimelinePanel events={events} />);

    expect(screen.getByText('SignatureFirstObserved')).toBeInTheDocument();
    expect(screen.getByText('Signature first observed')).toBeInTheDocument();
    expect(screen.getByText('StatusChanged')).toBeInTheDocument();
    expect(screen.getByText('Status changed from Active to Resolved')).toBeInTheDocument();
  });

  it('renders event details as chips', () => {
    const events: DlqTimelineEvent[] = [
      { eventType: 'StatusChanged', description: 'transition', timestamp: '2026-01-02T00:00:00Z', details: { From: 'Active', To: 'Resolved' } },
    ];
    render(<SignatureTimelinePanel events={events} />);

    expect(screen.getByText('From:')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('To:')).toBeInTheDocument();
    expect(screen.getByText('Resolved')).toBeInTheDocument();
  });
});
