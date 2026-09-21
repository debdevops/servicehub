import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { CrossCloudTraceLink } from '@/components/dlq/CrossCloudTraceLink';

describe('CrossCloudTraceLink', () => {
  it('renders nothing when correlationId is null', () => {
    const { container } = render(<CrossCloudTraceLink correlationId={null} />, { wrapper: MemoryRouter });
    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when correlationId is undefined', () => {
    const { container } = render(<CrossCloudTraceLink />, { wrapper: MemoryRouter });
    expect(container).toBeEmptyDOMElement();
  });

  it('links to /cross-cloud-trace with the correlation ID as traceId when present', () => {
    render(<CrossCloudTraceLink correlationId="trace-abc-123" />, { wrapper: MemoryRouter });

    const link = screen.getByRole('link', { name: /View cross-cloud path/ });
    expect(link).toHaveAttribute('href', '/cross-cloud-trace?traceId=trace-abc-123');
  });

  it('URL-encodes the correlation ID', () => {
    render(<CrossCloudTraceLink correlationId="trace with spaces" />, { wrapper: MemoryRouter });

    const link = screen.getByRole('link', { name: /View cross-cloud path/ });
    expect(link).toHaveAttribute('href', '/cross-cloud-trace?traceId=trace%20with%20spaces');
  });
});
