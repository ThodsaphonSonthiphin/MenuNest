import { useState } from 'react'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import { Dialog } from '@syncfusion/react-popups'
import { DropDownList } from '@syncfusion/react-dropdowns'
import { Controller } from 'react-hook-form'
// TODO: migrate to Pure React when available
import { QRCodeGeneratorComponent } from '@syncfusion/ej2-react-barcode-generator'
import {
  useListFamilyMembersQuery,
  useListRelationshipsQuery,
} from '../../shared/api/api'
import type { FamilyMemberDto, RelationshipDto } from '../../shared/api/api'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useFamilyPage } from './hooks/useFamilyPage'
import { useAddRelationship } from './hooks/useAddRelationship'

const RELATION_TYPE_OPTIONS = [
  { text: 'พ่อ/แม่ (Parent)', value: 'Parent' },
  { text: 'ลูก (Child)', value: 'Child' },
  { text: 'คู่สมรส (Spouse)', value: 'Spouse' },
  { text: 'พี่น้อง (Sibling)', value: 'Sibling' },
  { text: 'อื่นๆ (Other)', value: 'Other' },
]

const RELATION_LABEL_MAP: Record<string, string> = {
  Parent: 'พ่อ/แม่',
  Child: 'ลูก',
  Spouse: 'คู่สมรส',
  Sibling: 'พี่น้อง',
  Other: 'อื่นๆ',
}

const MENU_NEST_LOGO_SVG =
  'data:image/svg+xml;base64,' +
  btoa(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">' +
      '<circle cx="20" cy="20" r="19" fill="#F57C00"/>' +
      '<circle cx="20" cy="20" r="17" fill="#fff"/>' +
      '<circle cx="20" cy="20" r="15" fill="#F57C00"/>' +
      '<text x="20" y="27" text-anchor="middle" font-size="16" fill="white" font-weight="bold">M</text>' +
      '</svg>',
  )

export function FamilyPage() {
  const { familyName, familyInviteCode } = useCurrentUser()
  const { data: members } = useListFamilyMembersQuery()
  const { data: relationships } = useListRelationshipsQuery()

  const {
    errorMessage,
    isRotating,
    isLeaving,
    handleRotateCode,
    handleLeaveFamily,
    handleDeleteRelationship,
  } = useFamilyPage()

  const addRel = useAddRelationship()

  const [showLeaveConfirm, setShowLeaveConfirm] = useState(false)
  const [showRotateConfirm, setShowRotateConfirm] = useState(false)

  const inviteUrl = familyInviteCode
    ? `${window.location.origin}/join?code=${familyInviteCode}`
    : ''

  /* ---------- Column templates ---------- */

  const MemberNameTemplate = ({ data: m }: ColumnTemplateProps<FamilyMemberDto>) => {
    const initial = m.displayName.charAt(0)
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <div
          style={{
            width: 32,
            height: 32,
            borderRadius: '50%',
            background: 'linear-gradient(135deg, #F57C00, #E65100)',
            color: '#fff',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontWeight: 600,
            fontSize: 14,
            flexShrink: 0,
          }}
        >
          {initial}
        </div>
        <span style={{ fontWeight: 500 }}>{m.displayName}</span>
      </div>
    )
  }

  const MemberRelBadgeTemplate = ({ data: m }: ColumnTemplateProps<FamilyMemberDto>) => (
    <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
      {m.isCreator && (
        <span
          style={{
            display: 'inline-block',
            padding: '2px 8px',
            borderRadius: 12,
            fontSize: 11,
            fontWeight: 500,
            background: '#FFF3E0',
            color: '#E65100',
          }}
        >
          ผู้สร้าง
        </span>
      )}
      {m.relationships.map((r) => (
        <span
          key={r.relationshipId}
          style={{
            display: 'inline-block',
            padding: '2px 8px',
            borderRadius: 12,
            fontSize: 11,
            fontWeight: 500,
            background: '#E3F2FD',
            color: '#1565C0',
          }}
        >
          {r.label}
        </span>
      ))}
    </div>
  )

  const MemberJoinedTemplate = ({ data: m }: ColumnTemplateProps<FamilyMemberDto>) => (
    <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
      {new Date(m.joinedAt).toLocaleDateString('th-TH', {
        day: 'numeric',
        month: 'short',
      })}
    </span>
  )

  const RelTypeTemplate = ({ data: r }: ColumnTemplateProps<RelationshipDto>) => (
    <span
      style={{
        display: 'inline-block',
        padding: '2px 8px',
        borderRadius: 12,
        fontSize: 11,
        fontWeight: 500,
        background: '#E3F2FD',
        color: '#1565C0',
      }}
    >
      {RELATION_LABEL_MAP[r.relationType] ?? r.relationType}
    </span>
  )

  const RelDeleteTemplate = ({ data: r }: ColumnTemplateProps<RelationshipDto>) => (
    <Button
      type="button"
      size={Size.Small}
      variant={Variant.Outlined}
      color={Color.Secondary}
      onClick={() => handleDeleteRelationship(r.id)}
    >
      🗑
    </Button>
  )

  const memberOptions =
    members?.map((m) => ({ text: m.displayName, value: m.userId })) ?? []

  return (
    <section className="page page--family">
      <header className="page__header">
        <h1>{familyName ?? 'Family'}</h1>
      </header>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {/* ---- Section 1: Invite Code + QR ---- */}
      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ fontSize: 16, marginBottom: 4 }}>รหัสเชิญ</h2>
        <p
          style={{
            fontSize: 13,
            color: 'var(--color-text-muted)',
            marginBottom: 16,
          }}
        >
          ส่งรหัสหรือ QR code นี้ให้สมาชิกครอบครัว — scan แล้วเข้าร่วมได้เลย
        </p>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 24,
            flexWrap: 'wrap',
          }}
        >
          <div>
            <div
              style={{
                fontFamily: 'monospace',
                fontSize: 28,
                fontWeight: 700,
                letterSpacing: 3,
                color: 'var(--color-primary-dark)',
                marginBottom: 12,
              }}
            >
              {familyInviteCode ?? '----'}
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={() =>
                  navigator.clipboard?.writeText(familyInviteCode ?? '')
                }
              >
                📋 คัดลอก
              </Button>
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={() => setShowRotateConfirm(true)}
                disabled={isRotating}
              >
                🔄 สร้างรหัสใหม่
              </Button>
            </div>
          </div>
          {familyInviteCode && (
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 6,
              }}
            >
              <div
                style={{
                  border: '2px solid var(--color-primary)',
                  borderRadius: 12,
                  padding: 12,
                  background: 'var(--color-primary-light)',
                }}
              >
                <QRCodeGeneratorComponent
                  value={inviteUrl}
                  width="120px"
                  height="120px"
                  foreColor="#E65100"
                  backgroundColor="transparent"
                  errorCorrectionLevel={30}
                  logo={{
                    imageSource: MENU_NEST_LOGO_SVG,
                    width: 30,
                    height: 30,
                  }}
                  displayText={{ visibility: false }}
                  mode="SVG"
                  margin={{ left: 2, right: 2, top: 2, bottom: 2 }}
                />
              </div>
              <span
                style={{
                  fontSize: 11,
                  color: 'var(--color-primary-dark)',
                  fontWeight: 600,
                }}
              >
                📱 Scan เพื่อเข้าร่วม
              </span>
            </div>
          )}
        </div>
      </div>

      {/* ---- Section 2: Members Grid ---- */}
      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>
          สมาชิก ({members?.length ?? 0} คน)
        </h2>
        {members && (
          <Grid dataSource={members as FamilyMemberDto[]} height="auto">
            <Columns>
              <Column
                field="displayName"
                headerText="ชื่อ"
                template={MemberNameTemplate}
              />
              <Column field="email" headerText="อีเมล" />
              <Column
                headerText="ความสัมพันธ์"
                template={MemberRelBadgeTemplate}
              />
              <Column
                headerText="เข้าร่วม"
                width={100}
                template={MemberJoinedTemplate}
              />
            </Columns>
          </Grid>
        )}
      </div>

      {/* ---- Section 3: Relationships Grid ---- */}
      <div className="card" style={{ marginBottom: 16 }}>
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginBottom: 12,
          }}
        >
          <h2 style={{ fontSize: 16 }}>ความสัมพันธ์</h2>
          <Button
            type="button"
            variant={Variant.Outlined}
            color={Color.Primary}
            size={Size.Small}
            onClick={addRel.open}
          >
            + เพิ่ม
          </Button>
        </div>
        <p
          style={{
            fontSize: 13,
            color: 'var(--color-text-muted)',
            marginBottom: 12,
          }}
        >
          กำหนดความสัมพันธ์ระหว่างสมาชิก — badge จะแสดงในตารางด้านบนอัตโนมัติ
        </p>
        {relationships && relationships.length > 0 ? (
          <Grid dataSource={relationships as RelationshipDto[]} height="auto">
            <Columns>
              <Column field="fromUserName" headerText="จาก" />
              <Column
                field="relationType"
                headerText="ความสัมพันธ์"
                template={RelTypeTemplate}
              />
              <Column field="toUserName" headerText="ถึง" />
              <Column
                headerText=""
                width={60}
                template={RelDeleteTemplate}
              />
            </Columns>
          </Grid>
        ) : (
          <p
            style={{
              color: 'var(--color-text-muted)',
              textAlign: 'center',
              padding: 24,
            }}
          >
            ยังไม่มีความสัมพันธ์ — กด &quot;+ เพิ่ม&quot; เพื่อเริ่มต้น
          </p>
        )}
      </div>

      {/* ---- Section 4: Danger Zone ---- */}
      <div
        className="card"
        style={{
          marginBottom: 16,
          borderColor: '#FECDD3',
          background: '#FFF5F5',
        }}
      >
        <h2
          style={{ fontSize: 16, color: 'var(--color-danger)', marginBottom: 8 }}
        >
          Danger Zone
        </h2>
        <p
          style={{
            fontSize: 13,
            color: 'var(--color-text-muted)',
            marginBottom: 12,
          }}
        >
          ออกจากครอบครัวนี้ — คุณจะไม่เห็น recipe, stock, meal plan, และ shopping
          list ของครอบครัวนี้อีกต่อไป
        </p>
        <Button
          type="button"
          variant={Variant.Outlined}
          onClick={() => setShowLeaveConfirm(true)}
          disabled={isLeaving}
          style={{ borderColor: 'var(--color-danger)', color: 'var(--color-danger)' }}
        >
          ออกจากครอบครัว
        </Button>
      </div>

      {/* ---- Add Relationship Dialog ---- */}
      {addRel.isOpen && (
        <Dialog
          modal={true}
          open={addRel.isOpen}
          header="เพิ่มความสัมพันธ์"
          onClose={addRel.close}
          style={{ width: '440px' }}
        >
          <form onSubmit={addRel.onSubmit} noValidate>
            <div
              style={{
                padding: '16px 0',
                display: 'flex',
                flexDirection: 'column',
                gap: 16,
              }}
            >
              <div>
                <label
                  style={{
                    display: 'block',
                    fontSize: 13,
                    fontWeight: 500,
                    marginBottom: 4,
                  }}
                >
                  จากสมาชิก{' '}
                  <span style={{ color: 'var(--color-danger)' }}>*</span>
                </label>
                <Controller
                  control={addRel.form.control}
                  name="fromUserId"
                  rules={{ required: 'กรุณาเลือกสมาชิก' }}
                  render={({ field }) => (
                    <DropDownList
                      dataSource={memberOptions}
                      fields={{ text: 'text', value: 'value' }}
                      value={field.value}
                      onChange={(e) => field.onChange(e.value ?? '')}
                      placeholder="— เลือกสมาชิก —"
                    />
                  )}
                />
                {addRel.form.formState.errors.fromUserId && (
                  <p className="field-error">
                    {addRel.form.formState.errors.fromUserId.message}
                  </p>
                )}
              </div>

              <div>
                <label
                  style={{
                    display: 'block',
                    fontSize: 13,
                    fontWeight: 500,
                    marginBottom: 4,
                  }}
                >
                  เป็น{' '}
                  <span style={{ color: 'var(--color-danger)' }}>*</span>
                </label>
                <Controller
                  control={addRel.form.control}
                  name="relationType"
                  rules={{ required: 'กรุณาเลือกความสัมพันธ์' }}
                  render={({ field }) => (
                    <DropDownList
                      dataSource={RELATION_TYPE_OPTIONS}
                      fields={{ text: 'text', value: 'value' }}
                      value={field.value}
                      onChange={(e) => field.onChange(e.value ?? '')}
                      placeholder="— เลือกความสัมพันธ์ —"
                    />
                  )}
                />
                {addRel.form.formState.errors.relationType && (
                  <p className="field-error">
                    {addRel.form.formState.errors.relationType.message}
                  </p>
                )}
              </div>

              <div>
                <label
                  style={{
                    display: 'block',
                    fontSize: 13,
                    fontWeight: 500,
                    marginBottom: 4,
                  }}
                >
                  ของสมาชิก{' '}
                  <span style={{ color: 'var(--color-danger)' }}>*</span>
                </label>
                <Controller
                  control={addRel.form.control}
                  name="toUserId"
                  rules={{ required: 'กรุณาเลือกสมาชิก' }}
                  render={({ field }) => (
                    <DropDownList
                      dataSource={memberOptions}
                      fields={{ text: 'text', value: 'value' }}
                      value={field.value}
                      onChange={(e) => field.onChange(e.value ?? '')}
                      placeholder="— เลือกสมาชิก —"
                    />
                  )}
                />
                {addRel.form.formState.errors.toUserId && (
                  <p className="field-error">
                    {addRel.form.formState.errors.toUserId.message}
                  </p>
                )}
              </div>

              {addRel.errorMessage && (
                <div className="error-banner">{addRel.errorMessage}</div>
              )}
            </div>

            <div
              style={{
                display: 'flex',
                justifyContent: 'flex-end',
                gap: 8,
                paddingTop: 8,
                borderTop: '1px solid var(--color-border)',
              }}
            >
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={addRel.close}
              >
                ยกเลิก
              </Button>
              <Button
                type="submit"
                variant={Variant.Filled}
                color={Color.Primary}
                disabled={addRel.isLoading}
              >
                {addRel.isLoading ? 'กำลังบันทึก…' : 'บันทึก'}
              </Button>
            </div>
          </form>
        </Dialog>
      )}

      {/* ---- Rotate Confirm Dialog ---- */}
      {showRotateConfirm && (
        <Dialog
          modal={true}
          open={showRotateConfirm}
          header="สร้างรหัสเชิญใหม่"
          onClose={() => setShowRotateConfirm(false)}
          style={{ width: '380px' }}
        >
          <p style={{ margin: '16px 0' }}>
            รหัสเก่าจะใช้ไม่ได้อีกต่อไป — ต้องการสร้างรหัสใหม่?
          </p>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <Button
              type="button"
              variant={Variant.Outlined}
              color={Color.Secondary}
              onClick={() => setShowRotateConfirm(false)}
            >
              ยกเลิก
            </Button>
            <Button
              type="button"
              variant={Variant.Filled}
              color={Color.Primary}
              disabled={isRotating}
              onClick={async () => {
                await handleRotateCode()
                setShowRotateConfirm(false)
              }}
            >
              {isRotating ? 'กำลังสร้าง…' : 'สร้างรหัสใหม่'}
            </Button>
          </div>
        </Dialog>
      )}

      {/* ---- Leave Confirm Dialog ---- */}
      {showLeaveConfirm && (
        <Dialog
          modal={true}
          open={showLeaveConfirm}
          header="ออกจากครอบครัว"
          onClose={() => setShowLeaveConfirm(false)}
          style={{ width: '380px' }}
        >
          <p style={{ margin: '16px 0' }}>
            คุณจะไม่เห็น recipe, stock, meal plan, และ shopping list
            ของครอบครัวนี้อีกต่อไป — ต้องการออก?
          </p>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <Button
              type="button"
              variant={Variant.Outlined}
              color={Color.Secondary}
              onClick={() => setShowLeaveConfirm(false)}
            >
              ยกเลิก
            </Button>
            <Button
              type="button"
              variant={Variant.Filled}
              onClick={handleLeaveFamily}
              disabled={isLeaving}
              style={{
                background: 'var(--color-danger)',
                borderColor: 'var(--color-danger)',
              }}
            >
              {isLeaving ? 'กำลังออก…' : 'ออกจากครอบครัว'}
            </Button>
          </div>
        </Dialog>
      )}
    </section>
  )
}
