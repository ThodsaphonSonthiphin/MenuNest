import { useState } from 'react'
import { DropDownList } from '@syncfusion/react-dropdowns'
import type { ChangeEvent as DDLChangeEvent } from '@syncfusion/react-dropdowns'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useUpdateUserSettingsMutation } from '../../shared/api/api'
import { homeOptions } from './homeOptions'
import { UV_ALERT_OPTIONS, FEELS_ALERT_OPTIONS, selectedAlertValue } from './weatherAlertOptions'
import { UV_WARN_DEFAULT, FEELS_WARN_DEFAULT } from '../trips/lib/weather'
import './SettingsPage.css'

export function SettingsPage() {
  const { familyId, homePath, uvWarnThreshold, feelsLikeWarnThreshold, isLoadingProfile } = useCurrentUser()
  const [updateSettings, { isLoading }] = useUpdateUserSettingsMutation()
  const [saved, setSaved] = useState(false)

  const options = homeOptions(!!familyId)
  const effective = homePath ?? '/budget'
  const value = options.some((o) => o.path === effective) ? effective : null

  const uvValue = selectedAlertValue(uvWarnThreshold, UV_WARN_DEFAULT)
  const feelsValue = selectedAlertValue(feelsLikeWarnThreshold, FEELS_WARN_DEFAULT)

  const handleChange = async (e: DDLChangeEvent) => {
    if (isLoadingProfile) {
      // Profile hasn't loaded yet: uvWarnThreshold/feelsLikeWarnThreshold are
      // still stale-default nulls. Saving now would persist a full-snapshot
      // PUT that resets the user's real thresholds. The control is also
      // disabled while loading, so this is defense-in-depth only.
      return
    }
    const path = e.value as string
    setSaved(false)
    try {
      await updateSettings({ homePath: path, uvWarnThreshold, feelsLikeWarnThreshold }).unwrap()
      setSaved(true)
    } catch {
      // Save failed (network/500): leave "บันทึกแล้ว" hidden. No crash, no
      // unhandled rejection, no error affordance (per approved mock).
    }
  }

  const handleUvChange = async (e: DDLChangeEvent) => {
    if (isLoadingProfile) {
      // Same data-loss guard as handleChange: don't persist a snapshot built
      // from stale-default nulls while the profile is still loading.
      return
    }
    const next = Number(e.value)
    setSaved(false)
    try {
      await updateSettings({ homePath, uvWarnThreshold: next, feelsLikeWarnThreshold }).unwrap()
      setSaved(true)
    } catch {
      // Save failed (network/500): leave "บันทึกแล้ว" hidden. No crash, no
      // unhandled rejection, no error affordance (per approved mock).
    }
  }

  const handleFeelsChange = async (e: DDLChangeEvent) => {
    if (isLoadingProfile) {
      return
    }
    const next = Number(e.value)
    setSaved(false)
    try {
      await updateSettings({ homePath, uvWarnThreshold, feelsLikeWarnThreshold: next }).unwrap()
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
          disabled={isLoadingProfile}
          onChange={handleChange}
        />
      </div>

      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="4.2" />
              <path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M19.1 4.9l-1.4 1.4M6.3 17.7l-1.4 1.4" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title" id="settings-weather-label">เตือนอากาศ</div>
            <div className="settings-row__sub">เตือนบนการ์ดเมื่อจุดหมายแดด/ร้อนเกินที่ตั้งไว้</div>
          </div>
        </div>

        <div className="settings-weather-controls">
          <div className="settings-weather-field">
            <label className="settings-weather-field__label" id="settings-uv-label">ดัชนี UV</label>
            <DropDownList
              className="settings-weather-ddl"
              dataSource={UV_ALERT_OPTIONS}
              fields={{ text: 'label', value: 'value' }}
              value={uvValue}
              aria-labelledby="settings-uv-label"
              disabled={isLoadingProfile}
              onChange={handleUvChange}
            />
          </div>
          <div className="settings-weather-field">
            <label className="settings-weather-field__label" id="settings-feels-label">รู้สึกร้อน (feels-like)</label>
            <DropDownList
              className="settings-weather-ddl"
              dataSource={FEELS_ALERT_OPTIONS}
              fields={{ text: 'label', value: 'value' }}
              value={feelsValue}
              aria-labelledby="settings-feels-label"
              disabled={isLoadingProfile}
              onChange={handleFeelsChange}
            />
          </div>
        </div>
      </div>

      {saved && !isLoading && <p className="settings-saved">บันทึกแล้ว</p>}
    </section>
  )
}
