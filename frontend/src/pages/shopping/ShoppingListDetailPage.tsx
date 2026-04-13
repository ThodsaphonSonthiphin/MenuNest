import { useParams } from 'react-router-dom'

export function ShoppingListDetailPage() {
  const { id } = useParams<{ id: string }>()

  return (
    <section className="page page--shopping-detail">
      <h1>Shopping list</h1>
      <p>Detail view placeholder (id: {id}).</p>
    </section>
  )
}
