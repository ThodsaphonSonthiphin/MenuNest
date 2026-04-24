export function AddAccountDialog({onClose}: {onClose: () => void}) {
  return (
    <div className="budget-modal-overlay" onClick={e => { if (e.target === e.currentTarget) onClose() }}>
      <div className="budget-modal">
        <h3>Add Account</h3>
        <div className="subtitle">Dialog coming in Task 24.</div>
        <div className="budget-modal-footer">
          <button className="budget-row-btn" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  )
}
