import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PropertiesTab } from '@/components/messages/tabs/PropertiesTab';
import type { Message } from '@/lib/mockData';

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
    properties: { correlationId: 'corr-abc', source: 'OrderService' },
    queueType: 'active',
    body: '{"key":"value"}',
    headers: {},
    timeToLive: '1d 0h 0m 0s',
    lockToken: 'lock-abc-123-def',
    ...overrides,
  };
}

describe('PropertiesTab — active message', () => {
  it('renders without crashing', () => {
    const { container } = render(<PropertiesTab message={makeMessage()} />);
    expect(container).not.toBeEmptyDOMElement();
  });

  it('does NOT show DLQ panel for an active message', () => {
    render(<PropertiesTab message={makeMessage({ queueType: 'active' })} />);
    expect(screen.queryByText('Dead-Letter Queue Message')).not.toBeInTheDocument();
  });

  it('renders the sequence number label', () => {
    render(<PropertiesTab message={makeMessage({ sequenceNumber: 1234567 })} />);
    expect(screen.getByText('Sequence Number')).toBeInTheDocument();
  });

  it('renders the delivery count with current session note', () => {
    render(<PropertiesTab message={makeMessage({ deliveryCount: 3 })} />);
    expect(screen.getByText(/3 \(current session\)/)).toBeInTheDocument();
  });

  it('renders the content type', () => {
    render(<PropertiesTab message={makeMessage({ contentType: 'application/json' })} />);
    expect(screen.getByText('application/json')).toBeInTheDocument();
  });

  it('renders the lock token', () => {
    render(<PropertiesTab message={makeMessage({ lockToken: 'lock-test-xyz' })} />);
    expect(screen.getByText('lock-test-xyz')).toBeInTheDocument();
  });

  it('renders the time to live', () => {
    render(<PropertiesTab message={makeMessage({ timeToLive: '7d 0h 0m 0s' })} />);
    expect(screen.getByText('7d 0h 0m 0s')).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: warning severity', () => {
  const dlqMessage = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'MaxDeliveryCountExceeded',
    deadLetterSource: 'OrchestrationQueue',
    deliveryCount: 3,
  });

  it('shows the "Dead-Letter Queue Message" heading', () => {
    render(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText('Dead-Letter Queue Message')).toBeInTheDocument();
  });

  it('renders the DeadLetterReason value', () => {
    render(<PropertiesTab message={dlqMessage} />);
    // Value appears in both the fact section and the PropertyRow — use getAllByText
    const matches = screen.getAllByText('MaxDeliveryCountExceeded');
    expect(matches.length).toBeGreaterThanOrEqual(1);
  });

  it('renders the DeadLetterErrorDescription label', () => {
    render(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText('DeadLetterErrorDescription')).toBeInTheDocument();
  });

  it('shows the "Warning" severity label for low delivery count DLQ message', () => {
    render(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText(/Warning/i)).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: critical severity', () => {
  const criticalDlq = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'MaxDeliveryCountExceeded',
    deadLetterSource: 'PaymentsQueue',
    deliveryCount: 8,
  });

  it('shows the "Critical" severity badge for high delivery count DLQ message', () => {
    render(<PropertiesTab message={criticalDlq} />);
    expect(screen.getByText(/Critical/i)).toBeInTheDocument();
  });

  it('renders delivery count correctly', () => {
    render(<PropertiesTab message={criticalDlq} />);
    expect(screen.getByText('8')).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: test severity', () => {
  const testDlq = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'test - manual inspection',
    deadLetterSource: 'ServiceHub Testing',
    deliveryCount: 1,
  });

  it('shows "Test/Manual" severity badge', () => {
    render(<PropertiesTab message={testDlq} />);
    expect(screen.getByText(/Test\/Manual/i)).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: incomplete metadata', () => {
  const incompleteMessage = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: '',         // empty string
    deadLetterSource: undefined,  // missing
    deliveryCount: 2,
  });

  it('shows "Incomplete Azure Data" warning', () => {
    render(<PropertiesTab message={incompleteMessage} />);
    expect(screen.getByText('Incomplete Azure Data')).toBeInTheDocument();
  });
});

describe('PropertiesTab — message properties section', () => {
  it('renders correlationId from message.properties', () => {
    const msg = makeMessage({ properties: { correlationId: 'corr-xyz-987' } });
    render(<PropertiesTab message={msg} />);
    expect(screen.getByText('corr-xyz-987')).toBeInTheDocument();
  });

  it('renders custom properties when present', () => {
    const msg = makeMessage({ properties: { env: 'production', version: '2.0' } });
    render(<PropertiesTab message={msg} />);
    expect(screen.getByText('Custom Application Properties')).toBeInTheDocument();
    expect(screen.getByText('production')).toBeInTheDocument();
  });
});
