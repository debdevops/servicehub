export interface HealthGrade {
  grade: string;
  color: string;
  bgClass: string;
  textClass: string;
  borderClass: string;
}

export function getHealthGrade(totalActive: number, totalDlq: number): HealthGrade {
  const dlqRatio = totalDlq / Math.max(totalActive + totalDlq, 1);
  if (dlqRatio === 0) {
    return {
      grade: 'A',
      color: 'emerald',
      bgClass: 'bg-emerald-50',
      textClass: 'text-emerald-700',
      borderClass: 'border-emerald-200',
    };
  }

  if (dlqRatio < 0.05) {
    return {
      grade: 'B',
      color: 'green',
      bgClass: 'bg-green-50',
      textClass: 'text-green-700',
      borderClass: 'border-green-200',
    };
  }

  if (dlqRatio < 0.15) {
    return {
      grade: 'C',
      color: 'amber',
      bgClass: 'bg-amber-50',
      textClass: 'text-amber-700',
      borderClass: 'border-amber-200',
    };
  }

  if (dlqRatio < 0.4) {
    return {
      grade: 'D',
      color: 'orange',
      bgClass: 'bg-orange-50',
      textClass: 'text-orange-700',
      borderClass: 'border-orange-200',
    };
  }

  return {
    grade: 'F',
    color: 'red',
    bgClass: 'bg-red-50',
    textClass: 'text-red-700',
    borderClass: 'border-red-200',
  };
}

/**
 * A namespace counts as "spiking" either on an absolute DLQ count above `threshold`, or on a
 * bad DLQ-to-active ratio (D/F health grade) — the same rule `getHealthGrade` uses. This keeps
 * every "spike"-labeled UI element (a namespace card's banner, the fleet-wide spike count, the
 * DLQ hot-spot list) in agreement: none can call a namespace spiking while another calls it fine.
 * Callers must all use this single definition rather than re-deriving their own absolute-only or
 * ratio-only check.
 */
export function isDlqSpike(totalActive: number, totalDlq: number, threshold: number): boolean {
  if (totalDlq > threshold) return true;
  const grade = getHealthGrade(totalActive, totalDlq).grade;
  return grade === 'D' || grade === 'F';
}