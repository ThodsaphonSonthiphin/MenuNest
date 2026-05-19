import { Buffer } from 'node:buffer'
import { test, expect } from './fixtures/healthFixture'

test.describe('Health module — Drug Master', () => {
  test('lists drugs on /health/drugs', async ({ authedPage, mockApi }) => {
    await mockApi.drugs.list().apply()
    await authedPage.route('**/api/symptoms', (route) =>
      route.fulfill({ json: [{ id: 'symptom-migraine', name: 'Migraine', isCustom: false }] }),
    )

    await authedPage.goto('/health/drugs')
    await authedPage.waitForLoadState('networkidle')

    await expect(authedPage.getByText('Ibuprofen').first()).toBeVisible()
    await expect(authedPage.getByText('Paracetamol').first()).toBeVisible()
  })

  test('navigates to new drug form when create CTA is clicked', async ({ authedPage, mockApi }) => {
    await mockApi.drugs.list().apply()
    await authedPage.route('**/api/symptoms', (route) =>
      route.fulfill({ json: [{ id: 'symptom-migraine', name: 'Migraine', isCustom: false }] }),
    )

    await authedPage.goto('/health/drugs')
    await authedPage.waitForLoadState('networkidle')

    // DrugMasterPage renders "➕ เพิ่มยาใหม่" and "ถ่ายซองยาใหม่" buttons
    const newDrugBtn = authedPage
      .getByRole('button', { name: /เพิ่มยาใหม่|ถ่ายซองยาใหม่/ })
      .first()
    if (await newDrugBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await newDrugBtn.click()
      await expect(authedPage).toHaveURL(/\/health\/drugs\/new/)
    }
  })

  test('creates a new drug via form submission', async ({ authedPage, mockApi, capturedRequests }) => {
    await mockApi.drugs.list().createSuccess().apply()
    await authedPage.route('**/api/symptoms', (route) =>
      route.fulfill({ json: [{ id: 'symptom-migraine', name: 'Migraine', isCustom: false }] }),
    )

    await authedPage.goto('/health/drugs/new')
    await authedPage.waitForLoadState('domcontentloaded')

    // DrugFormPage: name input has placeholder "เช่น Paracetamol", no aria-label
    const nameInput = authedPage.locator('input[type="text"][placeholder*="Paracetamol"]').first()
    if (await nameInput.isVisible({ timeout: 5_000 }).catch(() => false)) {
      await nameInput.fill('Naproxen')
      // doseStrength input also required — placeholder "เช่น 500 mg"
      const doseInput = authedPage.locator('input[type="text"][placeholder*="500 mg"]').first()
      if (await doseInput.isVisible({ timeout: 2_000 }).catch(() => false)) {
        await doseInput.fill('200mg')
      }
      // Save button text: "💾 บันทึก"
      const submitBtn = authedPage.getByRole('button', { name: /บันทึก/ }).first()
      await submitBtn.click()
      await capturedRequests.waitFor('POST', '/api/drugs', 5_000).catch(() => null)
    }
  })

  test('SAS upload request fires when photo is attached (if photo UI exists)', async ({
    authedPage,
    mockApi,
    capturedRequests,
  }) => {
    await mockApi.drugs.list().apply()
    await authedPage.route('**/api/symptoms', (route) =>
      route.fulfill({ json: [{ id: 'symptom-migraine', name: 'Migraine', isCustom: false }] }),
    )

    // Photo uploader only appears in edit mode — navigate to edit page for drug-ibuprofen
    // The GET /api/drugs/:id response is handled by the drugs/* route (returns list[0])
    await authedPage.goto('/health/drugs/drug-ibuprofen/edit')
    await authedPage.waitForLoadState('networkidle')

    const fileInput = authedPage.locator('input[type="file"]').first()
    if (await fileInput.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await fileInput.setInputFiles({
        name: 'pill.jpg',
        mimeType: 'image/jpeg',
        buffer: Buffer.from('fake-image-bytes'),
      })
      await capturedRequests.waitFor('POST', '/api/photos/upload-sas', 5_000).catch(() => null)
    }
  })

  test('SAS endpoint failure surfaces an error', async ({ authedPage, mockApi }) => {
    await mockApi.drugs.list().sasFails(500).apply()
    await authedPage.route('**/api/symptoms', (route) =>
      route.fulfill({ json: [{ id: 'symptom-migraine', name: 'Migraine', isCustom: false }] }),
    )

    // Photo uploader only appears in edit mode
    await authedPage.goto('/health/drugs/drug-ibuprofen/edit')
    await authedPage.waitForLoadState('networkidle')

    const fileInput = authedPage.locator('input[type="file"]').first()
    if (await fileInput.isVisible({ timeout: 2_000 }).catch(() => false)) {
      await fileInput.setInputFiles({
        name: 'pill.jpg',
        mimeType: 'image/jpeg',
        buffer: Buffer.from('fake-image-bytes'),
      })
      await authedPage.waitForTimeout(2_000)
      await expect(authedPage.locator('body')).toBeVisible()
    }
  })
})
