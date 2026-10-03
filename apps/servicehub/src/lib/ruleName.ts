/**
 * Rules made by "Auto Generate" before names were readable are called "Auto: Unknown in topic/subscriptions/sub" — "Unknown" is the
 * matcher's word for "the cloud recorded no reason", not a failure type. Shown the way new rules are named; the stored rule is untouched.
 */
export function ruleTitle(name: string): string {
  const m = /^Auto: Unknown in (.+)$/.exec(name)
  return m ? `Auto: No recorded reason in ${m[1].replace('/subscriptions/', ' › ')}` : name.replace(/^(Auto: .+? in .+?)\/subscriptions\//, '$1 › ')
}
