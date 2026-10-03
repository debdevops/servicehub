import type { CloudProviderType } from './api/types';

// Sticky "which cloud is the user working in" signal driving the UI theme
// (Azure sky blue, AWS light orange, GCP light green — see styles/index.css).
// Updated when the user clicks a namespace in the sidebar and whenever the URL
// carries a ?namespace= of a known provider, so the theme follows interaction
// instead of defaulting to whichever namespace happens to be listed first.

const STORAGE_KEY = 'servicehub_theme_provider';

type Listener = (provider: CloudProviderType) => void;

const listeners = new Set<Listener>();

function readInitial(): CloudProviderType {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'aws' || stored === 'gcp' || stored === 'azure' ? stored : 'azure';
}

let current: CloudProviderType = readInitial();

export function getThemeProvider(): CloudProviderType {
  return current;
}

export function setThemeProvider(provider: CloudProviderType | undefined): void {
  if (!provider || provider === current) return;
  current = provider;
  localStorage.setItem(STORAGE_KEY, provider);
  listeners.forEach(listener => listener(provider));
}

export function subscribeThemeProvider(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
