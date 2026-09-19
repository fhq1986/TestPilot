import { describe, it, expect, beforeEach } from 'vitest'
import { getAuthToken } from './request'

describe('getAuthToken', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('returns token from localStorage', () => {
    localStorage.setItem('auth_token', 'abc')
    expect(getAuthToken()).toBe('abc')
  })

  it('returns empty string when not present', () => {
    expect(getAuthToken()).toBe('')
  })
})
