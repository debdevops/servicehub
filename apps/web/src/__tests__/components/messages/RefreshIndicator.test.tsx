import { render, screen } from '@testing-library/react';
import { RefreshIndicator } from '@/components/messages/RefreshIndicator';

describe('RefreshIndicator', () => {
  it('returns null when autoRefreshEnabled is false', () => {
    const { container } = render(
      <RefreshIndicator isRefreshing={false} autoRefreshEnabled={false} />
    );
    expect(container.firstChild).toBeNull();
  });

  it('returns null when autoRefreshEnabled is false even if isRefreshing is true', () => {
    const { container } = render(
      <RefreshIndicator isRefreshing={true} autoRefreshEnabled={false} />
    );
    expect(container.firstChild).toBeNull();
  });

  it('shows "Live" text when autoRefreshEnabled and not refreshing', () => {
    render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} />);
    expect(screen.getByText('Live')).toBeInTheDocument();
  });

  it('shows "Refreshing..." text when isRefreshing is true', () => {
    render(<RefreshIndicator isRefreshing={true} autoRefreshEnabled={true} />);
    expect(screen.getByText('Refreshing...')).toBeInTheDocument();
  });

  it('does not show "Live" while refreshing', () => {
    render(<RefreshIndicator isRefreshing={true} autoRefreshEnabled={true} />);
    expect(screen.queryByText('Live')).not.toBeInTheDocument();
  });

  it('shows blue styling when refreshing', () => {
    render(<RefreshIndicator isRefreshing={true} autoRefreshEnabled={true} />);
    const indicator = screen.getByText('Refreshing...').closest('div');
    expect(indicator?.className).toContain('bg-blue-50');
  });

  it('shows green styling when live (not refreshing)', () => {
    render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} />);
    const indicator = screen.getByText('Live').closest('div');
    expect(indicator?.className).toContain('bg-green-50');
  });

  it('shows "just now" for very recent lastUpdated', () => {
    const lastUpdated = Date.now() - 2000; // 2 seconds ago
    render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} lastUpdated={lastUpdated} />);
    expect(screen.getByText(/just now/)).toBeInTheDocument();
  });

  it('shows seconds ago for lastUpdated between 5-60 seconds', () => {
    const lastUpdated = Date.now() - 10000; // 10 seconds ago
    render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} lastUpdated={lastUpdated} />);
    expect(screen.getByText(/10s ago/)).toBeInTheDocument();
  });

  it('shows minutes ago for lastUpdated over 60 seconds', () => {
    const lastUpdated = Date.now() - 120000; // 2 minutes ago
    render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} lastUpdated={lastUpdated} />);
    expect(screen.getByText(/2m ago/)).toBeInTheDocument();
  });

  it('does not show time text when lastUpdated is undefined', () => {
    render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} />);
    expect(screen.queryByText(/ago/)).not.toBeInTheDocument();
    expect(screen.queryByText(/just now/)).not.toBeInTheDocument();
  });

  it('does not show time text while refreshing even with lastUpdated', () => {
    const lastUpdated = Date.now() - 5000;
    render(<RefreshIndicator isRefreshing={true} autoRefreshEnabled={true} lastUpdated={lastUpdated} />);
    expect(screen.queryByText(/ago/)).not.toBeInTheDocument();
  });

  it('applies fixed positioning', () => {
    const { container } = render(<RefreshIndicator isRefreshing={false} autoRefreshEnabled={true} />);
    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.classList.contains('fixed')).toBe(true);
  });
});
