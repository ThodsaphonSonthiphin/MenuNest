import { useNavigate } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import type { MealPlanEntryDto } from '../../shared/api/api'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useDashboard } from './hooks/useDashboard'
import type { MealGridRow } from './hooks/useDashboard'

export function DashboardPage() {
  const { familyName } = useCurrentUser()
  const navigate = useNavigate()
  const {
    gridRows,
    stockCheckMap,
    isLoadingMeals,
    thaiDate,
    recipeCount,
    ingredientCount,
    mealsThisWeek,
    hasMeals,
  } = useDashboard()

  const LabelTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) => (
    <span
      style={{
        fontWeight: 600,
        color: row.label === 'วันนี้' ? 'var(--color-primary-dark)' : 'var(--color-text-muted)',
      }}
    >
      {row.label}
    </span>
  )

  const MealCellTemplate = (entry: MealPlanEntryDto | null) => {
    if (!entry) {
      return <span style={{ color: 'var(--color-text-muted)', fontSize: 13 }}>—</span>
    }
    const isCooked = entry.status === 'Cooked'
    const stock = stockCheckMap[entry.id]
    return (
      <div style={{ cursor: 'pointer' }} onClick={() => navigate('/meal-plan')}>
        <span
          style={{
            fontWeight: 500,
            fontSize: 13,
            textDecoration: isCooked ? 'line-through' : undefined,
            color: isCooked ? 'var(--color-text-muted)' : undefined,
          }}
        >
          {entry.recipeName}
        </span>
        {isCooked ? (
          <span style={{ marginLeft: 6, fontSize: 11, color: 'var(--color-text-muted)' }}>
            ✓ ทำแล้ว
          </span>
        ) : stock ? (
          <span
            style={{
              marginLeft: 6,
              fontSize: 11,
              color: stock.isSufficient ? '#2E7D32' : '#E65100',
            }}
          >
            {stock.isSufficient ? '✅' : `⚠️ ขาด ${stock.missingCount}`}
          </span>
        ) : null}
      </div>
    )
  }

  const BreakfastTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) =>
    MealCellTemplate(row.breakfast)
  const LunchTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) =>
    MealCellTemplate(row.lunch)
  const DinnerTemplate = ({ data: row }: ColumnTemplateProps<MealGridRow>) =>
    MealCellTemplate(row.dinner)

  return (
    <section className="page page--dashboard">
      <header className="page__header" style={{ flexDirection: 'column', alignItems: 'flex-start', gap: 2 }}>
        <h1>สวัสดี, {familyName ?? 'ครอบครัว'}</h1>
        <p style={{ color: 'var(--color-text-muted)', fontSize: 14, margin: 0 }}>{thaiDate}</p>
      </header>

      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>มื้ออาหารวันนี้และพรุ่งนี้</h2>
        {isLoadingMeals ? (
          <p style={{ color: 'var(--color-text-muted)' }}>กำลังโหลด...</p>
        ) : hasMeals ? (
          <Grid dataSource={gridRows as MealGridRow[]} height="auto">
            <Columns>
              <Column field="label" headerText="วัน" width={100} template={LabelTemplate} />
              <Column field="breakfast" headerText="🌅 เช้า" template={BreakfastTemplate} />
              <Column field="lunch" headerText="☀️ กลางวัน" template={LunchTemplate} />
              <Column field="dinner" headerText="🌙 เย็น" template={DinnerTemplate} />
            </Columns>
          </Grid>
        ) : (
          <p style={{ textAlign: 'center', padding: 32, color: 'var(--color-text-muted)' }}>
            ยังไม่มีแผนมื้ออาหาร — กดปุ่มด้านล่างเพื่อเริ่มวางแผน
          </p>
        )}
      </div>

      <div style={{ display: 'flex', gap: 12, marginBottom: 16, flexWrap: 'wrap' }}>
        <div className="card" style={{ flex: 1, minWidth: 140, textAlign: 'center', padding: 20, background: '#FFF3E0' }}>
          <div style={{ fontSize: 28, fontWeight: 700, color: '#F57C00' }}>{recipeCount}</div>
          <div style={{ fontSize: 13, color: '#E65100', marginTop: 4 }}>📖 Recipes</div>
        </div>
        <div className="card" style={{ flex: 1, minWidth: 140, textAlign: 'center', padding: 20, background: '#E3F2FD' }}>
          <div style={{ fontSize: 28, fontWeight: 700, color: '#1565C0' }}>{ingredientCount}</div>
          <div style={{ fontSize: 13, color: '#0D47A1', marginTop: 4 }}>🥬 Ingredients</div>
        </div>
        <div className="card" style={{ flex: 1, minWidth: 140, textAlign: 'center', padding: 20, background: '#E8F5E9' }}>
          <div style={{ fontSize: 28, fontWeight: 700, color: '#2E7D32' }}>{mealsThisWeek}</div>
          <div style={{ fontSize: 13, color: '#1B5E20', marginTop: 4 }}>📅 Meals This Week</div>
        </div>
      </div>

      <div style={{ textAlign: 'center' }}>
        <Button type="button" variant={Variant.Filled} color={Color.Primary} onClick={() => navigate('/meal-plan')}>
          ดูแผนทั้งหมด →
        </Button>
      </div>
    </section>
  )
}
