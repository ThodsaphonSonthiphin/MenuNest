import { createBrowserRouter, Navigate } from 'react-router-dom'
import { AppLayout } from './shared/components/AppLayout'
import { ProtectedRoute } from './shared/components/ProtectedRoute'
import { FamilyRequiredRoute } from './shared/components/FamilyRequiredRoute'

import { LoginPage } from './pages/auth'
import { JoinFamilyPage, FamilyPage } from './pages/family'
import { DashboardPage } from './pages/dashboard'
import { RecipesPage, RecipeDetailPage } from './pages/recipes'
import { StockPage } from './pages/stock'
import { MealPlanPage } from './pages/meal-plan'
import { ShoppingListsPage, ShoppingListDetailPage } from './pages/shopping'
import { IngredientsPage } from './pages/ingredients'
import { AiAssistantPage } from './pages/ai-assistant'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      { path: '/join-family', element: <JoinFamilyPage /> },
      {
        element: <FamilyRequiredRoute />,
        children: [
          {
            element: <AppLayout />,
            children: [
              { path: '/', element: <DashboardPage /> },
              { path: '/recipes', element: <RecipesPage /> },
              { path: '/recipes/:id', element: <RecipeDetailPage /> },
              { path: '/stock', element: <StockPage /> },
              { path: '/meal-plan', element: <MealPlanPage /> },
              { path: '/shopping', element: <ShoppingListsPage /> },
              { path: '/shopping/:id', element: <ShoppingListDetailPage /> },
              { path: '/ingredients', element: <IngredientsPage /> },
              { path: '/family', element: <FamilyPage /> },
              { path: '/ai-assistant', element: <AiAssistantPage /> },
              { path: '*', element: <Navigate to="/" replace /> },
            ],
          },
        ],
      },
    ],
  },
])
