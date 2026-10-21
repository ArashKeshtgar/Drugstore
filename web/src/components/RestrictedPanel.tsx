import { useState } from 'react'

/**
 * Shown instead of a prescription the clinic has restricted (the patient's consent directive: R restricted, V very
 * restricted). Opening it takes a reason, which is logged with the user's name.
 */
export function RestrictedPanel({ label, onOpen, onClose }: { label: string; onOpen: (reason: string) => Promise<void>; onClose: () => void }) {
  const [why, setWhy] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <aside className="card pad side wide" role="region" aria-label="Restricted prescription">
      <div className="side-head"><h2>{label === 'V' ? 'Very restricted' : 'Restricted'}</h2><button className="quiet" onClick={onClose} aria-label="Close">✕</button></div>
      <p>The patient has a consent directive at the clinic. Open this prescription only if you are caring for them or have another lawful reason. Your name and your reason are logged.</p>
      <label>Why do you need to open it?<textarea rows={3} maxLength={300} value={why} onChange={e => setWhy(e.target.value)} /></label>
      {error && <p className="error" role="alert">{error}</p>}
      <button className="primary" disabled={why.trim().length < 10}
        onClick={async () => { setError(null); try { await onOpen(why.trim()) } catch (e) { setError(e instanceof Error ? e.message : 'Not opened.') } }}>Open this prescription</button>
      <p className="muted small">At least 10 characters. The access lasts one hour.</p>
    </aside>
  )
}
