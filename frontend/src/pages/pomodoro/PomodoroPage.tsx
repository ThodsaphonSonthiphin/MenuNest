import { useState } from 'react'
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
  const { state, remainingMs, start, pause, resume, reset, updateSettings } =
    usePomodoroTimer()
  const [settingsOpen, setSettingsOpen] = useState(false)

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

      <button
        type="button"
        className="pomo-settings-toggle"
        onClick={() => setSettingsOpen((v) => !v)}
        data-testid="pomo-settings-toggle"
      >
        {settingsOpen ? 'Hide settings' : 'Show settings'}
      </button>

      {settingsOpen && (
        <div className="pomo-settings" data-testid="pomo-settings">
          <label className="pomo-field">
            <span>Focus ({state.settings.focusMin} min)</span>
            <input
              type="range"
              min={1}
              max={90}
              value={state.settings.focusMin}
              onChange={(e) => updateSettings({ focusMin: Number(e.target.value) })}
              data-testid="pomo-focus-slider"
            />
          </label>
          <label className="pomo-field">
            <span>Break ({state.settings.breakMin} min)</span>
            <input
              type="range"
              min={1}
              max={30}
              value={state.settings.breakMin}
              onChange={(e) => updateSettings({ breakMin: Number(e.target.value) })}
              data-testid="pomo-break-slider"
            />
          </label>
          <label className="pomo-field pomo-field--row">
            <input
              type="checkbox"
              checked={state.settings.soundOn}
              onChange={(e) => updateSettings({ soundOn: e.target.checked })}
              data-testid="pomo-sound-toggle"
            />
            <span>Sound on cycle end</span>
          </label>
          <label className="pomo-field pomo-field--row">
            <input
              type="checkbox"
              checked={state.settings.notifOn}
              onChange={(e) => updateSettings({ notifOn: e.target.checked })}
              data-testid="pomo-notif-toggle"
            />
            <span>Browser notification</span>
          </label>
        </div>
      )}
    </div>
  )
}
