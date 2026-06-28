import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { DemoModeBanner } from '@/components/layout/DemoModeBanner';
import { DemoModeProvider } from '@/lib/demo/DemoContext';

function renderBanner(cloudProvider: 'azure' | 'aws' | 'gcp') {
  return render(
    <BrowserRouter>
      <DemoModeProvider cloudProvider={cloudProvider}>
        <DemoModeBanner />
      </DemoModeProvider>
    </BrowserRouter>
  );
}

describe('DemoModeBanner', () => {
  it('renders nothing when outside DemoModeProvider', () => {
    render(
      <BrowserRouter>
        <DemoModeBanner />
      </BrowserRouter>
    );
    expect(screen.queryByTestId('demo-mode-banner')).not.toBeInTheDocument();
  });

  it('renders the banner when isDemoMode=true (Azure)', () => {
    renderBanner('azure');
    const banner = screen.getByTestId('demo-mode-banner');
    expect(banner).toBeInTheDocument();
    expect(banner).toHaveTextContent('Demo Mode');
  });

  it('shows Azure cloud provider name', () => {
    renderBanner('azure');
    expect(screen.getByText(/azure service bus/i)).toBeInTheDocument();
  });

  it('shows AWS cloud provider name', () => {
    renderBanner('aws');
    expect(screen.getByText(/aws sqs/i)).toBeInTheDocument();
  });

  it('shows GCP cloud provider name', () => {
    renderBanner('gcp');
    expect(screen.getByText(/gcp pub\/sub/i)).toBeInTheDocument();
  });

  it('renders a "Connect Real" link pointing to /connect', () => {
    renderBanner('azure');
    const link = screen.getByRole('link', { name: /connect real azure/i });
    expect(link).toHaveAttribute('href', '/connect');
  });

  it('renders a "Connect Real AWS" link for AWS provider', () => {
    renderBanner('aws');
    const link = screen.getByRole('link', { name: /connect real aws/i });
    expect(link).toHaveAttribute('href', '/connect');
  });

  it('renders a "Connect Real GCP" link for GCP provider', () => {
    renderBanner('gcp');
    const link = screen.getByRole('link', { name: /connect real gcp/i });
    expect(link).toHaveAttribute('href', '/connect');
  });

  it('has role="banner" for accessibility', () => {
    renderBanner('azure');
    expect(screen.getByRole('banner')).toBeInTheDocument();
  });
});
