import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, dateTime, firstError, type Ingredient, type PatientFields, type PatientProfile, type PatientRow, type Prescriber } from '../api'
import { useRef_ } from '../App'

/** Patients (with allergies and drug plan) and prescribers. */
export default function People() {
  const [tab, setTab] = useState<'patients' | 'prescribers'>('patients')
  return (
    <>
      <div className="head"><div><h1>Patients &amp; prescribers</h1><div className="muted">Allergies recorded here are checked on every prescription.</div></div></div>
      <div className="tabs">
        <button className={`tab ${tab === 'patients' ? 'on' : ''}`} onClick={() => setTab('patients')}>Patients</button>
        <button className={`tab ${tab === 'prescribers' ? 'on' : ''}`} onClick={() => setTab('prescribers')}>Prescribers</button>
      </div>
      {tab === 'patients' ? <Patients /> : <Prescribers />}
    </>
  )
}

function Patients() {
  const [q, setQ] = useState('')
  const [rows, setRows] = useState<PatientRow[]>([])
  const [open, setOpen] = useState<number | 'new' | null>(null)
  const search = useCallback(() => { if (q.trim().length >= 2) api.patients(q).then(setRows); else setRows([]) }, [q])
  useEffect(() => { const t = setTimeout(search, 200); return () => clearTimeout(t) }, [search])
  return (
    <div className={open ? 'split' : ''}>
      <section className="card">
        <div className="filters"><input type="search" value={q} onChange={e => setQ(e.target.value)} placeholder="Last name, health card, birth date or phone" style={{ maxWidth: 380 }} autoFocus />
          <button className="primary small" style={{ marginLeft: 'auto' }} onClick={() => setOpen('new')}>Register patient</button></div>
        <table className="data"><thead><tr><th>Name</th><th>Born</th><th>Sex</th><th>Phone</th><th>Allergies</th></tr></thead>
          <tbody>{rows.map(p => <tr key={p.patientId} className={`clickable ${open === p.patientId ? 'selected' : ''}`} onClick={() => setOpen(p.patientId)}>
            <td><strong>{p.name}</strong></td><td>{p.dateOfBirth}</td><td>{p.sex}</td><td>{p.phone}</td><td>{p.allergies || ''}</td></tr>)}
            {q.trim().length < 2 && <tr><td colSpan={5} className="muted">Type at least two characters.</td></tr>}</tbody></table>
      </section>
      {open && <PatientEditor key={open} id={open === 'new' ? null : open} onClose={() => setOpen(null)} onSaved={id => { setOpen(id); search() }} />}
    </div>
  )
}

const empty: PatientFields = { firstName: '', lastName: '', sex: 'U' }

function PatientEditor({ id, onClose, onSaved }: { id: number | null; onClose: () => void; onSaved: (id: number) => void }) {
  const { ref } = useRef_()
  const [profile, setProfile] = useState<PatientProfile | null>(null)
  const [form, setForm] = useState<PatientFields>(empty)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [msg, setMsg] = useState<string | null>(null)
  const load = useCallback(async () => { if (id) { const p = await api.patient(id); setProfile(p); setForm(p.patient) } }, [id])
  useEffect(() => { load() }, [load])
  const set = (k: keyof PatientFields) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm({ ...form, [k]: e.target.value || null })
  const err = (k: string) => errors[k]?.[0] && <span className="error small">{errors[k][0]}</span>
  const save = async () => {
    setErrors({}); setMsg(null)
    try { const r = await api.savePatient(id, { ...form, defaultPlanId: form.defaultPlanId ? Number(form.defaultPlanId) : null }); setMsg('Saved.'); onSaved(r.patientId); await load() }
    catch (e) { if (e instanceof ApiError && Object.keys(e.fieldErrors).length) setErrors(e.fieldErrors); else setMsg(firstError(e, 'Not saved.')) }
  }
  return (
    <aside className="card pad side wide">
      <div className="side-head"><h2>{id ? `${form.lastName}, ${form.firstName}` : 'Register a patient'}</h2><button className="quiet" onClick={onClose}>✕</button></div>
      <div className="form-row">
        <label>First name<input value={form.firstName} onChange={set('firstName')} />{err('firstName')}</label>
        <label>Last name<input value={form.lastName} onChange={set('lastName')} />{err('lastName')}</label>
        <label>Date of birth<input type="date" value={form.dateOfBirth ?? ''} onChange={set('dateOfBirth')} />{err('dateOfBirth')}</label>
        <label>Sex<select value={form.sex ?? 'U'} onChange={set('sex')}>{['F', 'M', 'X', 'U'].map(s => <option key={s}>{s}</option>)}</select></label>
      </div>
      <div className="form-row">
        <label>Ontario health card<input value={form.healthCardNumber ?? ''} onChange={set('healthCardNumber')} />{err('healthCardNumber')}</label>
        <label>Version<input value={form.healthCardVersion ?? ''} maxLength={2} onChange={set('healthCardVersion')} /></label>
        <label>Drug plan<select value={form.defaultPlanId ?? ''} onChange={set('defaultPlanId')}><option value="">None (cash)</option>{ref.plans.map(p => <option key={p.planId} value={p.planId}>{p.name}</option>)}</select></label>
        <label>Plan member id<input value={form.planMemberId ?? ''} onChange={set('planMemberId')} /></label>
      </div>
      <div className="form-row">
        <label>Phone<input value={form.phone ?? ''} onChange={set('phone')} /></label>
        <label>Email<input value={form.email ?? ''} onChange={set('email')} /></label>
        <label>City<input value={form.city ?? ''} onChange={set('city')} /></label>
        <label>Postal code<input value={form.postalCode ?? ''} onChange={set('postalCode')} />{err('postalCode')}</label>
      </div>
      {msg && <p className="notice">{msg}</p>}
      <div className="actions"><button className="primary" onClick={save}>{id ? 'Save' : 'Register'}</button></div>
      {profile && <Allergies profile={profile} onChanged={load} />}
      {profile && profile.prescriptions.length > 0 && (
        <section><h3>Prescriptions</h3>
          <ul className="plain">{profile.prescriptions.map(r => <li key={r.prescriptionId}><span className="mono">{r.rxNumber}</span> · {dateTime(r.receivedAt)} · {r.items} drugs · {r.status}</li>)}</ul></section>
      )}
    </aside>
  )
}

function Allergies({ profile, onChanged }: { profile: PatientProfile; onChanged: () => Promise<void> }) {
  const [ingredients, setIngredients] = useState<Ingredient[]>([])
  const [form, setForm] = useState({ ingredientId: '', allergyClass: '', description: '', reaction: '', severity: 'Unknown' })
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { api.ingredients().then(setIngredients) }, [])
  const classes = [...new Set(ingredients.map(i => i.allergyClass).filter(Boolean))] as string[]
  const add = async () => {
    setError(null)
    try {
      await api.addAllergy(profile.patient.patientId, { ingredientId: form.ingredientId ? Number(form.ingredientId) : null, allergyClass: form.allergyClass || null,
        description: form.description || null, reaction: form.reaction || null, severity: form.severity })
      setForm({ ingredientId: '', allergyClass: '', description: '', reaction: '', severity: 'Unknown' }); await onChanged()
    } catch (e) { setError(firstError(e, 'Not recorded.')) }
  }
  return (
    <section className="allergies"><h3>Allergies</h3>
      {profile.allergies.length === 0 && <p className="muted small">None on file. Ask the patient.</p>}
      <ul className="plain">{profile.allergies.map(a => <li key={a.allergyId}>
        <strong>{a.ingredient ?? a.allergyClass ?? a.description}</strong>{a.reaction && ` · ${a.reaction}`} · {a.severity}
        <button className="quiet" onClick={() => window.confirm('Remove this allergy?') && api.removeAllergy(a.allergyId).then(onChanged)} aria-label="Remove">✕</button></li>)}</ul>
      <div className="form-row">
        <label>Ingredient<select value={form.ingredientId} onChange={e => setForm({ ...form, ingredientId: e.target.value })}><option value="">—</option>{ingredients.map(i => <option key={i.ingredientId} value={i.ingredientId}>{i.name}</option>)}</select></label>
        <label>or family<select value={form.allergyClass} onChange={e => setForm({ ...form, allergyClass: e.target.value })}><option value="">—</option>{classes.map(c => <option key={c}>{c}</option>)}</select></label>
        <label>or as said<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
      </div>
      <div className="form-row">
        <label>Reaction<input value={form.reaction} onChange={e => setForm({ ...form, reaction: e.target.value })} placeholder="Hives, anaphylaxis…" /></label>
        <label>Severity<select value={form.severity} onChange={e => setForm({ ...form, severity: e.target.value })}>{['Unknown', 'Mild', 'Moderate', 'Severe'].map(s => <option key={s}>{s}</option>)}</select></label>
        <div className="actions" style={{ alignSelf: 'end' }}><button onClick={add}>Add allergy</button></div>
      </div>
      {error && <p className="error">{error}</p>}
    </section>
  )
}

function Prescribers() {
  const [rows, setRows] = useState<Prescriber[]>([])
  const [edit, setEdit] = useState<Partial<Prescriber> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const load = useCallback(() => api.prescribers().then(setRows), [])
  useEffect(() => { load() }, [load])
  const save = async () => {
    setError(null)
    try { await api.savePrescriber(edit!.prescriberId ?? null, { licenceBody: edit!.licenceBody ?? 'CPSO', licenceNumber: edit!.licenceNumber ?? '', firstName: edit!.firstName ?? '', lastName: edit!.lastName ?? '',
      phone: edit!.phone, fax: edit!.fax, clinic: edit!.clinic, isActive: edit!.isActive ?? true }); setEdit(null); load() }
    catch (e) { setError(firstError(e, 'Not saved.')) }
  }
  return (
    <div className={edit ? 'split' : ''}>
      <section className="card">
        <div className="filters"><button className="primary small" onClick={() => setEdit({ licenceBody: 'CPSO', isActive: true })}>Add prescriber</button></div>
        <table className="data"><thead><tr><th>Name</th><th>Licence</th><th>Clinic</th><th>Phone / fax</th></tr></thead>
          <tbody>{rows.map(p => <tr key={p.prescriberId} className="clickable" onClick={() => setEdit(p)}><td>{p.lastName}, {p.firstName}</td><td className="mono">{p.licenceBody} {p.licenceNumber}</td>
            <td>{p.clinic}</td><td className="small">{p.phone}{p.fax && ` / ${p.fax}`}</td></tr>)}</tbody></table>
      </section>
      {edit && (
        <aside className="card pad side">
          <div className="side-head"><h2>{edit.prescriberId ? `${edit.lastName}, ${edit.firstName}` : 'Add a prescriber'}</h2><button className="quiet" onClick={() => setEdit(null)}>✕</button></div>
          <div className="form-row">
            <label>College<select value={edit.licenceBody} onChange={e => setEdit({ ...edit, licenceBody: e.target.value })}>
              <option value="CPSO">CPSO (physician)</option><option value="CNO">CNO (nurse practitioner)</option><option value="RCDSO">RCDSO (dentist)</option><option value="OCP">OCP (pharmacist)</option></select></label>
            <label>Registration number<input value={edit.licenceNumber ?? ''} onChange={e => setEdit({ ...edit, licenceNumber: e.target.value })} /></label>
          </div>
          <div className="form-row">
            <label>First name<input value={edit.firstName ?? ''} onChange={e => setEdit({ ...edit, firstName: e.target.value })} /></label>
            <label>Last name<input value={edit.lastName ?? ''} onChange={e => setEdit({ ...edit, lastName: e.target.value })} /></label>
          </div>
          <label>Clinic<input value={edit.clinic ?? ''} onChange={e => setEdit({ ...edit, clinic: e.target.value })} /></label>
          <div className="form-row">
            <label>Phone<input value={edit.phone ?? ''} onChange={e => setEdit({ ...edit, phone: e.target.value })} /></label>
            <label>Fax<input value={edit.fax ?? ''} onChange={e => setEdit({ ...edit, fax: e.target.value })} /></label>
          </div>
          {error && <p className="error">{error}</p>}
          <div className="actions"><button className="primary" onClick={save}>Save</button></div>
        </aside>
      )}
    </div>
  )
}
