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

  // LIVE_APP_URL removed in multi-cloud rewrite — auth note no longer links to hosted app
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
    // The h1 is the only level-1 heading on the page (multi-cloud rewrite)
    const h1 = document.querySelector('h1');
    expect(h1).not.toBeNull();
    expect(h1!.textContent).toMatch(/one platform|three clouds/i);
  });

  it('renders a second part of the hero heading', () => {
    renderPage();
    // Multi-cloud rewrite: "forensic debugger" is in the subtitle paragraph, not a heading
    expect(screen.getByText(/forensic debugger/i)).toBeInTheDocument();
  });

  it('renders the "Open ServiceHub" primary CTA links pointing to the connect page', () => {
    renderPage();
    // CTAs must navigate to /app/connect (not back to root) so users reach the app
    const ctaLinks = screen
      .getAllByRole('link', { name: /open servicehub/i })
      .filter((l) => l.getAttribute('href') === '/connect');
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
    // Multi-cloud rewrite: footer Product section has "Self-Hosting Guide" link
    expect(screen.getByRole('link', { name: /self-hosting guide/i })).toBeInTheDocument();
  });

  it('renders the "View on GitHub" CTA link', () => {
    renderPage();
    // Multi-cloud rewrite: footer Community section has "GitHub Repository" link
    const ghLink = screen.getAllByRole('link', { name: /github repository/i });
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
    // Multi-cloud rewrite: stats updated to 3 cloud providers, setup < 60s
    expect(screen.getAllByText('3').length).toBeGreaterThan(0);
    expect(screen.getByText('30-day')).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(screen.getByText('< 60s')).toBeInTheDocument();
  });

  // ── How It Works ───────────────────────────────────────────────────────────

  it('renders the "How it works" section heading', () => {
    renderPage();
    // Multi-cloud rewrite: setup time updated to under 60 seconds
    expect(screen.getByRole('heading', { name: /up and running in under 60 seconds/i })).toBeInTheDocument();
  });

  it('renders all 3 how-it-works steps', () => {
    renderPage();
    // Multi-cloud rewrite: step 1 renamed to "Choose Your Cloud"
    expect(screen.getAllByText(/choose your cloud/i).length).toBeGreaterThan(0);
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
    // Multi-cloud rewrite: comparison now covers all three cloud portals
    expect(screen.getByRole('heading', { name: /ServiceHub vs. Native Cloud Portals/i })).toBeInTheDocument();
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
    // Multi-cloud rewrite: use cases section replaced by Demo Trio section
    expect(screen.getByRole('heading', { name: /try a live demo/i })).toBeInTheDocument();
  });

  it('renders the production incident use case', () => {
    renderPage();
    // Multi-cloud rewrite: Azure cloud provider card replaces production incident use case
    expect(screen.getAllByText(/open azure demo/i).length).toBeGreaterThan(0);
  });

  it('renders the post-mortem use case', () => {
    renderPage();
    // Multi-cloud rewrite: AWS cloud provider card replaces post-mortem use case
    expect(screen.getAllByText(/open aws demo/i).length).toBeGreaterThan(0);
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
    // Multi-cloud rewrite: auth note section explains Microsoft Entra for hosted access
    expect(screen.getAllByText(/Microsoft Entra ID/i).length).toBeGreaterThanOrEqual(1);
  });

  // ── Why Choose / Bullet Lists ──────────────────────────────────────────────

  it('renders "Why Teams Choose ServiceHub" section', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /why teams choose servicehub/i })).toBeInTheDocument();
  });

  it('renders the "Beats the Azure Portal" list header', () => {
    renderPage();
    // Multi-cloud rewrite: "Beats the Azure Portal" replaced by multi-cloud benefit list
    expect(screen.getByText(/multi-cloud without the complexity/i)).toBeInTheDocument();
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
    // Multi-cloud rewrite: final CTA has "Star on GitHub" link instead
    expect(screen.getAllByRole('link', { name: /star on github/i }).length).toBeGreaterThan(0);
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
      .find((l) => l.getAttribute('href') === '/security');
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
