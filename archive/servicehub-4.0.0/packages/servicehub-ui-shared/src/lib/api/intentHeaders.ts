export const riskIntent = {
  sendMessage: 'messages:send',
  deadLetter: 'messages:deadletter',
  replayMessage: 'messages:replay',
  purgeMessage: 'messages:purge',
  cancelScheduled: 'messages:cancel-scheduled',
  deleteNamespace: 'namespaces:delete',
  replayAllRules: 'rules:replay-all',
  bulkReplay: 'bulk:replay',
  bulkPurge: 'bulk:purge',
  signatureReplay: 'signature:replay',
} as const;

export function withRiskIntent(intent: string): Record<string, string> {
  return {
    'X-ServiceHub-Intent': intent,
    'X-ServiceHub-Confirm': 'true',
  };
}
