import { useCallback } from 'react'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import { useAuthDataManager } from '../../shared/data/useAuthDataManager'
import { api } from '../../shared/api/api'
import { useAppDispatch } from '../../store'

export function IngredientsPage() {
  const dm = useAuthDataManager({ url: '/api/ingredients' })
  const dispatch = useAppDispatch()

  // After any CRUD operation completes, invalidate RTK Query cache so other
  // pages (recipes, stock, etc.) that depend on the ingredients list stay
  // in sync.
  const handleDataChangeComplete = useCallback(() => {
    dispatch(api.util.invalidateTags([{ type: 'Ingredients', id: 'LIST' }]))
  }, [dispatch])

  return (
    <section className="page page--ingredients">
      <header className="page__header">
        <h1>Ingredients</h1>
      </header>

      <p style={{ color: 'var(--color-text-muted)', marginBottom: 16 }}>
        Master list นี้ใช้เป็น autocomplete เวลาสร้าง recipe / เพิ่ม stock — 1 ingredient = 1 หน่วย
      </p>

      {!dm && <p>Loading...</p>}

      {dm && (
        <Grid
          dataSource={dm}
          toolbar={['Add', 'Edit', 'Delete', 'Update', 'Cancel']}
          editSettings={{
            allowAdd: true,
            allowEdit: true,
            allowDelete: true,
            mode: 'Normal',
            confirmOnDelete: true,
          }}
          onDataChangeComplete={handleDataChangeComplete}
          height="auto"
        >
          <Columns>
            <Column field="id" isPrimaryKey visible={false} />
            <Column
              field="name"
              headerText="ชื่อ"
              validationRules={{ required: true, maxLength: 120 }}
            />
            <Column
              field="unit"
              headerText="หน่วย"
              validationRules={{ required: true, maxLength: 40 }}
            />
          </Columns>
        </Grid>
      )}
    </section>
  )
}
