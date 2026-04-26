export const riskIntent = {
  sendMessage: 'messages:send',
  deadLetter: 'messages:deadletter',
  replayMessage: 'messages:replay',
  cancelScheduled: 'messages:cancel-scheduled',
  deleteNamespace: 'namespaces:delete',
  replayAllRules: 'rules:replay-all',
} as const;

export function withRiskIntent(intent: string): Record<string, string> {
  return {
    'X-ServiceHub-Intent': intent,
    'X-ServiceHub-Confirm': 'true',
  };
}
