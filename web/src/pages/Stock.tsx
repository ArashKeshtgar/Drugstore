import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, dateTime, drugLabel, firstError, money, today, type Drug } from '../api'
import { useRef_ } from '../App'

type Line = { drugId: number; lotNumber: string; expiryDate: string; quantity: string; unitCost: string }
const blank = (): Line => ({ drugId: 0, lotNumber: '', expiryDate: '', quantity: '', unitCost: '' })

/** Purchasing and stock: supplier invoices received into lots, and what is on the shelf by expiry. */
export default function Stock() {
  const [tab, setTab] = useState<'stock' | 'purchases'>('stock')
  const [receiving, setReceiving] = useState(false)
  const [stock, setStock] = useState<Awaited<ReturnType<typeof api.stock>>>([])
  const [purchases, setPurchases] = useState<Awaited<ReturnType<typeof api.purchases>>>([])
  const load = useCallback(() => Promise.all([api.stock().then(setStock), api.purchases().then(setPurchases)]), [])
  useEffect(() => { load() }, [load])
  return (
    <>
      <div className="head">
        <div><h1>Stock</h1><div className="muted">Lots received from suppliers. Dispensing takes from the lot that expires first; expired stock is never dispensed.</div></div>
        <button className="primary" onClick={() => setReceiving(true)}>Receive an invoice</button>
      </div>
      <div className="tabs">
        <button className={`tab ${tab === 'stock' ? 'on' : ''}`} onClick={() => setTab('stock')}>On hand</button>
        <button className={`tab ${tab === 'purchases' ? 'on' : ''}`} onClick={() => setTab('purchases')}>Supplier invoices</button>
      </div>
      <div className={receiving ? 'split' : ''}>
        <section className="card">
          {tab === 'stock' ? (
            <table className="data"><thead><tr><th>Product</th><th>DIN</th><th>Lot</th><th>Expires</th><th className="num">On hand</th><th className="num">Cost / unit</th></tr></thead>
              <tbody>{stock.map(l => <tr key={l.lotId}><td>{l.drug}</td><td className="mono">{l.din}</td><td className="mono">{l.lotNumber}</td>
                <td>{l.expiryDate}{l.expired ? <span className="badge sev-Major"> expired</span> : l.expiresSoon ? <span className="badge warn"> within 90 days</span> : null}</td>
                <td className="num">{l.quantityOnHand}</td><td className="num">{l.unitCost.toFixed(4)}</td></tr>)}
                {stock.length === 0 && <tr><td colSpan={6} className="muted">Nothing on hand. Receive a supplier invoice.</td></tr>}</tbody></table>
          ) : (
            <table className="data"><thead><tr><th>Received</th><th>Supplier</th><th>Invoice</th><th className="num">Drugs</th><th className="num">Supplies + HST</th><th className="num">Total</th></tr></thead>
              <tbody>{purchases.map(p => <tr key={p.purchaseId}><td className="small">{dateTime(p.receivedAt)}</td><td>{p.supplier}</td><td className="mono">{p.invoiceNumber} · {p.invoiceDate}</td>
                <td className="num">{money(p.drugsAmount)}</td><td className="num">{money(p.suppliesAmount + p.suppliesHst)}</td><td className="num">{money(p.total)}</td></tr>)}</tbody></table>
          )}
        </section>
        {receiving && <ReceiveInvoice onClose={() => setReceiving(false)} onSaved={() => { setReceiving(false); load() }} />}
      </div>
    </>
  )
}

function ReceiveInvoice({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const { ref } = useRef_()
  const [drugs, setDrugs] = useState<Drug[]>([])
  const [form, setForm] = useState({ supplierId: 0, invoiceNumber: '', invoiceDate: today(), suppliesAmount: '', suppliesHst: '' })
  const [lines, setLines] = useState<Line[]>([blank()])
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [error, setError] = useState<string | null>(null)
  useEffect(() => { api.drugs().then(setDrugs) }, [])
  const setLine = (i: number, patch: Partial<Line>) => setLines(lines.map((l, j) => j === i ? { ...l, ...patch } : l))
  const err = (k: string) => errors[k]?.[0] && <span className="error small">{errors[k][0]}</span>
  const drugsTotal = lines.reduce((s, l) => s + (Number(l.quantity) || 0) * (Number(l.unitCost) || 0), 0)
  const save = async () => {
    setErrors({}); setError(null)
    try {
      await api.receivePurchase({ supplierId: form.supplierId, invoiceNumber: form.invoiceNumber, invoiceDate: form.invoiceDate,
        suppliesAmount: Number(form.suppliesAmount) || 0, suppliesHst: Number(form.suppliesHst) || 0,
        lines: lines.filter(l => l.drugId).map(l => ({ drugId: l.drugId, lotNumber: l.lotNumber, expiryDate: l.expiryDate, quantity: Number(l.quantity), unitCost: Number(l.unitCost) })) })
      onSaved()
    } catch (e) { if (e instanceof ApiError && Object.keys(e.fieldErrors).length) setErrors(e.fieldErrors); else setError(firstError(e, 'Not saved.')) }
  }
  return (
    <aside className="card pad side wide">
      <div className="side-head"><h2>Receive a supplier invoice</h2><button className="quiet" onClick={onClose}>✕</button></div>
      <div className="form-row">
        <label>Supplier<select value={form.supplierId} onChange={e => setForm({ ...form, supplierId: Number(e.target.value) })}><option value={0}>Choose…</option>
          {ref.suppliers.filter(s => s.isActive).map(s => <option key={s.supplierId} value={s.supplierId}>{s.name}</option>)}</select>{err('supplierId')}</label>
        <label>Invoice number<input value={form.invoiceNumber} onChange={e => setForm({ ...form, invoiceNumber: e.target.value })} />{err('invoiceNumber')}</label>
        <label>Invoice date<input type="date" value={form.invoiceDate} onChange={e => setForm({ ...form, invoiceDate: e.target.value })} />{err('invoiceDate')}</label>
      </div>
      <table className="data lines"><thead><tr><th>Product</th><th>Lot</th><th>Expiry</th><th className="num">Qty</th><th className="num">Cost / unit</th><th /></tr></thead>
        <tbody>{lines.map((l, i) => <tr key={i}>
          <td><select value={l.drugId} onChange={e => { const d = drugs.find(x => x.drugId === Number(e.target.value)); setLine(i, { drugId: Number(e.target.value), unitCost: l.unitCost || (d ? String(d.unitCost) : '') }) }}>
            <option value={0}>Choose…</option>{drugs.map(d => <option key={d.drugId} value={d.drugId}>{drugLabel(d)}</option>)}</select>{err(`lines[${i}].drugId`)}</td>
          <td><input value={l.lotNumber} onChange={e => setLine(i, { lotNumber: e.target.value })} />{err(`lines[${i}].lotNumber`)}</td>
          <td><input type="date" value={l.expiryDate} onChange={e => setLine(i, { expiryDate: e.target.value })} />{err(`lines[${i}].expiryDate`)}</td>
          <td><input className="num" inputMode="decimal" value={l.quantity} onChange={e => setLine(i, { quantity: e.target.value })} /></td>
          <td><input className="num" inputMode="decimal" value={l.unitCost} onChange={e => setLine(i, { unitCost: e.target.value })} /></td>
          <td>{lines.length > 1 && <button className="quiet" onClick={() => setLines(lines.filter((_, j) => j !== i))}>✕</button>}</td></tr>)}
          <tr className="total"><td colSpan={3}><button className="link" onClick={() => setLines([...lines, blank()])}>+ Another lot</button></td><td colSpan={2} className="num">Drugs {money(drugsTotal)}</td><td /></tr>
        </tbody></table>
      <div className="form-row">
        <label>Other supplies (before tax)<input inputMode="decimal" value={form.suppliesAmount} onChange={e => setForm({ ...form, suppliesAmount: e.target.value })} /></label>
        <label>HST on supplies<input inputMode="decimal" value={form.suppliesHst} placeholder={form.suppliesAmount ? (Number(form.suppliesAmount) * 0.13).toFixed(2) : ''} onChange={e => setForm({ ...form, suppliesHst: e.target.value })} />{err('suppliesHst')}</label>
      </div>
      <p className="muted small">Prescription drugs are zero-rated; supplies carry 13 % HST, which the pharmacy claims back. The invoice goes to the accounting as a bill from the supplier.</p>
      {err('lines')}{error && <p className="error">{error}</p>}
      <div className="actions"><button onClick={onClose}>Cancel</button><button className="primary" onClick={save}>Receive</button></div>
    </aside>
  )
}
