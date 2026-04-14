import { Link, useParams } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useGetShoppingListDetailQuery } from '../../shared/api/api'
import { useShoppingListDetail } from './hooks/useShoppingListDetail'
import { ShoppingItemRow } from './components/ShoppingItemRow'
import { AddItemForm } from './components/AddItemForm'

export function ShoppingListDetailPage() {
  const { id } = useParams<{ id: string }>()
  const listId = id!

  const { data, isLoading, error } = useGetShoppingListDetailQuery(listId)

  const {
    errorMessage,
    isBuying,
    isUnbuying,
    isDeletingItem,
    isCompleting,
    isRegenerating,
    isAddingItem,
    handleBuy,
    handleUnbuy,
    handleDeleteItem,
    handleComplete,
    handleRegenerate,
    handleAddItem,
  } = useShoppingListDetail(listId)

  if (isLoading) {
    return (
      <section className="page">
        <p style={{ color: 'var(--color-text-muted)' }}>กำลังโหลด…</p>
      </section>
    )
  }

  if (error || !data) {
    return (
      <section className="page">
        <Link to="/shopping" style={{ fontSize: 14 }}>← Shopping Lists</Link>
        <div className="error-banner" style={{ marginTop: 12 }}>โหลดรายการไม่สำเร็จ</div>
      </section>
    )
  }

  const unboughtItems = data.items.filter((i) => !i.isBought)
  const boughtItems = data.items.filter((i) => i.isBought)

  const hasMealPlanSource = data.items.some(
    (i) => i.sourceMealPlanEntryIds != null && i.sourceMealPlanEntryIds.length > 0,
  )

  const isActive = data.status === 'Active'

  const percent =
    data.totalCount > 0
      ? Math.round((data.boughtCount / data.totalCount) * 100)
      : 0

  const isBusy = isBuying || isUnbuying || isDeletingItem || isCompleting || isRegenerating

  return (
    <section className="page page--shopping-detail">
      <header className="page__header">
        <div>
          <Link to="/shopping" style={{ fontSize: 14 }}>← Shopping Lists</Link>
          <h1 style={{ margin: '4px 0 0' }}>{data.name}</h1>
        </div>

        {isActive && (
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {hasMealPlanSource && (
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Primary}
                onClick={handleRegenerate}
                disabled={isRegenerating || isBusy}
              >
                🔄 คำนวณใหม่
              </Button>
            )}
            <Button
              type="button"
              variant={Variant.Filled}
              color={Color.Primary}
              onClick={handleComplete}
              disabled={isCompleting || isBusy}
            >
              ✓ เสร็จสิ้น
            </Button>
          </div>
        )}
      </header>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {/* Progress */}
      <div style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 14, marginBottom: 6 }}>
          <span style={{ color: 'var(--color-text-muted)' }}>
            {data.boughtCount} / {data.totalCount} ซื้อแล้ว
          </span>
          <span style={{ color: 'var(--color-text-muted)' }}>{percent}%</span>
        </div>
        <div className="progress-bar">
          <div className="progress-bar__fill" style={{ width: `${percent}%` }} />
        </div>
      </div>

      {/* Unbought section */}
      {unboughtItems.length > 0 && (
        <div style={{ marginBottom: 24 }}>
          <h2 style={{ fontSize: 15, marginBottom: 8, color: 'var(--color-text-muted)' }}>
            ยังไม่ได้ซื้อ ({unboughtItems.length})
          </h2>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: 36 }}></th>
                  <th>วัตถุดิบ</th>
                  <th style={{ width: 120 }}>จำนวน</th>
                  <th style={{ width: 100 }}></th>
                  <th style={{ width: 60 }}></th>
                </tr>
              </thead>
              <tbody>
                {unboughtItems.map((item) => (
                  <ShoppingItemRow
                    key={item.id}
                    item={item}
                    listId={listId}
                    onBuy={handleBuy}
                    onUnbuy={handleUnbuy}
                    onDelete={handleDeleteItem}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Bought section */}
      {boughtItems.length > 0 && (
        <div style={{ marginBottom: 24 }}>
          <h2 style={{ fontSize: 15, marginBottom: 8, color: 'var(--color-text-muted)' }}>
            ซื้อแล้ว ({boughtItems.length})
          </h2>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: 36 }}></th>
                  <th>วัตถุดิบ</th>
                  <th style={{ width: 120 }}>จำนวน</th>
                  <th style={{ width: 100 }}>เวลา</th>
                  <th style={{ width: 60 }}></th>
                </tr>
              </thead>
              <tbody>
                {boughtItems.map((item) => (
                  <ShoppingItemRow
                    key={item.id}
                    item={item}
                    listId={listId}
                    onBuy={handleBuy}
                    onUnbuy={handleUnbuy}
                    onDelete={handleDeleteItem}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {unboughtItems.length === 0 && boughtItems.length === 0 && (
        <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
          ยังไม่มีรายการ — เพิ่มด้านล่างได้เลย
        </p>
      )}

      {/* Add item form — only for active lists */}
      {isActive && (
        <AddItemForm
          existingItems={data.items}
          onAdd={handleAddItem}
          isAdding={isAddingItem}
        />
      )}
    </section>
  )
}
