import {describe, expect, it} from 'vitest'
import {classifyUndoKey} from './keyBinding'

const free = {inEditable: false, dialogOpen: false}
const ctrlZ = {key: 'z', ctrlKey: true, metaKey: false, shiftKey: false}
const cmdZ = {key: 'z', ctrlKey: false, metaKey: true, shiftKey: false}

describe('classifyUndoKey', () => {
  it('treats Ctrl+Z as undo', () => {
    expect(classifyUndoKey(ctrlZ, free)).toBe('undo')
  })

  it('treats Cmd+Z as undo', () => {
    expect(classifyUndoKey(cmdZ, free)).toBe('undo')
  })

  it('treats Cmd+Shift+Z as redo', () => {
    expect(classifyUndoKey({...cmdZ, shiftKey: true}, free)).toBe('redo')
  })

  it('accepts an uppercase Z, which is what Shift produces', () => {
    expect(classifyUndoKey({key: 'Z', ctrlKey: true, metaKey: false, shiftKey: true}, free)).toBe('redo')
  })

  it('ignores a bare z', () => {
    expect(classifyUndoKey({key: 'z', ctrlKey: false, metaKey: false, shiftKey: false}, free)).toBe('ignore')
  })

  it('ignores another modified key', () => {
    expect(classifyUndoKey({key: 's', ctrlKey: true, metaKey: false, shiftKey: false}, free)).toBe('ignore')
  })

  it('ignores the binding inside a text field, so the browser undo wins', () => {
    expect(classifyUndoKey(ctrlZ, {inEditable: true, dialogOpen: false})).toBe('ignore')
  })

  it('ignores the binding while a dialog is open', () => {
    expect(classifyUndoKey(ctrlZ, {inEditable: false, dialogOpen: true})).toBe('ignore')
  })
})
