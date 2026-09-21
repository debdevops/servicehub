import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PropertiesTab } from '@/components/messages/tabs/PropertiesTab';
import type { Message } from '@servicehub/ui-shared/lib/mockData';

function renderWithRouter(ui: React.ReactElement) {
  return render(<MemoryRouter>{ui}</MemoryRouter>);
}

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
    const { container } = renderWithRouter(<PropertiesTab message={makeMessage()} />);
    expect(container).not.toBeEmptyDOMElement();
  });

  it('does NOT show DLQ panel for an active message', () => {
    renderWithRouter(<PropertiesTab message={makeMessage({ queueType: 'active' })} />);
    expect(screen.queryByText('Dead-Letter Queue Message')).not.toBeInTheDocument();
  });

  it('renders the sequence number label', () => {
    renderWithRouter(<PropertiesTab message={makeMessage({ sequenceNumber: 1234567 })} />);
    expect(screen.getByText('Sequence Number')).toBeInTheDocument();
  });

  it('renders the delivery count with current session note', () => {
    renderWithRouter(<PropertiesTab message={makeMessage({ deliveryCount: 3 })} />);
    expect(screen.getByText(/3 \(current session\)/)).toBeInTheDocument();
  });

  it('renders the content type', () => {
    renderWithRouter(<PropertiesTab message={makeMessage({ contentType: 'application/json' })} />);
    expect(screen.getByText('application/json')).toBeInTheDocument();
  });

  it('renders the lock token', () => {
    renderWithRouter(<PropertiesTab message={makeMessage({ lockToken: 'lock-test-xyz' })} />);
    expect(screen.getByText('lock-test-xyz')).toBeInTheDocument();
  });

  it('renders the time to live', () => {
    renderWithRouter(<PropertiesTab message={makeMessage({ timeToLive: '7d 0h 0m 0s' })} />);
    expect(screen.getByText('7d 0h 0m 0s')).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: warning severity', () => {
  const dlqMessage = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'MaxDeliveryCountExceeded',
    deadLetterSource: 'OrchestrationQueue',
    deadLetterErrorDescription: 'Session lock expired before message could be settled',
    deliveryCount: 3,
  });

  it('shows the "Dead-Letter Queue Message" heading', () => {
    renderWithRouter(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText('Dead-Letter Queue Message')).toBeInTheDocument();
  });

  it('renders the DeadLetterReason value', () => {
    renderWithRouter(<PropertiesTab message={dlqMessage} />);
    // Value appears in both the fact section and the PropertyRow — use getAllByText
    const matches = screen.getAllByText('MaxDeliveryCountExceeded');
    expect(matches.length).toBeGreaterThanOrEqual(1);
  });

  it('renders the DeadLetterErrorDescription label', () => {
    renderWithRouter(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText('DeadLetterErrorDescription')).toBeInTheDocument();
  });

  it('renders the DeadLetterErrorDescription value, not the DeadLetterSource value', () => {
    renderWithRouter(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText('Session lock expired before message could be settled')).toBeInTheDocument();
  });

  it('shows the "Warning" severity label for low delivery count DLQ message', () => {
    renderWithRouter(<PropertiesTab message={dlqMessage} />);
    expect(screen.getByText(/Warning/i)).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: critical severity', () => {
  const criticalDlq = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'MaxDeliveryCountExceeded',
    deadLetterSource: 'PaymentsQueue',
    deadLetterErrorDescription: 'Downstream payment gateway returned HTTP 503',
    deliveryCount: 8,
  });

  it('shows the "Critical" severity badge for high delivery count DLQ message', () => {
    renderWithRouter(<PropertiesTab message={criticalDlq} />);
    expect(screen.getByText(/Critical/i)).toBeInTheDocument();
  });

  it('renders delivery count correctly', () => {
    renderWithRouter(<PropertiesTab message={criticalDlq} />);
    expect(screen.getByText('8')).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter message: test severity', () => {
  const testDlq = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'test - manual inspection',
    deadLetterSource: 'ServiceHub Testing',
    deadLetterErrorDescription: 'Manually dead-lettered for inspection',
    deliveryCount: 1,
  });

  it('shows "Test/Manual" severity badge', () => {
    renderWithRouter(<PropertiesTab message={testDlq} />);
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
    renderWithRouter(<PropertiesTab message={incompleteMessage} />);
    expect(screen.getByText('Incomplete Azure Data')).toBeInTheDocument();
  });
});

describe('PropertiesTab — dead-letter fields are mapped to distinct labels', () => {
  const distinctFieldsMessage = makeMessage({
    queueType: 'deadletter',
    deadLetterSource: 'arn:aws:sqs:ap-south-1:123:orders',
    deadLetterReason: 'PaymentTimeout',
    deadLetterErrorDescription: 'Payment service timed out',
    deliveryCount: 2,
  });

  it('renders DeadLetterReason under its own label with the reason value', () => {
    renderWithRouter(<PropertiesTab message={distinctFieldsMessage} />);
    expect(screen.getByText('DeadLetterReason')).toBeInTheDocument();
    expect(screen.getAllByText('PaymentTimeout').length).toBeGreaterThanOrEqual(1);
  });

  it('renders DeadLetterErrorDescription under its own label with the error-description value, not the source', () => {
    renderWithRouter(<PropertiesTab message={distinctFieldsMessage} />);
    expect(screen.getByText('DeadLetterErrorDescription')).toBeInTheDocument();
    expect(screen.getByText('Payment service timed out')).toBeInTheDocument();
    expect(screen.queryByText('arn:aws:sqs:ap-south-1:123:orders', { selector: '.font-mono' })).not.toBeInTheDocument();
  });

  it('renders Dead-Letter Source under the "Complete Message Properties" section with the source value', () => {
    renderWithRouter(<PropertiesTab message={distinctFieldsMessage} />);
    expect(screen.getByText('Dead-Letter Source')).toBeInTheDocument();
    expect(screen.getByText('arn:aws:sqs:ap-south-1:123:orders')).toBeInTheDocument();
  });
});

describe('PropertiesTab — AWS native-redrive: source present, error description absent', () => {
  const awsNativeRedriveMessage = makeMessage({
    queueType: 'deadletter',
    deadLetterReason: 'MaxReceiveCount exceeded',
    deadLetterSource: 'arn:aws:sqs:us-east-1:456:payments-queue',
    deadLetterErrorDescription: undefined,
    deliveryCount: 5,
  });

  it('does not substitute the source ARN into the DeadLetterErrorDescription value', () => {
    renderWithRouter(<PropertiesTab message={awsNativeRedriveMessage} />);
    const factSection = screen.getByText('DeadLetterErrorDescription').closest('div')?.parentElement;
    expect(factSection).not.toBeNull();
    expect(factSection?.textContent).not.toContain('arn:aws:sqs:us-east-1:456:payments-queue');
  });

  it('shows the incomplete-data warning instead of a fabricated description', () => {
    renderWithRouter(<PropertiesTab message={awsNativeRedriveMessage} />);
    expect(screen.getByText('Incomplete Azure Data')).toBeInTheDocument();
  });
});

describe('PropertiesTab — message properties section', () => {
  it('renders correlationId from message.properties', () => {
    const msg = makeMessage({ properties: { correlationId: 'corr-xyz-987' } });
    renderWithRouter(<PropertiesTab message={msg} />);
    expect(screen.getByText('corr-xyz-987')).toBeInTheDocument();
  });

  it('renders custom properties when present', () => {
    const msg = makeMessage({ properties: { env: 'production', version: '2.0' } });
    renderWithRouter(<PropertiesTab message={msg} />);
    expect(screen.getByText('Custom Application Properties')).toBeInTheDocument();
    expect(screen.getByText('production')).toBeInTheDocument();
  });
});
