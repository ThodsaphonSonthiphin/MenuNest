import { Grid, Column, Columns } from '@syncfusion/react-grid'
import {
  useListIngredientsQuery,
  useCreateIngredientMutation,
  useUpdateIngredientMutation,
  useDeleteIngredientMutation,
} from '../../shared/api/api'
import { useRtkDataManager } from '../../shared/data/useRtkDataManager'

export function IngredientsPage() {
  const { data } = useListIngredientsQuery()
  const [create] = useCreateIngredientMutation()
  const [update] = useUpdateIngredientMutation()
  const [remove] = useDeleteIngredientMutation()

  const dm = useRtkDataManager(data, {
    key: 'id',
    onAdd: (row) => create({ name: row.name as string, unit: row.unit as string }).unwrap(),
    onUpdate: (row) =>
      update({ id: row.id as string, name: row.name as string, unit: row.unit as string }).unwrap(),
    onDelete: (rows) => remove(rows[0].id as string).unwrap(),
  })

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
