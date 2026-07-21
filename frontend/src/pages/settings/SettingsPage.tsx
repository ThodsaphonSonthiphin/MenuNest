import { useEffect, useRef, useState } from 'react'
import { DropDownList } from '@syncfusion/react-dropdowns'
import type { ChangeEvent as DDLChangeEvent } from '@syncfusion/react-dropdowns'
import { NumericTextBox } from '@syncfusion/react-inputs'
import { Checkbox } from '@syncfusion/react-buttons'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useUpdateUserSettingsMutation, useGetVersionQuery } from '../../shared/api/api'
import { homeOptions } from './homeOptions'
import {
  alertControlFromStored, storedFromAlertControl, clampThreshold,
  UV_MIN, UV_MAX, FEELS_MIN, FEELS_MAX,
} from './weatherAlertControl'
import { UV_WARN_DEFAULT, FEELS_WARN_DEFAULT } from '../trips/lib/weather'
import { inSync } from '../../shared/version/versionCompare'
import { APP_VERSION, APP_COMMIT, BUILD_TIME } from '../../shared/version/buildInfo'
import './SettingsPage.css'

export function SettingsPage() {
  const { familyId, homePath, uvWarnThreshold, feelsLikeWarnThreshold, isLoadingProfile } = useCurrentUser()
  const [updateSettings, { isLoading }] = useUpdateUserSettingsMutation()
  const [saved, setSaved] = useState(false)
  const { data: apiVersion, isLoading: apiLoading, isError: apiError } = useGetVersionQuery()
  const buildDate = new Date(BUILD_TIME).toLocaleDateString('th-TH', { day: 'numeric', month: 'short', year: 'numeric' })

  const options = homeOptions(!!familyId)
  const effective = homePath ?? '/budget'
  const value = options.some((o) => o.path === effective) ? effective : null

  // Local control state — bound to the inputs so toggling OFF (persists 0) still keeps the
  // typed number visible in-session and re-sends it when toggled back ON. Synced from the
  // profile once it has loaded.
  const [uvOn, setUvOn] = useState(true)
  const [uvVal, setUvVal] = useState(UV_WARN_DEFAULT)
  const [feelsOn, setFeelsOn] = useState(true)
  const [feelsVal, setFeelsVal] = useState(FEELS_WARN_DEFAULT)

  // Hydrate the controls from the stored profile exactly ONCE, on first load --
  // not on later stored-value changes. Saving optimistically patches the getMe
  // cache (api.ts updateUserSettings onQueryStarted), so re-running the sync on
  // those changes would reset a field to its default the moment the user toggles
  // an axis off (which persists 0), wiping their typed value. After the initial
  // hydrate, local state is the source of truth.
  const hasHydrated = useRef(false)

  useEffect(() => {
    if (isLoadingProfile || hasHydrated.current) return
    hasHydrated.current = true
    const uv = alertControlFromStored(uvWarnThreshold, UV_WARN_DEFAULT)
    const feels = alertControlFromStored(feelsLikeWarnThreshold, FEELS_WARN_DEFAULT)
    setUvOn(uv.on); setUvVal(uv.value)
    setFeelsOn(feels.on); setFeelsVal(feels.value)
  }, [isLoadingProfile, uvWarnThreshold, feelsLikeWarnThreshold])

  // Full-snapshot PUT (ADR-091 full-replace). Guard against saving while the profile is still
  // loading — the thresholds would be stale-default nulls and clobber the user's real values.
  const persist = async (next: { homePath: string | null; uvStored: number; feelsStored: number }) => {
    if (isLoadingProfile) return
    setSaved(false)
    try {
      await updateSettings({
        homePath: next.homePath,
        uvWarnThreshold: next.uvStored,
        feelsLikeWarnThreshold: next.feelsStored,
      }).unwrap()
      setSaved(true)
    } catch {
      // Save failed (network/500): leave "บันทึกแล้ว" hidden. No crash, no error affordance.
    }
  }

  const handleHomeChange = (e: DDLChangeEvent) => {
    void persist({
      homePath: e.value as string,
      uvStored: storedFromAlertControl(uvOn, uvVal),
      feelsStored: storedFromAlertControl(feelsOn, feelsVal),
    })
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
          onChange={handleHomeChange}
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
            <div className="settings-weather-field__row">
              <Checkbox
                checked={uvOn}
                disabled={isLoadingProfile}
                aria-labelledby="settings-uv-label"
                onChange={(e) => {
                  const on = e.value
                  setUvOn(on)
                  void persist({ homePath, uvStored: storedFromAlertControl(on, uvVal), feelsStored: storedFromAlertControl(feelsOn, feelsVal) })
                }}
              />
              <NumericTextBox
                className="settings-weather-num"
                value={uvVal}
                min={UV_MIN}
                max={UV_MAX}
                step={1}
                disabled={isLoadingProfile || !uvOn}
                aria-labelledby="settings-uv-label"
                onChange={(e) => {
                  const v = clampThreshold((e.value as number | null) ?? UV_MIN, UV_MIN, UV_MAX)
                  setUvVal(v)
                  void persist({ homePath, uvStored: storedFromAlertControl(uvOn, v), feelsStored: storedFromAlertControl(feelsOn, feelsVal) })
                }}
              />
            </div>
            <p className="settings-weather-field__hint">≥6 = แดดแรง</p>
          </div>

          <div className="settings-weather-field">
            <label className="settings-weather-field__label" id="settings-feels-label">รู้สึกร้อน (°C)</label>
            <div className="settings-weather-field__row">
              <Checkbox
                checked={feelsOn}
                disabled={isLoadingProfile}
                aria-labelledby="settings-feels-label"
                onChange={(e) => {
                  const on = e.value
                  setFeelsOn(on)
                  void persist({ homePath, uvStored: storedFromAlertControl(uvOn, uvVal), feelsStored: storedFromAlertControl(on, feelsVal) })
                }}
              />
              <NumericTextBox
                className="settings-weather-num"
                value={feelsVal}
                min={FEELS_MIN}
                max={FEELS_MAX}
                step={1}
                disabled={isLoadingProfile || !feelsOn}
                aria-labelledby="settings-feels-label"
                onChange={(e) => {
                  const v = clampThreshold((e.value as number | null) ?? FEELS_MIN, FEELS_MIN, FEELS_MAX)
                  setFeelsVal(v)
                  void persist({ homePath, uvStored: storedFromAlertControl(uvOn, uvVal), feelsStored: storedFromAlertControl(feelsOn, v) })
                }}
              />
            </div>
            <p className="settings-weather-field__hint">แนะนำ ~35–40°</p>
          </div>
        </div>
      </div>

      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 2l8 4v6c0 5-3.5 8.2-8 10-4.5-1.8-8-5-8-10V6z" />
              <path d="M9.2 12l1.9 1.9 3.7-3.8" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title">เวอร์ชัน</div>
            <div className="settings-row__sub">รุ่นที่กำลังใช้งานของแอปและ API</div>
          </div>
        </div>

        <div className="settings-version">
          <div className="settings-version__line">
            <span className="settings-version__k">แอป</span>
            <code className="settings-version__v">{APP_VERSION}</code>
          </div>
          <div className="settings-version__line">
            <span className="settings-version__k">API</span>
            {apiLoading && <span className="settings-version__skel" aria-label="กำลังโหลด" />}
            {!apiLoading && apiError && <span className="settings-version__unavail">ไม่พร้อมใช้งาน</span>}
            {!apiLoading && !apiError && apiVersion && (
              <>
                <code className="settings-version__v">{apiVersion.version}</code>
                {inSync(APP_COMMIT, apiVersion.commit)
                  ? <span className="settings-version__badge settings-version__badge--ok">ตรงกัน</span>
                  : <span className="settings-version__badge settings-version__badge--warn">ไม่ตรงกัน</span>}
              </>
            )}
          </div>
          <div className="settings-version__meta">อัปเดตแอปเมื่อ {buildDate}</div>
        </div>
      </div>

      {saved && !isLoading && <p className="settings-saved">บันทึกแล้ว</p>}
    </section>
  )
}