import { useCallback, useEffect, useState } from 'react'
import { api, firstError, money, type Ingredient, type InteractionRow, type Plan, type Supplier } from '../api'
import { useRef_ } from '../App'

type Tab = 'forms' | 'routes' | 'units' | 'schedules' | 'sig' | 'ingredients' | 'interactions' | 'manufacturers' | 'suppliers' | 'plans'
const TABS: [Tab, string][] = [['forms', 'Dosage forms'], ['routes', 'Routes'], ['units', 'Units'], ['schedules', 'Schedules'], ['sig', 'SIG codes'],
  ['ingredients', 'Ingredients'], ['interactions', 'Interactions'], ['manufacturers', 'Manufacturers'], ['suppliers', 'Suppliers'], ['plans', 'Payer plans']]

/** Base definitions: the lists every other form chooses from. */
export default function BaseDefinitions() {
  const [tab, setTab] = useState<Tab>('forms')
  return (
    <>
      <div className="head"><div><h1>Base definitions</h1><div className="muted">The lists the catalog and reception choose from. Schedules are NAPRA's and the CDSA's and can't be changed here.</div></div></div>
      <div className="tabs">{TABS.map(([k, l]) => <button key={k} className={`tab ${tab === k ? 'on' : ''}`} onClick={() => setTab(k)}>{l}</button>)}</div>
      {tab === 'forms' && <Codes table="forms" />}
      {tab === 'routes' && <Codes table="routes" />}
      {tab === 'units' && <Units />}
      {tab === 'schedules' && <Schedules />}
      {tab === 'sig' && <Sig />}
      {tab === 'ingredients' && <Ingredients />}
      {tab === 'interactions' && <Interactions />}
      {tab === 'manufacturers' && <Manufacturers />}
      {tab === 'suppliers' && <Suppliers />}
      {tab === 'plans' && <Plans />}
    </>
  )
}

function useSaver() {
  const { reloadRef } = useRef_()
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const run = async (f: () => Promise<unknown>) => {
    setMsg(null)
    try { await f(); await reloadRef(); setMsg({ ok: true, text: 'Saved.' }); return true } catch (e) { setMsg({ ok: false, text: firstError(e, 'Not saved.') }); return false }
  }
  return { run, view: msg && <p className={msg.ok ? 'notice ok' : 'error'}>{msg.text}</p> }
}

function Codes({ table }: { table: 'forms' | 'routes' }) {
  const { ref } = useRef_()
  const rows = table === 'forms' ? ref.forms : ref.routes
  const [f, setF] = useState({ code: '', name: '', nameFr: '', isActive: true })
  const { run, view } = useSaver()
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Code</th><th>English</th><th>French</th></tr></thead>
        <tbody>{rows.map(r => <tr key={r.code} className={`clickable ${!r.isActive ? 'inactive' : ''}`} onClick={() => setF({ code: r.code, name: r.name, nameFr: r.nameFr ?? '', isActive: r.isActive })}>
          <td className="mono">{r.code}</td><td>{r.name}</td><td>{r.nameFr}</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>{table === 'forms' ? 'Dosage form' : 'Route'}</h2>
        <label>Code<input value={f.code} onChange={e => setF({ ...f, code: e.target.value.toUpperCase() })} /></label>
        <label>English<input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></label>
        <label>French<input value={f.nameFr} onChange={e => setF({ ...f, nameFr: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={f.isActive} onChange={e => setF({ ...f, isActive: e.target.checked })} /> Active</label>
        {view}
        <div className="actions"><button onClick={() => setF({ code: '', name: '', nameFr: '', isActive: true })}>New</button>
          <button className="primary" onClick={() => run(() => api.saveCode(table, f.code, { name: f.name, nameFr: f.nameFr || null, isActive: f.isActive }))}>Save</button></div>
      </aside>
    </div>
  )
}

function Units() {
  const { ref } = useRef_()
  const [f, setF] = useState({ code: '', name: '', kind: 'Strength', isActive: true })
  const { run, view } = useSaver()
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Code</th><th>Name</th><th>Used for</th></tr></thead>
        <tbody>{ref.units.map(u => <tr key={u.code} className={`clickable ${!u.isActive ? 'inactive' : ''}`} onClick={() => setF({ ...u })}><td className="mono">{u.code}</td><td>{u.name}</td><td>{u.kind}</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>Unit</h2>
        <label>Code<input value={f.code} onChange={e => setF({ ...f, code: e.target.value.toUpperCase() })} /></label>
        <label>Name<input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></label>
        <label>Used for<select value={f.kind} onChange={e => setF({ ...f, kind: e.target.value })}><option>Strength</option><option>Dispensing</option></select></label>
        {view}
        <div className="actions"><button className="primary" onClick={() => run(() => api.saveUnit(f.code, f))}>Save</button></div>
      </aside>
    </div>
  )
}

function Schedules() {
  const { ref } = useRef_()
  return (
    <section className="card"><table className="data"><thead><tr><th>Code</th><th>Schedule</th><th>Prescription</th><th>Narcotics Monitoring System</th><th>What it means</th></tr></thead>
      <tbody>{ref.schedules.map(s => <tr key={s.code}><td className="mono">{s.code}</td><td>{s.name}</td><td>{s.requiresPrescription ? 'Required' : 'No'}</td><td>{s.monitored ? 'Recorded' : ''}</td><td className="small">{s.description}</td></tr>)}</tbody></table></section>
  )
}

function Sig() {
  const { ref } = useRef_()
  const [f, setF] = useState({ code: '', expansion: '', expansionFr: '', dosesPerDay: '' as string, isActive: true })
  const { run, view } = useSaver()
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Code</th><th>On the label</th><th>French</th><th className="num">Doses a day</th></tr></thead>
        <tbody>{ref.sigCodes.map(s => <tr key={s.code} className={`clickable ${!s.isActive ? 'inactive' : ''}`}
          onClick={() => setF({ code: s.code, expansion: s.expansion, expansionFr: s.expansionFr ?? '', dosesPerDay: s.dosesPerDay?.toString() ?? '', isActive: s.isActive })}>
          <td className="mono">{s.code}</td><td>{s.expansion}</td><td>{s.expansionFr}</td><td className="num">{s.dosesPerDay ?? ''}</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>SIG code</h2>
        <p className="muted small">What prescribers write and what the label says. "Doses a day" lets reception work out the days' supply.</p>
        <label>Code<input value={f.code} onChange={e => setF({ ...f, code: e.target.value.toUpperCase() })} /></label>
        <label>On the label<input value={f.expansion} onChange={e => setF({ ...f, expansion: e.target.value })} /></label>
        <label>French<input value={f.expansionFr} onChange={e => setF({ ...f, expansionFr: e.target.value })} /></label>
        <label>Doses a day<input inputMode="decimal" value={f.dosesPerDay} onChange={e => setF({ ...f, dosesPerDay: e.target.value })} /></label>
        {view}
        <div className="actions"><button className="primary" onClick={() => run(() => api.saveSig(f.code, { expansion: f.expansion, expansionFr: f.expansionFr || null, dosesPerDay: f.dosesPerDay ? Number(f.dosesPerDay) : null, isActive: f.isActive }))}>Save</button></div>
      </aside>
    </div>
  )
}

function Ingredients() {
  const [rows, setRows] = useState<Ingredient[]>([])
  const [f, setF] = useState<Partial<Ingredient>>({ isActive: true })
  const { run, view } = useSaver()
  const load = useCallback(() => api.ingredients().then(setRows), [])
  useEffect(() => { load() }, [load])
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Ingredient</th><th>ATC</th><th>Allergy family</th></tr></thead>
        <tbody>{rows.map(i => <tr key={i.ingredientId} className="clickable" onClick={() => setF(i)}><td>{i.name}</td><td className="mono">{i.atcCode}</td><td>{i.allergyClass}</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>{f.ingredientId ? f.name : 'New ingredient'}</h2>
        <label>Generic name<input value={f.name ?? ''} onChange={e => setF({ ...f, name: e.target.value })} /></label>
        <label>ATC code<input value={f.atcCode ?? ''} onChange={e => setF({ ...f, atcCode: e.target.value.toUpperCase() })} placeholder="N02BE01" /></label>
        <label>Allergy family<input value={f.allergyClass ?? ''} onChange={e => setF({ ...f, allergyClass: e.target.value })} placeholder="Penicillins" /></label>
        {view}
        <div className="actions"><button onClick={() => setF({ isActive: true })}>New</button>
          <button className="primary" onClick={() => run(async () => { await api.saveIngredient(f.ingredientId ?? null, { name: f.name ?? '', atcCode: f.atcCode || null, allergyClass: f.allergyClass || null, isActive: f.isActive ?? true }); await load() })}>Save</button></div>
      </aside>
    </div>
  )
}

function Interactions() {
  const [rows, setRows] = useState<InteractionRow[]>([])
  const [ingredients, setIngredients] = useState<Ingredient[]>([])
  const [f, setF] = useState({ a: 0, b: 0, severity: 'Moderate', description: '' })
  const { run, view } = useSaver()
  const load = useCallback(() => api.interactions().then(setRows), [])
  useEffect(() => { load(); api.ingredients().then(setIngredients) }, [load])
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Ingredients</th><th>Severity</th><th>What happens</th><th /></tr></thead>
        <tbody>{rows.map(r => <tr key={`${r.ingredientAId}-${r.ingredientBId}`} className="clickable" onClick={() => setF({ a: r.ingredientAId, b: r.ingredientBId, severity: r.severity, description: r.description })}>
          <td>{r.a} + {r.b}</td><td><span className={`badge sev-${r.severity}`}>{r.severity}</span></td><td className="small">{r.description}</td>
          <td><button className="quiet" onClick={e => { e.stopPropagation(); if (window.confirm('Remove this interaction?')) api.deleteInteraction(r.ingredientAId, r.ingredientBId).then(load) }}>✕</button></td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>Interaction</h2>
        <label>Ingredient<select value={f.a} onChange={e => setF({ ...f, a: Number(e.target.value) })}><option value={0}>Choose…</option>{ingredients.map(i => <option key={i.ingredientId} value={i.ingredientId}>{i.name}</option>)}</select></label>
        <label>with<select value={f.b} onChange={e => setF({ ...f, b: Number(e.target.value) })}><option value={0}>Choose…</option>{ingredients.map(i => <option key={i.ingredientId} value={i.ingredientId}>{i.name}</option>)}</select></label>
        <label>Severity<select value={f.severity} onChange={e => setF({ ...f, severity: e.target.value })}><option>Major</option><option>Moderate</option><option>Minor</option></select></label>
        <label>What happens<textarea rows={3} value={f.description} onChange={e => setF({ ...f, description: e.target.value })} /></label>
        {view}
        <div className="actions"><button className="primary" onClick={() => run(async () => { await api.saveInteraction({ ingredientAId: f.a, ingredientBId: f.b, severity: f.severity, description: f.description }); await load() })}>Save</button></div>
      </aside>
    </div>
  )
}

function Manufacturers() {
  const { ref } = useRef_()
  const [f, setF] = useState<{ id?: number; name: string; country: string; isActive: boolean }>({ name: '', country: 'CA', isActive: true })
  const { run, view } = useSaver()
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Manufacturer</th><th>Country</th></tr></thead>
        <tbody>{ref.manufacturers.map(m => <tr key={m.manufacturerId} className="clickable" onClick={() => setF({ id: m.manufacturerId, name: m.name, country: m.country ?? '', isActive: m.isActive })}><td>{m.name}</td><td>{m.country}</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>Manufacturer</h2>
        <label>Name<input value={f.name} onChange={e => setF({ ...f, name: e.target.value })} /></label>
        <label>Country<input value={f.country} maxLength={2} onChange={e => setF({ ...f, country: e.target.value.toUpperCase() })} /></label>
        {view}
        <div className="actions"><button onClick={() => setF({ name: '', country: 'CA', isActive: true })}>New</button>
          <button className="primary" onClick={() => run(() => api.saveManufacturer(f.id ?? null, { name: f.name, country: f.country || null, isActive: f.isActive }))}>Save</button></div>
      </aside>
    </div>
  )
}

function Suppliers() {
  const { ref } = useRef_()
  const [f, setF] = useState<Partial<Supplier>>({ isActive: true })
  const { run, view } = useSaver()
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Code</th><th>Supplier</th><th>Phone</th><th>Account</th></tr></thead>
        <tbody>{ref.suppliers.map(s => <tr key={s.supplierId} className="clickable" onClick={() => setF(s)}><td className="mono">{s.code}</td><td>{s.name}</td><td>{s.phone}</td><td>{s.accountNumber}</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>Supplier</h2>
        <div className="form-row"><label>Code<input value={f.code ?? ''} onChange={e => setF({ ...f, code: e.target.value })} /></label><label>Account<input value={f.accountNumber ?? ''} onChange={e => setF({ ...f, accountNumber: e.target.value })} /></label></div>
        <label>Name<input value={f.name ?? ''} onChange={e => setF({ ...f, name: e.target.value })} /></label>
        <div className="form-row"><label>Phone<input value={f.phone ?? ''} onChange={e => setF({ ...f, phone: e.target.value })} /></label><label>Email<input value={f.email ?? ''} onChange={e => setF({ ...f, email: e.target.value })} /></label></div>
        {view}
        <div className="actions"><button onClick={() => setF({ isActive: true })}>New</button>
          <button className="primary" onClick={() => run(() => api.saveSupplier(f.supplierId ?? null, { code: f.code ?? '', name: f.name ?? '', phone: f.phone, email: f.email, accountNumber: f.accountNumber, isActive: f.isActive ?? true }))}>Save</button></div>
      </aside>
    </div>
  )
}

function Plans() {
  const { ref } = useRef_()
  const [f, setF] = useState<Partial<Plan>>({ payerType: 'Insurer', copayAmount: 0, copayPercent: 0, dispensingFee: 0, markupPercent: 0, isActive: true })
  const { run, view } = useSaver()
  const n = (v: unknown) => Number(v ?? 0)
  return (
    <div className="split">
      <section className="card"><table className="data"><thead><tr><th>Plan</th><th>Payer</th><th className="num">Co-pay</th><th className="num">Dispensing fee</th><th className="num">Mark-up</th></tr></thead>
        <tbody>{ref.plans.map(p => <tr key={p.planId} className={`clickable ${!p.isActive ? 'inactive' : ''}`} onClick={() => setF(p)}>
          <td>{p.name}<div className="muted small">{p.notes}</div></td><td>{p.payerType}</td>
          <td className="num">{p.copayAmount ? money(p.copayAmount) : ''}{p.copayPercent ? ` ${p.copayPercent}%` : ''}</td><td className="num">{money(p.dispensingFee)}</td><td className="num">{p.markupPercent}%</td></tr>)}</tbody></table></section>
      <aside className="card pad side">
        <h2>Payer plan</h2>
        <p className="muted small">The amounts are the pharmacy's settings: confirm them against the current ODB agreement and each insurer's contract.</p>
        <div className="form-row"><label>Code<input value={f.code ?? ''} onChange={e => setF({ ...f, code: e.target.value })} /></label>
          <label>Payer<select value={f.payerType} onChange={e => setF({ ...f, payerType: e.target.value })}>{['ODB', 'Insurer', 'Cash', 'Other'].map(x => <option key={x}>{x}</option>)}</select></label></div>
        <label>Name<input value={f.name ?? ''} onChange={e => setF({ ...f, name: e.target.value })} /></label>
        <div className="form-row">
          <label>Co-pay ($)<input inputMode="decimal" value={f.copayAmount ?? 0} onChange={e => setF({ ...f, copayAmount: e.target.value as unknown as number })} /></label>
          <label>Co-pay (%)<input inputMode="decimal" value={f.copayPercent ?? 0} onChange={e => setF({ ...f, copayPercent: e.target.value as unknown as number })} /></label>
          <label>Dispensing fee ($)<input inputMode="decimal" value={f.dispensingFee ?? 0} onChange={e => setF({ ...f, dispensingFee: e.target.value as unknown as number })} /></label>
          <label>Mark-up (%)<input inputMode="decimal" value={f.markupPercent ?? 0} onChange={e => setF({ ...f, markupPercent: e.target.value as unknown as number })} /></label>
        </div>
        <label>Notes<input value={f.notes ?? ''} onChange={e => setF({ ...f, notes: e.target.value })} /></label>
        {view}
        <div className="actions"><button onClick={() => setF({ payerType: 'Insurer', copayAmount: 0, copayPercent: 0, dispensingFee: 0, markupPercent: 0, isActive: true })}>New</button>
          <button className="primary" onClick={() => run(() => api.savePlan(f.planId ?? null, { code: f.code ?? '', name: f.name ?? '', payerType: f.payerType ?? 'Insurer', copayAmount: n(f.copayAmount),
            copayPercent: n(f.copayPercent), dispensingFee: n(f.dispensingFee), markupPercent: n(f.markupPercent), notes: f.notes, isActive: f.isActive ?? true }))}>Save</button></div>
      </aside>
    </div>
  )
}
