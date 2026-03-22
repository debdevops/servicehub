import React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ErrorBoundary } from '@/components/ErrorBoundary';

// Helper component that unconditionally throws in render
function ThrowError({ message }: { message: string }): React.ReactElement {
  throw new Error(message);
}

// Guard against React's intentional double-render noise from ErrorBoundary
beforeEach(() => {
  vi.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('ErrorBoundary', () => {
  // ── Happy path ────────────────────────────────────────────────────────────

  it('renders children when no error is thrown', () => {
    render(
      <ErrorBoundary>
        <span>child content</span>
      </ErrorBoundary>
    );
    expect(screen.getByText('child content')).toBeInTheDocument();
  });

  // ── Error UI ──────────────────────────────────────────────────────────────

  it('renders "Something went wrong" heading when a child throws', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="boom" />
      </ErrorBoundary>
    );
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });

  it('renders the "Try Again" reset button', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="boom" />
      </ErrorBoundary>
    );
    expect(screen.getByText('Try Again')).toBeInTheDocument();
  });

  it('renders the "Reload Page" button', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="boom" />
      </ErrorBoundary>
    );
    expect(screen.getByText('Reload Page')).toBeInTheDocument();
  });

  // ── Custom fallback ───────────────────────────────────────────────────────

  it('renders the custom fallback element instead of the default error UI', () => {
    render(
      <ErrorBoundary fallback={<div>Custom fallback</div>}>
        <ThrowError message="boom" />
      </ErrorBoundary>
    );
    expect(screen.getByText('Custom fallback')).toBeInTheDocument();
    expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
  });

  // ── Error messages ────────────────────────────────────────────────────────

  it('shows a network-related friendly message for network errors', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="network request failed" />
      </ErrorBoundary>
    );
    expect(screen.getByText(/Unable to connect to the API/i)).toBeInTheDocument();
  });

  it('shows a friendly message for unauthorized errors', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="unauthorized access" />
      </ErrorBoundary>
    );
    expect(screen.getByText(/session has expired/i)).toBeInTheDocument();
  });

  it('shows a friendly message for timeout errors', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="timeout occurred" />
      </ErrorBoundary>
    );
    expect(screen.getByText(/took too long/i)).toBeInTheDocument();
  });

  it('shows a friendly message for service bus errors', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="service bus communication failed" />
      </ErrorBoundary>
    );
    expect(screen.getByText(/Azure Service Bus/i)).toBeInTheDocument();
  });

  it('shows a friendly message for connection errors', () => {
    render(
      <ErrorBoundary>
        <ThrowError message="connection lost" />
      </ErrorBoundary>
    );
    // The text appears in both the friendly <p> and the Technical Details <pre> in dev mode.
    // Verify the friendly message paragraph is present.
    expect(screen.getAllByText(/Connection lost/i).length).toBeGreaterThan(0);
    const errorParagraph = document.querySelector('p.text-gray-600') as HTMLElement;
    expect(errorParagraph).toHaveTextContent('Connection lost');
  });

  // ── Reset behavior ────────────────────────────────────────────────────────

  it('"Try Again" clears the error state and shows children again', async () => {
    // Keep throwing until explicit permission is granted. React 18 dev mode
    // re-invokes components multiple times before the error boundary commits,
    // so a simple flag-flip-on-first-throw would clear too early.
    let allowRender = false;

    function MaybeThrow() {
      if (!allowRender) {
        throw new Error('persistent render error');
      }
      return <span>recovered child</span>;
    }

    render(
      <ErrorBoundary>
        <MaybeThrow />
      </ErrorBoundary>
    );

    // Error boundary should be showing the fallback UI
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();

    // Allow renders to succeed, then trigger reset
    allowRender = true;
    await userEvent.click(screen.getByText('Try Again'));
    expect(screen.getByText('recovered child')).toBeInTheDocument();
  });
});
