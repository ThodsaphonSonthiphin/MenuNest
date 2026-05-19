import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { usePomodoroTimer } from './usePomodoroTimer'
import './PomodoroPage.css'

const formatMMSS = (ms: number): string => {
  const totalSec = Math.ceil(ms / 1000)
  const m = Math.floor(totalSec / 60)
  const s = totalSec % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

export function PomodoroPage() {
  const { state, remainingMs, start, pause, resume, reset } = usePomodoroTimer()

  return (
    <div className="pomo-page" data-testid="pomo-page">
      <div className="pomo-header">
        <span
          className={`pomo-mode-badge pomo-mode-badge--${state.mode}`}
          data-testid="pomo-mode-badge"
        >
          {state.mode === 'focus' ? 'Focus' : 'Break'}
        </span>
        <span className="pomo-daily-count" data-testid="pomo-daily-count">
          Pomodoros today: {state.dailyCount.focusCompleted}
        </span>
      </div>

      <div className="pomo-time" data-testid="pomo-time">
        {formatMMSS(remainingMs)}
      </div>

      <div className="pomo-controls">
        {state.status === 'idle' && (
          <Button
            variant={Variant.Standard}
            color={Color.Primary}
            onClick={start}
            data-testid="pomo-start"
          >
            Start
          </Button>
        )}
        {state.status === 'running' && (
          <Button
            variant={Variant.Standard}
            color={Color.Primary}
            onClick={pause}
            data-testid="pomo-pause"
          >
            Pause
          </Button>
        )}
        {state.status === 'paused' && (
          <Button
            variant={Variant.Standard}
            color={Color.Primary}
            onClick={resume}
            data-testid="pomo-start"
          >
            Resume
          </Button>
        )}
        <Button
          variant={Variant.Standard}
          color={Color.Secondary}
          onClick={reset}
          data-testid="pomo-reset"
        >
          Reset
        </Button>
      </div>
    </div>
  )
}
