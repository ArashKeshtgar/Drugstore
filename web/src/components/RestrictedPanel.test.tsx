import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError, isRestricted } from '../api'
import { RestrictedPanel } from './RestrictedPanel'

describe('RestrictedPanel', () => {
  it('names the label and asks for a reason of at least ten characters before it opens anything', async () => {
    const onOpen = vi.fn(async () => {})
    render(<RestrictedPanel label="V" onOpen={onOpen} onClose={() => {}} />)
    expect(screen.getByRole('heading', { name: 'Very restricted' })).toBeInTheDocument()
    const open = screen.getByRole('button', { name: 'Open this prescription' })
    expect(open).toBeDisabled()
    await userEvent.type(screen.getByLabelText(/Why do you need to open it/), '  Filling it at the counter now  ')
    expect(open).toBeEnabled()
    await userEvent.click(open)
    expect(onOpen).toHaveBeenCalledWith('Filling it at the counter now')   // trimmed
  })

  it('shows a refusal and can be closed', async () => {
    const onClose = vi.fn()
    render(<RestrictedPanel label="R" onOpen={async () => { throw new Error('No permission.') }} onClose={onClose} />)
    expect(screen.getByRole('heading', { name: 'Restricted' })).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText(/Why do you need to open it/), 'Covering the counter today')
    await userEvent.click(screen.getByRole('button', { name: 'Open this prescription' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No permission.')
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})

describe('isRestricted', () => {
  it('is only the 403 that says so', () => {
    expect(isRestricted(new ApiError(403, 'x', {}, { code: 'restricted', label: 'R' }))).toBe(true)
    expect(isRestricted(new ApiError(403, 'x', {}, {}))).toBe(false)
    expect(isRestricted(new ApiError(500, 'x', {}, { code: 'restricted' }))).toBe(false)
  })
})
