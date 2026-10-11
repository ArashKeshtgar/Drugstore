import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, firstError, type Drug, type Ingredient } from '../api'
import { useRef_ } from '../App'

type Form = Partial<Drug>

/** The drug catalog: one product per DIN, with its ingredient, schedule and ODB status. */
export default function Drugs() {
  const [q, setQ] = useState('')
  const [all, setAll] = useState(false)
  const [rows, setRows] = useState<Drug[] | null>(null)
  const [edit, setEdit] = useState<Form | null>(null)
  const load = useCallback(() => api.drugs(q, all).then(setRows), [q, all])
  useEffect(() => { const t = setTimeout(load, 200); return () => clearTimeout(t) }, [load])

  return (
    <>
      <div className="head"><div><h1>Drugs</h1><div className="muted">Each product has its DIN (Health Canada's 8-digit Drug Identification Number). The demo catalog's DINs are made up.</div></div></div>
      <div className={edit ? 'split' : ''}>
        <section className="card">
          <div className="filters">
            <input type="search" value={q} onChange={e => setQ(e.target.value)} placeholder="DIN, product or ingredient" style={{ maxWidth: 320 }} />
            <label className="check"><input type="checkbox" checked={all} onChange={e => setAll(e.target.checked)} /> Include inactive</label>
            <button className="primary small" style={{ marginLeft: 'auto' }} onClick={() => setEdit({ formCode: 'TAB', routeCode: 'PO', scheduleCode: 'I', dispensingUnit: 'TAB', packSize: 100, unitCost: 0, isActive: true, odbBenefit: false, odbLimitedUse: false })}>Add product</button>
          </div>
          <div className="table-wrap"><table className="data">
            <thead><tr><th>DIN</th><th>Product</th><th>Ingredient</th><th>Schedule</th><th>ODB</th><th className="num">Cost / unit</th></tr></thead>
            <tbody>{rows?.map(d => (
              <tr key={d.drugId} className={`clickable ${!d.isActive ? 'inactive' : ''} ${edit?.drugId === d.drugId ? 'selected' : ''}`} onClick={() => setEdit(d)}>
                <td className="mono">{d.din}{d.isDemo && <span className="badge"> demo</span>}</td>
                <td>{d.brandName}<div className="muted small">{d.strength ? `${d.strength} ${d.strengthUnitName ?? d.strengthUnit} · ` : ''}{d.form} · pack {d.packSize}</div></td>
                <td>{d.ingredient}</td>
                <td>{d.scheduleCode}{d.monitored && <span className="badge warn" title="Narcotics Monitoring System"> NMS</span>}</td>
                <td>{d.odbBenefit ? (d.odbLimitedUse ? 'LU' : 'Yes') : ''}</td>
                <td className="num">{d.unitCost.toFixed(4)}</td>
              </tr>))}</tbody>
          </table></div>
        </section>
        {edit && <DrugEditor key={edit.drugId ?? 'new'} drug={edit} onClose={() => setEdit(null)} onSaved={d => { setEdit(d); load() }} />}
      </div>
    </>
  )
}

function DrugEditor({ drug, onClose, onSaved }: { drug: Form; onClose: () => void; onSaved: (d: Drug) => void }) {
  const { ref } = useRef_()
  const [form, setForm] = useState<Form>(drug)
  const [ingredients, setIngredients] = useState<Ingredient[]>([])
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [msg, setMsg] = useState<string | null>(null)
  useEffect(() => { api.ingredients().then(setIngredients) }, [])
  const set = (k: keyof Drug, v: unknown) => setForm({ ...form, [k]: v })
  const err = (k: string) => errors[k]?.[0] && <span className="error small">{errors[k][0]}</span>
  const save = async () => {
    setErrors({}); setMsg(null)
    try {
      const saved = await api.saveDrug(form.drugId ?? null, {
        din: form.din ?? '', brandName: form.brandName ?? '', ingredientId: Number(form.ingredientId ?? 0), strength: form.strength ? Number(form.strength) : undefined,
        strengthUnit: form.strengthUnit || undefined, formCode: form.formCode ?? '', routeCode: form.routeCode ?? '', scheduleCode: form.scheduleCode ?? '',
        manufacturerId: form.manufacturerId ? Number(form.manufacturerId) : undefined, packSize: Number(form.packSize ?? 1), dispensingUnit: form.dispensingUnit ?? '',
        unitCost: Number(form.unitCost ?? 0), odbBenefit: !!form.odbBenefit, odbLimitedUse: !!form.odbLimitedUse, interchangeGroup: form.interchangeGroup || undefined, isActive: form.isActive ?? true,
      })
      setMsg('Saved.'); onSaved(saved)
    } catch (e) { if (e instanceof ApiError && Object.keys(e.fieldErrors).length) setErrors(e.fieldErrors); else setMsg(firstError(e, 'Not saved.')) }
  }
  return (
    <aside className="card pad side">
      <div className="side-head"><h2>{form.drugId ? form.brandName : 'Add a product'}</h2><button className="quiet" onClick={onClose}>✕</button></div>
      {form.isDemo && <p className="notice small">Demo product: its DIN and cost are made up.</p>}
      <div className="form-row">
        <label>DIN<input value={form.din ?? ''} maxLength={8} inputMode="numeric" onChange={e => set('din', e.target.value)} />{err('din')}</label>
        <label>Interchangeable group<input value={form.interchangeGroup ?? ''} onChange={e => set('interchangeGroup', e.target.value)} />{err('interchangeGroup')}</label>
      </div>
      <label>Product name<input value={form.brandName ?? ''} onChange={e => set('brandName', e.target.value)} />{err('brandName')}</label>
      <label>Ingredient<select value={form.ingredientId ?? ''} onChange={e => set('ingredientId', Number(e.target.value))}>
        <option value="">Choose…</option>{ingredients.map(i => <option key={i.ingredientId} value={i.ingredientId}>{i.name}</option>)}</select>{err('ingredientId')}</label>
      <div className="form-row">
        <label>Strength<input inputMode="decimal" value={form.strength ?? ''} onChange={e => set('strength', e.target.value)} />{err('strength')}</label>
        <label>Unit<select value={form.strengthUnit ?? ''} onChange={e => set('strengthUnit', e.target.value)}><option value="">—</option>
          {ref.units.filter(u => u.kind === 'Strength').map(u => <option key={u.code} value={u.code}>{u.name}</option>)}</select></label>
      </div>
      <div className="form-row">
        <label>Form<select value={form.formCode} onChange={e => set('formCode', e.target.value)}>{ref.forms.map(f => <option key={f.code} value={f.code}>{f.name}</option>)}</select></label>
        <label>Route<select value={form.routeCode} onChange={e => set('routeCode', e.target.value)}>{ref.routes.map(r => <option key={r.code} value={r.code}>{r.name}</option>)}</select></label>
      </div>
      <div className="form-row">
        <label>Schedule<select value={form.scheduleCode} onChange={e => set('scheduleCode', e.target.value)}>{ref.schedules.map(s => <option key={s.code} value={s.code}>{s.name}</option>)}</select></label>
        <label>Manufacturer<select value={form.manufacturerId ?? ''} onChange={e => set('manufacturerId', e.target.value)}><option value="">—</option>{ref.manufacturers.map(m => <option key={m.manufacturerId} value={m.manufacturerId}>{m.name}</option>)}</select></label>
      </div>
      <div className="form-row">
        <label>Pack size<input type="number" value={form.packSize ?? 1} onChange={e => set('packSize', Number(e.target.value))} />{err('packSize')}</label>
        <label>Dispensed as<select value={form.dispensingUnit} onChange={e => set('dispensingUnit', e.target.value)}>{ref.units.filter(u => u.kind === 'Dispensing').map(u => <option key={u.code} value={u.code}>{u.name}</option>)}</select></label>
        <label>Cost per unit<input inputMode="decimal" value={form.unitCost ?? 0} onChange={e => set('unitCost', e.target.value)} />{err('unitCost')}</label>
      </div>
      <label className="check"><input type="checkbox" checked={!!form.odbBenefit} onChange={e => set('odbBenefit', e.target.checked)} /> On the ODB Formulary</label>
      <label className="check"><input type="checkbox" checked={!!form.odbLimitedUse} onChange={e => set('odbLimitedUse', e.target.checked)} /> ODB Limited Use (needs the prescriber's LU code)</label>
      <label className="check"><input type="checkbox" checked={form.isActive ?? true} onChange={e => set('isActive', e.target.checked)} /> Active</label>
      {msg && <p className="notice">{msg}</p>}
      <div className="actions"><button className="primary" onClick={save}>Save</button></div>
    </aside>
  )
}
