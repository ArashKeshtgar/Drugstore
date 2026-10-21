import { useCallback, useEffect, useState } from 'react'
import { RestrictedPanel } from '../components/RestrictedPanel'
import { api, ApiError, dateTime, drugLabel, firstError, isRestricted, money, today, type Drug, type ItemInput, type PatientRow, type Prescriber, type QueueRow, type Rx, type Warning } from '../api'
import { useRef_ } from '../App'

const STATUSES = [['', 'Open'], ['Received', 'Received'], ['InProgress', 'In progress'], ['OnHold', 'On hold'], ['Completed', 'Completed'], ['Cancelled', 'Cancelled']]

/** Reception: prescriptions coming in, entered drug by drug and checked before they are filled. */
export default function Reception() {
  const [status, setStatus] = useState('')
  const [rows, setRows] = useState<QueueRow[] | null>(null)
  const [open, setOpen] = useState<number | null>(null)
  const [creating, setCreating] = useState(false)
  const load = useCallback(() => api.queue(status).then(setRows), [status])
  useEffect(() => { setRows(null); load() }, [load])

  return (
    <>
      <div className="head">
        <div><h1>Reception</h1><div className="muted">Prescriptions as they arrive: paper, fax, phone or electronic. Each drug is checked against the patient's allergies and medicines.</div></div>
        <button className="primary" onClick={() => { setOpen(null); setCreating(true) }}>Receive a prescription</button>
      </div>
      <div className={creating || open ? 'split' : ''}>
        <section className="card">
          <div className="filters">{STATUSES.map(([k, l]) => <button key={k} className={`chip ${status === k ? 'on' : ''}`} onClick={() => setStatus(k)}>{l}</button>)}</div>
          <div className="table-wrap"><table className="data">
            <thead><tr><th>Rx</th><th>Received</th><th>Patient</th><th>Prescriber</th><th>Plan</th><th>Drugs</th><th>Status</th></tr></thead>
            <tbody>
              {rows?.map(r => (
                <tr key={r.prescriptionId} className={`clickable ${open === r.prescriptionId ? 'selected' : ''}`} onClick={() => { setCreating(false); setOpen(r.prescriptionId) }}>
                  <td className="mono">{r.rxNumber}</td><td className="small">{dateTime(r.receivedAt)} · {r.channel}</td><td>{r.patient}</td><td>{r.prescriber}</td><td>{r.plan}</td>
                  <td>{r.ready}/{r.items} ready{r.warnings > 0 && <span className="badge warn" title="Findings not reviewed yet"> {r.warnings} to review</span>}</td>
                  <td><span className={`badge ${r.status}`}>{r.status}</span></td>
                </tr>
              ))}
              {rows?.length === 0 && <tr><td colSpan={7} className="muted">Nothing here.</td></tr>}
            </tbody>
          </table></div>
        </section>
        {creating && <ReceiveForm onClose={() => setCreating(false)} onSaved={id => { setCreating(false); load(); setOpen(id) }} />}
        {!creating && open && <RxView key={open} id={open} onChanged={load} onClose={() => setOpen(null)} />}
      </div>
    </>
  )
}

function Warnings({ list }: { list: Warning[] }) {
  if (list.length === 0) return <span className="ok small">No findings</span>
  return <ul className="warnings">{list.map((w, i) => <li key={i} className={`w-${w.severity}`}><strong>{w.kind.replace(/([a-z])([A-Z])/g, '$1 $2')}</strong> {w.message}</li>)}</ul>
}

function useSearch<T>(fetcher: (q: string) => Promise<T[]>, min = 2) {
  const [q, setQ] = useState('')
  const [rows, setRows] = useState<T[]>([])
  useEffect(() => {
    const t = setTimeout(() => { if (q.trim().length >= min) fetcher(q).then(setRows); else setRows([]) }, 200)
    return () => clearTimeout(t)
  }, [q]) // eslint-disable-line react-hooks/exhaustive-deps
  return { q, setQ, rows }
}

const blankItem = (): ItemInput => ({ drugId: null, drugText: '', sig: '', quantity: 0, daysSupply: null, refillsAuthorized: 0, noSubstitution: false })

function ReceiveForm({ onClose, onSaved }: { onClose: () => void; onSaved: (id: number) => void }) {
  const { ref } = useRef_()
  const patients = useSearch<PatientRow>(api.patients)
  const [patient, setPatient] = useState<PatientRow | null>(null)
  const [prescribers, setPrescribers] = useState<Prescriber[]>([])
  const [form, setForm] = useState({ prescriberId: 0, writtenDate: today(), channel: 'Paper', planId: '' as string, notes: '' })
  const [items, setItems] = useState<ItemInput[]>([blankItem()])
  const [drugs, setDrugs] = useState<Drug[]>([])
  const [checks, setChecks] = useState<{ sigExpanded: string; daysSupply?: number; warnings: Warning[] }[]>([])
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { api.prescribers().then(setPrescribers); api.drugs().then(setDrugs) }, [])

  // Live checks while typing: the label text and what reception would find.
  useEffect(() => {
    if (!patient) return
    const ready = items.filter(i => i.sig.trim() && i.quantity > 0)
    if (ready.length === 0) return
    const t = setTimeout(() => api.check({ patientId: patient.patientId, writtenDate: form.writtenDate, planId: form.planId ? Number(form.planId) : null, items })
      .then(setChecks).catch(() => {}), 350)
    return () => clearTimeout(t)
  }, [patient, items, form.writtenDate, form.planId])

  const setItem = (i: number, patch: Partial<ItemInput>) => setItems(items.map((x, j) => j === i ? { ...x, ...patch } : x))
  const save = async () => {
    setErrors({}); setError(null)
    if (!patient) { setError('Choose the patient.'); return }
    try {
      const rx = await api.receive({ patientId: patient.patientId, prescriberId: form.prescriberId, writtenDate: form.writtenDate, channel: form.channel,
        planId: form.planId ? Number(form.planId) : null, notes: form.notes || null, items: items.map(i => ({ ...i, quantity: Number(i.quantity), daysSupply: i.daysSupply || null })) })
      onSaved(rx.prescriptionId)
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length) setErrors(e.fieldErrors)
      else setError(firstError(e, 'Not saved.'))
    }
  }
  const err = (k: string) => errors[k]?.[0] && <span className="error small">{errors[k][0]}</span>

  return (
    <aside className="card pad side wide">
      <div className="side-head"><h2>Receive a prescription</h2><button className="quiet" onClick={onClose} aria-label="Close">✕</button></div>
      {patient ? (
        <div className="picked"><strong>{patient.name}</strong> <span className="muted small">born {patient.dateOfBirth}{patient.allergies > 0 ? ` · ${patient.allergies} allergies on file` : ' · no allergies on file'}</span>
          <button className="link" onClick={() => setPatient(null)}>change</button></div>
      ) : (
        <label>Patient<input value={patients.q} onChange={e => patients.setQ(e.target.value)} placeholder="Last name, health card, birth date (yyyy-mm-dd) or phone" autoFocus />
          {patients.rows.length > 0 && <ul className="pick-list">{patients.rows.map(p => <li key={p.patientId}><button onClick={() => setPatient(p)}>{p.name} <span className="muted small">{p.dateOfBirth}</span></button></li>)}</ul>}
          {err('patientId')}<span className="muted small">New patient? Register them under Patients &amp; prescribers.</span></label>
      )}
      <div className="form-row">
        <label>Prescriber<select value={form.prescriberId} onChange={e => setForm({ ...form, prescriberId: Number(e.target.value) })}>
          <option value={0}>Choose…</option>{prescribers.filter(p => p.isActive).map(p => <option key={p.prescriberId} value={p.prescriberId}>{p.lastName}, {p.firstName} ({p.licenceBody} {p.licenceNumber})</option>)}</select>{err('prescriberId')}</label>
        <label>Written<input type="date" value={form.writtenDate} onChange={e => setForm({ ...form, writtenDate: e.target.value })} />{err('writtenDate')}</label>
        <label>Received by<select value={form.channel} onChange={e => setForm({ ...form, channel: e.target.value })}>{['Paper', 'Fax', 'Phone', 'eRx'].map(c => <option key={c}>{c}</option>)}</select></label>
        <label>Plan<select value={form.planId} onChange={e => setForm({ ...form, planId: e.target.value })}>
          <option value="">Patient's usual plan</option>{ref.plans.filter(p => p.isActive).map(p => <option key={p.planId} value={p.planId}>{p.name}</option>)}</select></label>
      </div>
      {items.map((it, i) => (
        <fieldset key={i} className="rx-item">
          <legend>Drug {i + 1}{items.length > 1 && <button className="quiet" onClick={() => setItems(items.filter((_, j) => j !== i))} aria-label="Remove drug">✕</button>}</legend>
          <div className="form-row">
            <label>As written<input value={it.drugText} onChange={e => setItem(i, { drugText: e.target.value })} placeholder="e.g. Amoxicillin 500 mg" />{err(`items[${i}].drugText`)}</label>
            <label>Product<select value={it.drugId ?? ''} onChange={e => {
              const d = drugs.find(x => x.drugId === Number(e.target.value))
              setItem(i, { drugId: d ? d.drugId : null, drugText: it.drugText || (d ? drugLabel(d) : '') })
            }}><option value="">Match later…</option>{drugs.map(d => <option key={d.drugId} value={d.drugId}>{drugLabel(d)} · DIN {d.din}</option>)}</select>{err(`items[${i}].drugId`)}</label>
          </div>
          <div className="form-row">
            <label>Directions (SIG)<input value={it.sig} onChange={e => setItem(i, { sig: e.target.value })} placeholder="1 TAB PO BID X 10 D" />{err(`items[${i}].sig`)}</label>
            <label>Quantity<input inputMode="decimal" value={it.quantity || ''} onChange={e => setItem(i, { quantity: Number(e.target.value) || 0 })} />{err(`items[${i}].quantity`)}</label>
            <label>Days' supply<input inputMode="numeric" value={it.daysSupply ?? ''} placeholder={checks[i]?.daysSupply ? String(checks[i].daysSupply) : ''} onChange={e => setItem(i, { daysSupply: Number(e.target.value) || null })} /></label>
            <label>Refills<input type="number" min={0} max={12} value={it.refillsAuthorized} onChange={e => setItem(i, { refillsAuthorized: Number(e.target.value) })} />{err(`items[${i}].refillsAuthorized`)}</label>
          </div>
          <label className="check"><input type="checkbox" checked={it.noSubstitution} onChange={e => setItem(i, { noSubstitution: e.target.checked })} /> No substitution</label>
          {checks[i] && <div className="label-preview"><span className="muted small">Label:</span> {checks[i].sigExpanded}</div>}
          {checks[i] && <Warnings list={checks[i].warnings} />}
        </fieldset>
      ))}
      <button className="link" onClick={() => setItems([...items, blankItem()])}>+ Another drug</button>
      <label>Notes<input value={form.notes} onChange={e => setForm({ ...form, notes: e.target.value })} /></label>
      {err('items')}
      {error && <p className="error">{error}</p>}
      <div className="actions"><button onClick={onClose}>Cancel</button><button className="primary" onClick={save}>Receive</button></div>
    </aside>
  )
}

function RxView({ id, onChanged, onClose }: { id: number; onChanged: () => void; onClose: () => void }) {
  const [rx, setRx] = useState<Rx | null>(null)
  const [drugs, setDrugs] = useState<Drug[]>([])
  const [error, setError] = useState<string | null>(null)
  const [restricted, setRestricted] = useState<string | null>(null)   // the label, while the prescription is closed
  const load = () => api.prescription(id).then(r => { setRestricted(null); setRx(r) }).catch(e => { if (isRestricted(e)) setRestricted(String(e.data.label ?? 'R')); else setError(firstError(e, 'Not opened.')) })
  useEffect(() => { setRx(null); setRestricted(null); load(); api.drugs().then(setDrugs) }, [id])
  const act = async (f: () => Promise<Rx>) => { setError(null); try { setRx(await f()); onChanged() } catch (e) { setError(firstError(e, 'Not done.')) } }
  if (restricted && !rx) return <RestrictedPanel label={restricted} onClose={onClose} onOpen={async reason => { await api.overridePrescription(id, reason); await load() }} />
  if (!rx) return <aside className="card pad side wide"><p className="muted">Loading…</p></aside>

  return (
    <aside className="card pad side wide">
      <div className="side-head"><h2>Rx <span className="mono">{rx.rxNumber}</span> <span className={`badge ${rx.status}`}>{rx.status}</span></h2><button className="quiet" onClick={onClose} aria-label="Close">✕</button></div>
      <div className="facts">
        <span>Patient</span><strong>{rx.patient} <span className="muted small">born {rx.dateOfBirth}</span></strong>
        {rx.confidentiality && <><span>Consent</span><span className="badge" title="The patient has a consent directive at the clinic: share this prescription only with those caring for them">
          {rx.confidentiality === 'V' ? 'Very restricted' : 'Restricted'}</span></>}
        <span>Allergies</span><span>{rx.allergies.length ? rx.allergies.map(a => `${a.what}${a.reaction ? ` (${a.reaction})` : ''}`).join(', ') : 'None on file'}</span>
        <span>Prescriber</span><span>{rx.prescriber} · {rx.prescriberLicence}</span>
        <span>Written</span><span>{rx.writtenDate} · received {dateTime(rx.receivedAt)} by {rx.channel.toLowerCase()} ({rx.receivedBy})</span>
        <span>Plan</span><span>{rx.plan ?? 'None'}</span>
        {rx.notes && <><span>Notes</span><span>{rx.notes}</span></>}
      </div>
      {rx.items.map(it => (
        <div key={it.itemId} className={`rx-item view ${it.status}`}>
          <div className="rx-item-head">
            <strong>{it.lineNumber}. {it.drug ?? it.drugText}</strong>
            <span className={`badge ${it.status}`}>{it.status === 'ReadyToFill' ? 'Ready to fill' : it.status}</span>
          </div>
          <div className="muted small">{it.drug ? `DIN ${it.din} · ${it.ingredient} · ${it.schedule}` : `Written: ${it.drugText}`}{it.noSubstitution && ' · no substitution'}</div>
          <div className="label-preview">{it.sigExpanded}</div>
          <div className="small">Qty {it.quantity}{it.daysSupply ? ` · ${it.daysSupply} days` : ''} · refills {it.refillsRemaining}/{it.refillsAuthorized}</div>
          <Warnings list={it.warnings} />
          {it.warningsReviewedAt && <div className="muted small">Reviewed by {it.warningsReviewedBy}, {dateTime(it.warningsReviewedAt)}</div>}
          {it.cancelReason && <div className="muted small">{it.status}: {it.cancelReason}</div>}
          {(it.status === 'ReadyToFill' || (it.status === 'Dispensed' && it.refillsRemaining > 0)) && (
            <div className="actions">
              <button className="primary" onClick={async () => {
                const method = window.prompt('If the patient pays a share, how? Cash, Debit, Credit or Account', 'Debit') ?? undefined
                setError(null)
                try {
                  const r = await api.dispense(it.itemId, method)
                  window.alert(`Dispensed. Total ${money(r.price.total)}: plan ${money(r.price.planPays)}, patient ${money(r.price.patientPays)}.${r.monitored ? ' Monitored drug: record it in the Narcotics Monitoring System.' : ''}`)
                  setRx(await api.prescription(rx.prescriptionId)); onChanged()
                } catch (e) { setError(firstError(e, 'Not dispensed.')) }
              }}>{it.status === 'Dispensed' ? `Refill (${it.refillsRemaining} left)` : 'Dispense'}</button>
            </div>
          )}
          {(it.status === 'Received' || it.status === 'OnHold') && (
            <div className="actions">
              {!it.drugId && <select defaultValue="" onChange={e => e.target.value && act(() => api.match(it.itemId, Number(e.target.value)))} aria-label="Match to a product">
                <option value="">Match to a product…</option>{drugs.map(d => <option key={d.drugId} value={d.drugId}>{drugLabel(d)}</option>)}</select>}
              <button onClick={() => act(() => api.hold(it.itemId, window.prompt('Why on hold? (optional)') ?? undefined))}>Hold</button>
              <button onClick={() => { const r = window.prompt('Why is it cancelled?'); if (r) act(() => api.cancel(it.itemId, r)) }}>Cancel</button>
              <button className="primary" disabled={!it.drugId} onClick={() => act(() => api.review(it.itemId))} title={it.warnings.length ? 'I have reviewed the findings' : undefined}>
                {it.warnings.length ? 'Reviewed: ready to fill' : 'Ready to fill'}</button>
            </div>
          )}
        </div>
      ))}
      {error && <p className="error">{error}</p>}
    </aside>
  )
}
