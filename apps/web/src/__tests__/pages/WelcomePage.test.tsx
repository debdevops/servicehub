import { describe, it, expect } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { WelcomePage } from '@/pages/WelcomePage';

/**
 * Tests for WelcomePage component
 *
 * WelcomePage is the public landing page at `/ ` and `/welcome`.
 * It must:
 * - Render without errors (no auth, no hooks)
 * - Display all key sections (hero, features, comparison, use-cases, security, CTA, footer)
 * - Link to the live application with the correct label ("Open ServiceHub")
 * - Show the Microsoft Entra auth notice
 * - Link to GitHub
 * - Provide internal navigation to /app/connect and /app/security
 */
describe('WelcomePage', () => {
  const renderPage = () =>
    render(
      <BrowserRouter>
        <WelcomePage />
      </BrowserRouter>
    );

  const LIVE_APP_URL = 'https://app-servicehub-prod.azurewebsites.net/';
  const GITHUB_URL = 'https://github.com/debdevops/servicehub';

  // ── Smoke ──────────────────────────────────────────────────────────────────

  it('renders without throwing', () => {
    expect(() => renderPage()).not.toThrow();
  });

  // ── Header ─────────────────────────────────────────────────────────────────

  it('renders the ServiceHub brand name in the header', () => {
    renderPage();
    // At least one occurrence of "ServiceHub" text should exist
    expect(screen.getAllByText(/ServiceHub/i).length).toBeGreaterThan(0);
  });

  it('renders a GitHub link in the header', () => {
    renderPage();
    const links = screen.getAllByRole('link', { name: /github/i });
    expect(links.length).toBeGreaterThan(0);
    const ghLink = links.find((l) => l.getAttribute('href') === GITHUB_URL);
    expect(ghLink).toBeDefined();
  });

  // ── Hero ───────────────────────────────────────────────────────────────────

  it('renders the main hero heading', () => {
    renderPage();
    // The h1 is the only level-1 heading on the page
    const h1 = document.querySelector('h1');
    expect(h1).not.toBeNull();
    expect(h1!.textContent).toMatch(/azure service bus/i);
  });

  it('renders a second part of the hero heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /forensic debugger/i })).toBeInTheDocument();
  });

  it('renders the "Open ServiceHub" primary CTA links pointing to the live app', () => {
    renderPage();
    const ctaLinks = screen
      .getAllByRole('link', { name: /open servicehub/i })
      .filter((l) => l.getAttribute('href') === LIVE_APP_URL);
    expect(ctaLinks.length).toBeGreaterThan(0);
  });

  it('does NOT use "demo" as the CTA label for the live application link', () => {
    renderPage();
    // "Try Free Demo" and "Launch Free Demo" must no longer exist
    expect(screen.queryByRole('link', { name: /try free demo/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /launch free demo/i })).not.toBeInTheDocument();
  });

  it('renders a "Self-Host Locally" CTA link', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /self-host locally/i })).toBeInTheDocument();
  });

  it('renders the "View on GitHub" CTA link', () => {
    renderPage();
    const ghLink = screen.getAllByRole('link', { name: /view on github/i });
    expect(ghLink.length).toBeGreaterThan(0);
  });

  // ── Microsoft Entra Auth Notice ────────────────────────────────────────────

  it('renders the Microsoft Entra authentication notice', () => {
    renderPage();
    // Appears in both the short notice and the security section — both are correct
    expect(screen.getAllByText(/Microsoft Entra ID/i).length).toBeGreaterThanOrEqual(1);
  });

  it('states that no personal data is stored', () => {
    renderPage();
    // Both the short Entra note and the security deep-dive state this — both are valid
    expect(screen.getAllByText(/not store.*personal.*information|no personal data/i).length).toBeGreaterThanOrEqual(1);
  });

  it('mentions GDPR or data compliance', () => {
    renderPage();
    expect(screen.getByText(/GDPR|data-minimisation|data compliance/i)).toBeInTheDocument();
  });

  // ── Stats Bar ──────────────────────────────────────────────────────────────

  it('renders the stats bar with key metrics', () => {
    renderPage();
    expect(screen.getByText('10+')).toBeInTheDocument();
    expect(screen.getByText('30-day')).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(screen.getByText('30s')).toBeInTheDocument();
  });

  // ── How It Works ───────────────────────────────────────────────────────────

  it('renders the "How it works" section heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /up and running in 30 seconds/i })).toBeInTheDocument();
  });

  it('renders all 3 how-it-works steps', () => {
    renderPage();
    expect(screen.getByText(/connect your namespace/i)).toBeInTheDocument();
    expect(screen.getByText(/browse & analyse/i)).toBeInTheDocument();
    expect(screen.getByText(/replay & recover/i)).toBeInTheDocument();
  });

  it('shows the one-liner git clone command', () => {
    renderPage();
    expect(screen.getByText(/git clone/i)).toBeInTheDocument();
  });

  // ── Features Grid ──────────────────────────────────────────────────────────

  it('renders the features section heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /every feature you need/i })).toBeInTheDocument();
  });

  it('renders the Forensic Message Browser feature card', () => {
    renderPage();
    expect(screen.getByText(/forensic message browser/i)).toBeInTheDocument();
  });

  it('renders the Auto-Replay Rules Engine feature card', () => {
    renderPage();
    // Appears in the feature card h3 AND the comparison table — both are correct
    expect(screen.getAllByText(/auto-replay rules engine/i).length).toBeGreaterThanOrEqual(1);
  });

  it('renders the AI analysis feature card', () => {
    renderPage();
    expect(screen.getByText(/client-side ai analysis/i)).toBeInTheDocument();
  });

  it('renders the Correlation Explorer feature card', () => {
    renderPage();
    // Text appears in both the feature card heading and the chip list
    expect(screen.getAllByText(/correlation explorer/i).length).toBeGreaterThanOrEqual(1);
  });

  it('renders the DLQ Intelligence feature card', () => {
    renderPage();
    // Text appears in both the feature card heading and the chip list
    expect(screen.getAllByText(/dlq intelligence/i).length).toBeGreaterThanOrEqual(1);
  });

  it('renders the feature chip list', () => {
    renderPage();
    expect(screen.getByText(/🔍 DLQ Intelligence/i)).toBeInTheDocument();
    expect(screen.getByText(/⚡ Auto-Replay Rules/i)).toBeInTheDocument();
    expect(screen.getByText(/🔗 Correlation Explorer/i)).toBeInTheDocument();
  });

  // ── Comparison Table ───────────────────────────────────────────────────────

  it('renders the comparison section heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /ServiceHub vs Azure Portal/i })).toBeInTheDocument();
  });

  it('renders the comparison table with Azure Portal and ServiceHub columns', () => {
    renderPage();
    const table = screen.getByRole('table');
    expect(within(table).getByText(/Azure Portal/i)).toBeInTheDocument();
    expect(within(table).getByText(/ServiceHub/i)).toBeInTheDocument();
  });

  it('shows "Replay Messages from DLQ" comparison row', () => {
    renderPage();
    expect(screen.getByText(/replay messages from dlq/i)).toBeInTheDocument();
  });

  // ── Use Cases ──────────────────────────────────────────────────────────────

  it('renders the use cases section heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /real-world scenarios/i })).toBeInTheDocument();
  });

  it('renders the production incident use case', () => {
    renderPage();
    // Use the heading-specific text to avoid matching paragraph mentions
    expect(screen.getByRole('heading', { name: /production incident/i })).toBeInTheDocument();
  });

  it('renders the post-mortem use case', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /post-mortem root-cause/i })).toBeInTheDocument();
  });

  // ── Security Section ───────────────────────────────────────────────────────

  it('renders the security section heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /enterprise-grade security/i })).toBeInTheDocument();
  });

  it('mentions AES-GCM encryption', () => {
    renderPage();
    expect(screen.getAllByText(/AES-GCM/i).length).toBeGreaterThan(0);
  });

  it('mentions read-only by default', () => {
    renderPage();
    expect(screen.getAllByText(/read-only/i).length).toBeGreaterThan(0);
  });

  it('mentions zero data exfiltration / no external calls', () => {
    renderPage();
    // Appears in both the feature heading and the description paragraph — use getAllByText
    expect(screen.getAllByText(/no.*external.*calls|zero.*data.*exfiltration|never leaves your environment/i).length).toBeGreaterThanOrEqual(1);
  });

  it('renders the hosted auth explanation with Entra link to the live app', () => {
    renderPage();
    // The security section's Entra note links to the live app URL
    const allLinks = screen.getAllByRole('link');
    const entraAppLink = allLinks.find(
      (l) => l.getAttribute('href') === LIVE_APP_URL && l.textContent?.includes('app-servicehub')
    );
    expect(entraAppLink).toBeDefined();
  });

  // ── Why Choose / Bullet Lists ──────────────────────────────────────────────

  it('renders "Why Teams Choose ServiceHub" section', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /why teams choose servicehub/i })).toBeInTheDocument();
  });

  it('renders the "Beats the Azure Portal" list header', () => {
    renderPage();
    expect(screen.getByText(/beats the azure portal/i)).toBeInTheDocument();
  });

  it('renders the "Built for DevOps & SREs" list header', () => {
    renderPage();
    // Use heading role to match only the section heading, not the subtitle paragraph
    expect(screen.getByRole('heading', { name: /built for devops/i })).toBeInTheDocument();
  });

  // ── Final CTA ──────────────────────────────────────────────────────────────

  it('renders the final CTA heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /stop guessing. start debugging./i })).toBeInTheDocument();
  });

  it('renders the "Self-Host on localhost" link in the final CTA', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /self-host on localhost/i })).toBeInTheDocument();
  });

  // ── Footer ─────────────────────────────────────────────────────────────────

  it('renders the footer with Product, Community, and Legal sections', () => {
    renderPage();
    expect(screen.getByText(/^Product$/i)).toBeInTheDocument();
    expect(screen.getByText(/^Community$/i)).toBeInTheDocument();
    expect(screen.getByText(/^Legal$/i)).toBeInTheDocument();
  });

  it('renders Documentation link in footer', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /documentation/i })).toBeInTheDocument();
  });

  it('renders the MIT License link in footer', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /mit license/i })).toBeInTheDocument();
  });

  it('renders the copyright notice', () => {
    renderPage();
    expect(screen.getByText(/© 2026 ServiceHub/i)).toBeInTheDocument();
  });

  it('renders the Security & Privacy footer link pointing to internal route', () => {
    renderPage();
    const secLink = screen
      .getAllByRole('link', { name: /security & privacy/i })
      .find((l) => l.getAttribute('href') === '/app/security');
    expect(secLink).toBeDefined();
  });

  // ── Accessibility ──────────────────────────────────────────────────────────

  it('renders at least one <main> or landmark element', () => {
    const { container } = renderPage();
    // The page has <section>, <header>, <footer> — check for meaningful structure
    expect(container.querySelectorAll('section').length).toBeGreaterThan(3);
    expect(container.querySelector('header')).toBeInTheDocument();
    expect(container.querySelector('footer')).toBeInTheDocument();
  });

  it('all external links have rel="noopener noreferrer"', () => {
    const { container } = renderPage();
    const externalLinks = Array.from(container.querySelectorAll('a[target="_blank"]'));
    externalLinks.forEach((link) => {
      expect(link.getAttribute('rel')).toContain('noopener');
      expect(link.getAttribute('rel')).toContain('noreferrer');
    });
  });

  it('primary CTA buttons have descriptive aria-labels', () => {
    renderPage();
    const ctaWithLabel = screen
      .getAllByRole('link')
      .filter((l) => l.getAttribute('aria-label')?.toLowerCase().includes('servicehub'));
    expect(ctaWithLabel.length).toBeGreaterThan(0);
  });
});
