import { Link } from 'react-router-dom'
import type { ShoppingListDto } from '../../../shared/api/api'

interface ShoppingListCardProps {
  list: ShoppingListDto
}

const STATUS_LABEL: Record<ShoppingListDto['status'], string> = {
  Active: 'กำลังซื้อ',
  Completed: 'เสร็จสิ้น',
  Archived: 'เก็บถาวร',
}

const STATUS_CLASS: Record<ShoppingListDto['status'], string> = {
  Active: 'status--planned',
  Completed: 'status--cooked',
  Archived: '',
}

export function ShoppingListCard({ list }: ShoppingListCardProps) {
  const percent =
    list.totalCount > 0 ? Math.round((list.boughtCount / list.totalCount) * 100) : 0

  const createdDate = new Date(list.createdAt).toLocaleDateString('th-TH', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })

  return (
    <Link
      to={`/shopping/${list.id}`}
      style={{
        display: 'block',
        background: 'white',
        border: '1px solid var(--color-border)',
        borderRadius: 10,
        padding: '14px 16px',
        color: 'var(--color-text)',
        textDecoration: 'none',
        transition: 'all 0.15s',
      }}
      className="shopping-card"
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 8 }}>
        <span style={{ fontWeight: 600, fontSize: 16 }}>{list.name}</span>
        <span className={`status ${STATUS_CLASS[list.status]}`}>
          {STATUS_LABEL[list.status]}
        </span>
      </div>

      <div style={{ marginBottom: 6 }}>
        <div className="progress-bar">
          <div
            className="progress-bar__fill"
            style={{ width: `${percent}%` }}
          />
        </div>
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: 13, color: 'var(--color-text-muted)' }}>
        <span>
          {list.boughtCount} / {list.totalCount} ซื้อแล้ว
        </span>
        <span>{createdDate}</span>
      </div>
    </Link>
  )
}
