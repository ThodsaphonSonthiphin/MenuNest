import './PomodoroPage.css'

export function PomodoroPage() {
  return (
    <div className="pomo-page" data-testid="pomo-page">
      <h1 className="pomo-title">⏱️ Pomodoro</h1>
      <div className="pomo-time" data-testid="pomo-time">
        25:00
      </div>
    </div>
  )
}
