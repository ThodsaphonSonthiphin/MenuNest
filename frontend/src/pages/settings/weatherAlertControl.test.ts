import {describe, it, expect} from "vitest"
import {
  alertControlFromStored,
  storedFromAlertControl,
  clampThreshold,
  UV_MIN, UV_MAX, FEELS_MIN, FEELS_MAX,
} from "./weatherAlertControl"

describe("alertControlFromStored", () => {
  it("null -> on at default", () => expect(alertControlFromStored(null, 6)).toEqual({on: true, value: 6}))
  it("undefined -> on at default", () => expect(alertControlFromStored(undefined, 40)).toEqual({on: true, value: 40}))
  it("0 -> off, field shows default", () => expect(alertControlFromStored(0, 40)).toEqual({on: false, value: 40}))
  it("N>0 -> on at N", () => expect(alertControlFromStored(35, 40)).toEqual({on: true, value: 35}))
})

describe("storedFromAlertControl", () => {
  it("on -> the value", () => expect(storedFromAlertControl(true, 35)).toBe(35))
  it("off -> 0", () => expect(storedFromAlertControl(false, 35)).toBe(0))
})

describe("clampThreshold", () => {
  it("below min -> min", () => expect(clampThreshold(0, FEELS_MIN, FEELS_MAX)).toBe(FEELS_MIN))
  it("above max -> max", () => expect(clampThreshold(99, UV_MIN, UV_MAX)).toBe(UV_MAX))
  it("non-finite -> min", () => expect(clampThreshold(NaN, FEELS_MIN, FEELS_MAX)).toBe(FEELS_MIN))
  it("rounds a decimal up", () => expect(clampThreshold(35.6, FEELS_MIN, FEELS_MAX)).toBe(36))
  it("in-range passes through", () => expect(clampThreshold(35, FEELS_MIN, FEELS_MAX)).toBe(35))
  it("bounds are UV 1..15 / feels 1..60", () =>
    expect([UV_MIN, UV_MAX, FEELS_MIN, FEELS_MAX]).toEqual([1, 15, 1, 60]))
})