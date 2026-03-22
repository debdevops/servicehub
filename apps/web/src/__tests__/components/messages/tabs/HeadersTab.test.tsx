import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HeadersTab } from '@/components/messages/tabs/HeadersTab';

// Mock the clipboard API
Object.assign(navigator, {
  clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
});

describe('HeadersTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Empty state ──────────────────────────────────────────────────────────

  it('shows "No Headers" message when headers object is empty', () => {
    render(<HeadersTab headers={{}} />);
    expect(screen.getByText('No Headers')).toBeInTheDocument();
  });

  it('shows the explanatory text when headers is empty', () => {
    render(<HeadersTab headers={{}} />);
    expect(screen.getByText('This message has no custom headers')).toBeInTheDocument();
  });

  // ── Table rendering ──────────────────────────────────────────────────────

  it('renders a table with Header Name and Value columns', () => {
    render(<HeadersTab headers={{ 'Content-Type': 'application/json' }} />);
    expect(screen.getByText('Header Name')).toBeInTheDocument();
    expect(screen.getByText('Value')).toBeInTheDocument();
  });

  it('renders all header names', () => {
    const headers = {
      'Content-Type': 'application/json',
      'Correlation-Id': 'abc-123',
      'Session-Id': 'session-001',
    };
    render(<HeadersTab headers={headers} />);
    expect(screen.getByText('Content-Type')).toBeInTheDocument();
    expect(screen.getByText('Correlation-Id')).toBeInTheDocument();
    expect(screen.getByText('Session-Id')).toBeInTheDocument();
  });

  it('renders all header values', () => {
    const headers = {
      'Content-Type': 'application/json',
      'Correlation-Id': 'abc-123',
    };
    render(<HeadersTab headers={headers} />);
    expect(screen.getByText('application/json')).toBeInTheDocument();
    expect(screen.getByText('abc-123')).toBeInTheDocument();
  });

  it('renders one row per header entry', () => {
    const headers = { A: '1', B: '2', C: '3' };
    render(<HeadersTab headers={headers} />);
    // Verify we see all three values in the table body
    expect(screen.getByText('1')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  // ── Copy button ──────────────────────────────────────────────────────────

  it('renders a copy button for each header row', () => {
    render(<HeadersTab headers={{ 'X-Custom': 'hello' }} />);
    const copyBtns = screen.getAllByTitle('Copy value');
    expect(copyBtns.length).toBe(1);
  });

  it('calls clipboard.writeText with the header value when copy is clicked', async () => {
    render(<HeadersTab headers={{ 'X-Key': 'my-value' }} />);
    const copyBtn = screen.getByTitle('Copy value');
    await userEvent.click(copyBtn);
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('my-value');
  });
});
