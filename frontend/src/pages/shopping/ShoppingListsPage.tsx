import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { DropDownList } from '@syncfusion/react-dropdowns'
import { useListShoppingListsQuery } from '../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../store'
import { openCreateDialog, setFilter } from './shoppingSlice'
import { ShoppingListCard } from './components/ShoppingListCard'
import { CreateListDialog } from './components/CreateListDialog'

type Filter = 'active' | 'completed' | 'all'

const FILTER_OPTIONS = [
  { text: 'กำลังซื้อ', value: 'active' },
  { text: 'เสร็จสิ้น', value: 'completed' },
  { text: 'ทั้งหมด', value: 'all' },
]

function filterToStatus(filter: Filter): string | undefined {
  if (filter === 'active') return 'Active'
  if (filter === 'completed') return 'Completed'
  return undefined
}

export function ShoppingListsPage() {
  const dispatch = useAppDispatch()
  const filter = useAppSelector((s) => s.shopping.filter)
  const createDialogOpen = useAppSelector((s) => s.shopping.createDialogOpen)

  const { data, isLoading, error } = useListShoppingListsQuery({
    status: filterToStatus(filter as Filter),
  })

  return (
    <section className="page page--shopping">
      <header className="page__header">
        <h1>Shopping Lists</h1>
        <Button
          variant={Variant.Filled}
          color={Color.Primary}
          onClick={() => dispatch(openCreateDialog())}
        >
          + สร้างรายการ
        </Button>
      </header>

      <div style={{ marginBottom: 16, maxWidth: 220 }}>
        <DropDownList
          dataSource={FILTER_OPTIONS}
          fields={{ text: 'text', value: 'value' }}
          value={filter}
          onChange={(e: { value: unknown }) => {
            if (e.value) dispatch(setFilter(e.value as Filter))
          }}
        />
      </div>

      {isLoading && <p style={{ color: 'var(--color-text-muted)' }}>กำลังโหลด…</p>}
      {error && !isLoading && (
        <div className="error-banner">โหลดรายการไม่สำเร็จ กรุณาลองใหม่</div>
      )}

      {!isLoading && !error && data && data.length === 0 && (
        <p style={{ textAlign: 'center', padding: 40, color: 'var(--color-text-muted)' }}>
          ยังไม่มีรายการ — สร้างรายการแรก
        </p>
      )}

      {data && data.length > 0 && (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
            gap: 12,
          }}
        >
          {data.map((list) => (
            <ShoppingListCard key={list.id} list={list} />
          ))}
        </div>
      )}

      <CreateListDialog open={createDialogOpen} />
    </section>
  )
}
