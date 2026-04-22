import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { InsightsPage } from '@/pages/InsightsPage';
import { MOCK_INSIGHTS } from '@/lib/insightsMockData';

vi.mock('@/hooks/useNamespaces', () => ({
  useNamespaces: vi.fn(() => ({
    data: [{ id: '1', name: 'Test Namespace', displayName: 'Test Namespace' }],
    isLoading: false,
  })),
}));

describe('InsightsPage', () => {
  it('renders the AI Insights Dashboard heading', () => {
    render(<InsightsPage />);
    expect(screen.getByText('AI Insights Dashboard')).toBeInTheDocument();
  });

  it('shows namespace in header', () => {
    render(<InsightsPage />);
    expect(screen.getByText('Test Namespace')).toBeInTheDocument();
    expect(screen.getByText('Namespace:')).toBeInTheDocument();
  });

  it('renders the insights sidebar', () => {
    render(<InsightsPage />);
    expect(screen.getByText('Issue Categories')).toBeInTheDocument();
  });

  it('shows Critical Issues title by default', () => {
    render(<InsightsPage />);
    expect(screen.getByText('Critical Issues Requiring Attention')).toBeInTheDocument();
  });

  it('shows critical insights by default', () => {
    render(<InsightsPage />);
    const criticalInsights = MOCK_INSIGHTS.filter(i => i.category === 'critical');
    // Each insight card renders its title
    criticalInsights.forEach(insight => {
      expect(screen.getByText(insight.title)).toBeInTheDocument();
    });
  });

  it('clicking "Warnings" in sidebar switches to warnings category', () => {
    render(<InsightsPage />);
    const warningsBtn = screen.getByText('Warnings');
    fireEvent.click(warningsBtn);
    expect(screen.getByText('Warnings & Performance Issues')).toBeInTheDocument();
  });

  it('clicking "Patterns Detected" in sidebar switches to patterns category', () => {
    render(<InsightsPage />);
    const patternsBtn = screen.getByText('Patterns Detected');
    fireEvent.click(patternsBtn);
    expect(screen.getByText('Detected Patterns & Trends')).toBeInTheDocument();
  });

  it('clicking "Performance" in sidebar switches to performance category', () => {
    render(<InsightsPage />);
    const perfBtn = screen.getByText('Performance');
    fireEvent.click(perfBtn);
    expect(screen.getByText('Performance Optimization Opportunities')).toBeInTheDocument();
  });

  it('clicking "Security" in sidebar switches to security category', () => {
    render(<InsightsPage />);
    const secBtn = screen.getByText('Security');
    fireEvent.click(secBtn);
    expect(screen.getByText('Security Analysis')).toBeInTheDocument();
  });

  it('shows "No Issues Detected" when category has no insights', () => {
    render(<InsightsPage />);
    // Click through categories until we find one with no insights
    const categories = ['warnings', 'patterns', 'performance', 'security'] as const;
    const emptyCategory = categories.find(
      cat => MOCK_INSIGHTS.filter(i => i.category === cat).length === 0
    );
    if (emptyCategory) {
      const btnMap: Record<string, string> = {
        warnings: 'Warnings',
        patterns: 'Patterns',
        performance: 'Performance',
        security: 'Security',
      };
      fireEvent.click(screen.getByText(btnMap[emptyCategory]));
      expect(screen.getByText('No Issues Detected')).toBeInTheDocument();
    }
  });

  it('shows security-specific "no issues" message for security category with no insights', () => {
    render(<InsightsPage />);
    const securityInsights = MOCK_INSIGHTS.filter(i => i.category === 'security');
    if (securityInsights.length === 0) {
      fireEvent.click(screen.getByText('Security'));
      expect(screen.getByText('No security issues detected in your queues')).toBeInTheDocument();
    }
  });

  it('insight cards display metrics when available', () => {
    render(<InsightsPage />);
    const criticalInsights = MOCK_INSIGHTS.filter(i => i.category === 'critical');
    if (criticalInsights.length > 0 && criticalInsights[0].metrics.length > 0) {
      expect(screen.getByText(criticalInsights[0].metrics[0].label)).toBeInTheDocument();
    }
  });

  it('insight cards display recommendations when available', () => {
    render(<InsightsPage />);
    const criticalInsights = MOCK_INSIGHTS.filter(i => i.category === 'critical');
    if (criticalInsights.length > 0 && criticalInsights[0].recommendations.length > 0) {
      expect(screen.getByText(criticalInsights[0].recommendations[0].text)).toBeInTheDocument();
    }
  });
});
