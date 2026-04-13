import { useState } from 'react'

export function JoinFamilyPage() {
  const [inviteCode, setInviteCode] = useState('')

  return (
    <section className="page page--join-family">
      <div className="card">
        <h1>ยินดีต้อนรับ</h1>
        <p>คุณยังไม่ได้เข้าร่วม family</p>

        <div className="join-family__option">
          <label htmlFor="invite-code">มี invite code แล้ว?</label>
          <input
            id="invite-code"
            type="text"
            placeholder="XXXX-XXXX"
            value={inviteCode}
            onChange={(e) => setInviteCode(e.target.value)}
          />
          <button type="button" className="btn btn--primary" disabled={!inviteCode}>
            Join
          </button>
        </div>

        <div className="divider">or</div>

        <div className="join-family__option">
          <button type="button" className="btn btn--outline">
            + Create a new family
          </button>
        </div>
      </div>
    </section>
  )
}
