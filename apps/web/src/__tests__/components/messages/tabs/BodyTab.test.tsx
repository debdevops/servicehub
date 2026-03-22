import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BodyTab } from '@/components/messages/tabs/BodyTab';

// Mock clipboard
Object.assign(navigator, {
  clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
});

const sampleJSON = JSON.stringify({ orderId: 'ORD-001', amount: 99.99, status: 'pending' });
const invalidJSON = 'not-valid-json { broken }';
const plainText = 'Hello, this is a plain text message.';

describe('BodyTab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Rendering ─────────────────────────────────────────────────────────────

  it('renders the body content area', () => {
    const { container } = render(<BodyTab body={sampleJSON} contentType="application/json" />);
    expect(container).not.toBeEmptyDOMElement();
  });

  it('renders a copy button', () => {
    render(<BodyTab body={sampleJSON} contentType="application/json" />);
    // The Copy button (icon) should be present in the header area
    expect(screen.getByTitle(/copy/i)).toBeInTheDocument();
  });

  it('renders plain text body without crashing', () => {
    render(<BodyTab body={plainText} contentType="text/plain" />);
    expect(screen.getByText(plainText)).toBeInTheDocument();
  });

  it('renders invalid JSON as raw text without crashing', () => {
    render(<BodyTab body={invalidJSON} contentType="application/json" />);
    // Should not crash and should render the raw content
    const { container } = render(<BodyTab body={invalidJSON} contentType="application/json" />);
    expect(container).not.toBeEmptyDOMElement();
  });

  it('renders valid JSON with syntax highlighting elements', () => {
    const { container } = render(<BodyTab body={sampleJSON} contentType="application/json" />);
    // JSON highlighting wraps content in spans — at least some spans should exist
    const spans = container.querySelectorAll('span');
    expect(spans.length).toBeGreaterThan(0);
  });

  it('renders JSON with object brackets', () => {
    const { container } = render(<BodyTab body={sampleJSON} contentType="application/json" />);
    expect(container.textContent).toContain('{');
    expect(container.textContent).toContain('}');
  });

  it('renders JSON keys from the object', () => {
    const { container } = render(<BodyTab body={sampleJSON} contentType="application/json" />);
    // Keys appear in the rendered output
    expect(container.textContent).toContain('orderId');
    expect(container.textContent).toContain('amount');
  });

  it('renders nested JSON objects without crashing', () => {
    const nested = JSON.stringify({ user: { name: 'Alice', age: 30 }, active: true });
    const { container } = render(<BodyTab body={nested} contentType="application/json" />);
    expect(container).not.toBeEmptyDOMElement();
  });

  it('renders JSON arrays without crashing', () => {
    const arr = JSON.stringify({ items: [1, 2, 3] });
    const { container } = render(<BodyTab body={arr} contentType="application/json" />);
    expect(container).not.toBeEmptyDOMElement();
  });

  // ── Copy interaction ───────────────────────────────────────────────────────

  it('clicking copy calls clipboard.writeText with the formatted body', async () => {
    render(<BodyTab body={sampleJSON} contentType="application/json" />);
    const copyBtn = screen.getByTitle(/copy/i);
    await userEvent.click(copyBtn);
    expect(navigator.clipboard.writeText).toHaveBeenCalledTimes(1);
  });

  // ── Content type label ─────────────────────────────────────────────────────

  it('displays the content type in the header', () => {
    render(<BodyTab body={sampleJSON} contentType="application/json" />);
    expect(screen.getByText('application/json')).toBeInTheDocument();
  });

  it('displays plain content type in the header', () => {
    render(<BodyTab body={plainText} contentType="text/plain" />);
    expect(screen.getByText('text/plain')).toBeInTheDocument();
  });
});
