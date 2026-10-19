import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import { NavLink, Navigate, Route, Routes } from 'react-router'
import { api, type Me, type Ref } from './api'
import BaseDefinitions from './pages/BaseDefinitions'
import Drugs from './pages/Drugs'
import People from './pages/People'
import Reception from './pages/Reception'
import Stock from './pages/Stock'

type Ctx = { ref: Ref; reloadRef: () => Promise<void> }
const RefContext = createContext<Ctx | null>(null)
export const useRef_ = () => useContext(RefContext)!

function SignIn() {
  const error = new URLSearchParams(window.location.search).get('signInError')
  return (
    <main className="page" style={{ maxWidth: 460, margin: '4rem auto', display: 'grid', gap: '1rem' }}>
      <div className="brand"><img src="/favicon.svg" alt="" width={32} height={32} /> PharmaFlow</div>
      <p className="muted">Staff sign in through ClinicHub, which decides who may use the pharmacy.</p>
      {error && <p className="error">{error}</p>}
      <button className="primary" onClick={() => window.location.assign(`/api/auth/sso/start?returnUrl=${encodeURIComponent('/')}`)}>Sign in through ClinicHub</button>
    </main>
  )
}

export function App() {
  const [me, setMe] = useState<Me | null>(null)
  const [signIn, setSignIn] = useState<'clinichub' | 'demo'>('demo')
  const [ref, setRef] = useState<Ref | null>(null)
  const [error, setError] = useState<string | null>(null)
  const reloadRef = useCallback(() => api.ref().then(setRef), [])
  useEffect(() => {
    Promise.all([api.me(), api.authSettings()]).then(([m, s]) => {
      setMe(m)
      setSignIn(s.signIn)
      if (m.name && m.permissions.length > 0) reloadRef().catch(() => {})
    }).catch(e => setError(e.message))
  }, [reloadRef])

  if (error) return <p className="page error">Can't reach the pharmacy API: {error}</p>
  if (!me) return <p className="page muted">Loading…</p>
  if (!me.name) return <SignIn />
  if (me.permissions.length === 0) return <p className="page error">{me.name}: your role does not allow PharmaFlow. <a href="/api/auth/sso/signout">Sign out</a></p>
  if (!ref) return <p className="page muted">Loading…</p>
  return (
    <RefContext.Provider value={{ ref, reloadRef }}>
      <header className="topbar">
        <div className="brand"><img src="/favicon.svg" alt="" width={28} height={28} /> PharmaFlow</div>
        <nav>
          <NavLink to="/reception">Reception</NavLink>
          <NavLink to="/people">Patients &amp; prescribers</NavLink>
          <NavLink to="/stock">Stock</NavLink>
          <NavLink to="/drugs">Drugs</NavLink>
          <NavLink to="/definitions">Base definitions</NavLink>
        </nav>
        <span className="muted small who">
          {me.name}{me.clinic ? ` · ${me.clinic}` : ''}
          {signIn === 'clinichub' && <> · <a href="/api/auth/sso/signout">Sign out</a></>}
        </span>
      </header>
      <div className="demo-strip">Demo pharmacy: synthetic patients and a made-up drug catalog. Not for dispensing.</div>
      <main className="page">
        <Routes>
          <Route path="/" element={<Navigate replace to="/reception" />} />
          <Route path="/reception" element={<Reception />} />
          <Route path="/people" element={<People />} />
          <Route path="/stock" element={<Stock />} />
          <Route path="/drugs" element={<Drugs />} />
          <Route path="/definitions" element={<BaseDefinitions />} />
          <Route path="*" element={<Navigate to="/reception" />} />
        </Routes>
      </main>
    </RefContext.Provider>
  )
}

export function Field({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) {
  return <label>{label}{children}{error && <span className="error small">{error}</span>}</label>
}

export function useErrors() {
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const err = (k: string) => errors[k]?.[0]
  return { errors, setErrors, err }
}
