import { useState } from 'react'
import { DropDownList } from '@syncfusion/react-dropdowns'
import type { ChangeEvent as DDLChangeEvent } from '@syncfusion/react-dropdowns'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useUpdateUserSettingsMutation } from '../../shared/api/api'
import { homeOptions } from './homeOptions'
import './SettingsPage.css'

export function SettingsPage() {
  const { familyId, homePath } = useCurrentUser()
  const [updateSettings, { isLoading }] = useUpdateUserSettingsMutation()
  const [saved, setSaved] = useState(false)

  const options = homeOptions(!!familyId)
  const effective = homePath ?? '/budget'
  const value = options.some((o) => o.path === effective) ? effective : null

  const handleChange = async (e: DDLChangeEvent) => {
    const path = e.value as string
    setSaved(false)
    try {
      await updateSettings({ homePath: path }).unwrap()
      setSaved(true)
    } catch {
      // Save failed (network/500): leave "บันทึกแล้ว" hidden. No crash, no
      // unhandled rejection, no error affordance (per approved mock).
    }
  }

  return (
    <section className="page page--settings">
      <header className="page__header">
        <h1>การตั้งค่า</h1>
      </header>

      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 11.5 12 4l8 7.5" />
              <path d="M6 10v9h12v-9" />
              <path d="M10 19v-5h4v5" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title" id="settings-home-label">หน้าแรก (Home page)</div>
            <div className="settings-row__sub">หน้าที่จะเปิดขึ้นมาเมื่อเข้าแอป</div>
          </div>
        </div>

        <DropDownList
          className="settings-home-ddl"
          dataSource={options}
          fields={{ text: 'label', value: 'path' }}
          value={value}
          placeholder="ยังไม่ได้เลือกหน้าแรก"
          aria-labelledby="settings-home-label"
          onChange={handleChange}
        />
      </div>

      {saved && !isLoading && <p className="settings-saved">บันทึกแล้ว</p>}
    </section>
  )
}
