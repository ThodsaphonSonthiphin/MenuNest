import { createBrowserRouter, Navigate } from 'react-router-dom'
import { AppLayout } from './shared/components/AppLayout'
import { ProtectedRoute } from './shared/components/ProtectedRoute'
import { FamilyRequiredRoute } from './shared/components/FamilyRequiredRoute'

import { LoginPage } from './pages/auth'
import { JoinFamilyPage, FamilyPage } from './pages/family'
import { DashboardPage } from './pages/dashboard'
import { PomodoroPage } from './pages/pomodoro'
import { RecipesPage, RecipeDetailPage } from './pages/recipes'
import { StockPage } from './pages/stock'
import { MealPlanPage } from './pages/meal-plan'
import { ShoppingListsPage, ShoppingListDetailPage } from './pages/shopping'
import { IngredientsPage } from './pages/ingredients'
import { AiAssistantPage } from './pages/ai-assistant'
import { BudgetPage } from './pages/budget'
import {
  HealthHomePage,
  QuickLogAttackPage,
  ActiveEpisodePage,
  TakeMedicationPage,
  HistoryPage,
  EpisodeDetailPage,
  DrugMasterPage,
  DrugFormPage,
  ShareLinksPage,
  HealthSettingsPage,
  PublicReportPage,
} from './pages/health'

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  // Public doctor-report endpoint: a token-gated, anonymous route. The
  // doctor opens this URL from a QR code on a device that may have no
  // signed-in MenuNest user — so it must sit outside ProtectedRoute and
  // outside AppLayout (no nav bar for the doctor view).
  { path: '/share/:token', element: <PublicReportPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      // Budget is the default landing — '/' redirects into /budget so a
      // newly-signed-in user lands on the envelope view directly. Note
      // that /budget is family-scoped, so users without a family will
      // bounce through FamilyRequiredRoute to /join-family.
      { path: '/', element: <Navigate to="/budget" replace /> },
      { path: '/join-family', element: <JoinFamilyPage /> },
      // Health pages need auth but NOT a family — migraine tracking is a
      // personal-scope module. They still render inside AppLayout so the
      // user keeps the nav bar.
      {
        element: <AppLayout />,
        children: [
          { path: '/health', element: <HealthHomePage /> },
          { path: '/health/log', element: <QuickLogAttackPage /> },
          { path: '/health/active/:id', element: <ActiveEpisodePage /> },
          { path: '/health/take-med/:episodeId', element: <TakeMedicationPage /> },
          { path: '/health/history', element: <HistoryPage /> },
          { path: '/health/episode/:id', element: <EpisodeDetailPage /> },
          { path: '/health/drugs', element: <DrugMasterPage /> },
          { path: '/health/drugs/new', element: <DrugFormPage /> },
          { path: '/health/drugs/:id/edit', element: <DrugFormPage /> },
          { path: '/health/share', element: <ShareLinksPage /> },
          { path: '/health/settings', element: <HealthSettingsPage /> },
          { path: '/pomodoro', element: <PomodoroPage /> },
        ],
      },
      {
        element: <FamilyRequiredRoute />,
        children: [
          {
            element: <AppLayout />,
            children: [
              { path: '/dashboard', element: <DashboardPage /> },
              { path: '/recipes', element: <RecipesPage /> },
              { path: '/recipes/:id', element: <RecipeDetailPage /> },
              { path: '/stock', element: <StockPage /> },
              { path: '/meal-plan', element: <MealPlanPage /> },
              { path: '/shopping', element: <ShoppingListsPage /> },
              { path: '/shopping/:id', element: <ShoppingListDetailPage /> },
              { path: '/ingredients', element: <IngredientsPage /> },
              { path: '/family', element: <FamilyPage /> },
              { path: '/budget', element: <BudgetPage /> },
              { path: '/ai-assistant', element: <AiAssistantPage /> },
              { path: '*', element: <Navigate to="/" replace /> },
            ],
          },
        ],
      },
    ],
  },
])
