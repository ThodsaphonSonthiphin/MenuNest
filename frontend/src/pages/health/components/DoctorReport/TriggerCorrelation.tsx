import type { TriggerCorrelationDto } from '../../../../shared/api/healthTypes'

/**
 * Horizontal bar list of triggers ordered by attack count desc. Color
 * each bar based on a heuristic on the trigger name (so common triggers
 * land on a consistent semantic color — stress=red, hormonal=pink,
 * sleep=indigo). Unknown triggers fall back to a neutral slate.
 *
 * Mock: docs/mocks/doctor-report-mock.html — "⚡ Trigger correlation" card.
 */
export interface TriggerCorrelationProps {
  triggers: TriggerCorrelationDto[]
  totalAttacks: number
}

interface ColorRule {
  test: (name: string) => boolean
  color: string
  icon: string
}

const COLOR_RULES: ColorRule[] = [
  { test: (n) => /stress|เครียด/i.test(n), color: '#ef4444', icon: '🧠' },
  { test: (n) => /hormon|menstr|รอบเดือน|ประจำเดือน/i.test(n), color: '#ec4899', icon: '⚭' },
  { test: (n) => /sleep|นอน/i.test(n), color: '#6366f1', icon: '😴' },
  { test: (n) => /caffe|กาแฟ/i.test(n), color: '#a16207', icon: '☕' },
  { test: (n) => /alcohol|เหล้า|เบียร์|wine|beer/i.test(n), color: '#7c3aed', icon: '🍷' },
  { test: (n) => /food|อาหาร|chocolate|cheese/i.test(n), color: '#f97316', icon: '🍫' },
  { test: (n) => /weather|อากาศ|barometric/i.test(n), color: '#0ea5e9', icon: '🌧️' },
  { test: (n) => /screen|จอ|light|แสง/i.test(n), color: '#facc15', icon: '💡' },
]

function styleFor(name: string): { color: string; icon: string } {
  for (const rule of COLOR_RULES) {
    if (rule.test(name)) return { color: rule.color, icon: rule.icon }
  }
  return { color: '#64748b', icon: '•' }
}

export function TriggerCorrelation({ triggers, totalAttacks }: TriggerCorrelationProps) {
  const sorted = [...triggers].sort((a, b) => b.attackCount - a.attackCount)

  return (
    <div className="health-report-card">
      <h2 className="health-report-h2">
        ⚡ Trigger correlation ({totalAttacks} attacks)
      </h2>
      {sorted.length === 0 ? (
        <div className="health-report-empty">ไม่มี triggers ระบุในช่วงนี้</div>
      ) : (
        sorted.map((t) => {
          const { color, icon } = styleFor(t.triggerName)
          const pct = Math.round(t.percentage)
          return (
            <div key={t.triggerId} className="health-report-trigger-bar">
              <div className="health-report-trigger-bar-label">
                <span>
                  {icon} {t.triggerName}
                </span>
                <span className="health-report-trigger-count">
                  {t.attackCount === 0
                    ? '0 (not recorded)'
                    : `${t.attackCount} attack${t.attackCount > 1 ? 's' : ''} (${pct}%)`}
                </span>
              </div>
              <div className="health-report-trigger-bar-track">
                <div
                  className="health-report-trigger-bar-fill"
                  style={{ width: `${pct}%`, background: color }}
                />
              </div>
            </div>
          )
        })
      )}
    </div>
  )
}
