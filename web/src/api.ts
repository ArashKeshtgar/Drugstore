// Typed client for PharmaFlow.Api. Shapes mirror the C# records in src/PharmaFlow.Api.

export class ApiError extends Error {
  constructor(public status: number, message: string, public fieldErrors: Record<string, string[]> = {}) { super(message) }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method, credentials: 'same-origin',
    // X-PharmaFlow: with sign-in through ClinicHub the session is a cookie, so the API wants proof the call comes from this app.
    headers: { 'X-PharmaFlow': '1', ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) throw new ApiError(res.status, data?.detail ?? data?.title ?? `Request failed (${res.status})`, data?.errors ?? {})
  return data as T
}

export const firstError = (e: unknown, fallback: string) =>
  e instanceof ApiError ? Object.values(e.fieldErrors).flat()[0] ?? e.message : fallback

/** name is null when nobody is signed in. permissions: reception, dispense, stock, admin (admin allows all). */
export type Me = { name: string | null; permissions: string[]; clinic: string | null }
export type Code = { code: string; name: string; nameFr?: string; isActive: boolean }
export type Unit = { code: string; name: string; kind: 'Strength' | 'Dispensing'; isActive: boolean }
export type Schedule = { code: string; name: string; requiresPrescription: boolean; monitored: boolean; description?: string }
export type SigCode = { code: string; expansion: string; expansionFr?: string; dosesPerDay?: number; isActive: boolean }
export type Manufacturer = { manufacturerId: number; name: string; country?: string; isActive: boolean }
export type Supplier = { supplierId: number; code: string; name: string; phone?: string; email?: string; accountNumber?: string; isActive: boolean }
export type Plan = { planId: number; code: string; name: string; payerType: string; copayAmount: number; copayPercent: number; dispensingFee: number; markupPercent: number; notes?: string; isActive: boolean }
export type Ref = { forms: Code[]; routes: Code[]; units: Unit[]; schedules: Schedule[]; sigCodes: SigCode[]; manufacturers: Manufacturer[]; suppliers: Supplier[]; plans: Plan[] }
export type Ingredient = { ingredientId: number; name: string; atcCode?: string; allergyClass?: string; isActive: boolean }
export type InteractionRow = { ingredientAId: number; a: string; ingredientBId: number; b: string; severity: string; description: string }
export type Drug = {
  drugId: number; din: string; brandName: string; ingredientId: number; ingredient: string; strength?: number; strengthUnit?: string; formCode: string; routeCode: string
  scheduleCode: string; schedule: string; monitored: boolean; manufacturerId?: number; manufacturer?: string; packSize: number; dispensingUnit: string; unitCost: number
  odbBenefit: boolean; odbLimitedUse: boolean; interchangeGroup?: string; isActive: boolean; isDemo: boolean; form: string; strengthUnitName?: string
}
export type PatientRow = { patientId: number; name: string; dateOfBirth: string; sex: string; phone?: string; allergies: number }
export type PatientFields = {
  healthCardNumber?: string | null; healthCardVersion?: string | null; firstName: string; lastName: string; dateOfBirth?: string | null; sex?: string
  phone?: string | null; email?: string | null; addressLine1?: string | null; city?: string | null; postalCode?: string | null; defaultPlanId?: number | null
  planMemberId?: string | null; notes?: string | null; rowVersion?: string
}
export type Allergy = { allergyId: number; ingredientId?: number; ingredient?: string; allergyClass?: string; description?: string; reaction?: string; severity: string }
export type PatientProfile = { patient: PatientFields & { patientId: number }; allergies: Allergy[]; prescriptions: { prescriptionId: number; rxNumber: string; receivedAt: string; status: string; items: number }[] }
export type Prescriber = { prescriberId: number; licenceBody: string; licenceNumber: string; firstName: string; lastName: string; phone?: string; fax?: string; clinic?: string; isActive: boolean }
export type Warning = { kind: string; severity: string; message: string }
export type ItemInput = { drugId: number | null; drugText: string; sig: string; quantity: number; daysSupply?: number | null; refillsAuthorized: number; noSubstitution: boolean }
export type Item = ItemInput & {
  itemId: number; lineNumber: number; drug?: string; din?: string; ingredient?: string; schedule?: string; sigExpanded: string; refillsRemaining: number
  status: string; warnings: Warning[]; warningsReviewedBy?: string; warningsReviewedAt?: string; cancelReason?: string
}
export type Rx = {
  prescriptionId: number; rxNumber: string; patientId: number; patient: string; dateOfBirth: string; prescriberId: number; prescriber: string; prescriberLicence: string
  writtenDate: string; receivedAt: string; channel: string; planId?: number; plan?: string; status: string; notes?: string; receivedBy: string
  allergies: { what: string; reaction?: string; severity: string }[]; items: Item[]
  /** The clinic's confidentiality label (a consent directive): R restricted, V very restricted. */
  confidentiality: 'R' | 'V' | null
}
export type QueueRow = { prescriptionId: number; rxNumber: string; receivedAt: string; channel: string; status: string; patient: string; patientId: number; prescriber: string; plan: string; items: number; ready: number; warnings: number }

const q = (params: Record<string, string | number | boolean | undefined | null>) => {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') p.set(k, String(v))
  const s = p.toString()
  return s ? `?${s}` : ''
}

export const api = {
  me: () => request<Me>('GET', '/api/me'),
  authSettings: () => request<{ signIn: 'clinichub' | 'demo' }>('GET', '/api/auth/settings'),
  ref: () => request<Ref>('GET', '/api/ref'),
  saveCode: (table: 'forms' | 'routes', code: string, body: { name: string; nameFr?: string | null; isActive: boolean }) => request<Code>('PUT', `/api/ref/${table}/${encodeURIComponent(code)}`, body),
  saveUnit: (code: string, body: { name: string; kind: string; isActive: boolean }) => request<Unit>('PUT', `/api/ref/units/${encodeURIComponent(code)}`, body),
  saveSig: (code: string, body: { expansion: string; expansionFr?: string | null; dosesPerDay?: number | null; isActive: boolean }) => request<SigCode>('PUT', `/api/ref/sig-codes/${encodeURIComponent(code)}`, body),
  ingredients: (search?: string) => request<Ingredient[]>('GET', `/api/ref/ingredients${q({ q: search })}`),
  saveIngredient: (id: number | null, body: { name: string; atcCode?: string | null; allergyClass?: string | null; isActive: boolean }) =>
    request<Ingredient>(id ? 'PUT' : 'POST', id ? `/api/ref/ingredients/${id}` : '/api/ref/ingredients', body),
  interactions: (ingredientId?: number) => request<InteractionRow[]>('GET', `/api/ref/interactions${q({ ingredientId })}`),
  saveInteraction: (body: { ingredientAId: number; ingredientBId: number; severity: string; description: string }) => request<void>('PUT', '/api/ref/interactions', body),
  deleteInteraction: (a: number, b: number) => request<void>('DELETE', `/api/ref/interactions/${a}/${b}`),
  saveManufacturer: (id: number | null, body: { name: string; country?: string | null; isActive: boolean }) =>
    request<Manufacturer>(id ? 'PUT' : 'POST', id ? `/api/ref/manufacturers/${id}` : '/api/ref/manufacturers', body),
  saveSupplier: (id: number | null, body: Omit<Supplier, 'supplierId'>) => request<Supplier>(id ? 'PUT' : 'POST', id ? `/api/ref/suppliers/${id}` : '/api/ref/suppliers', body),
  savePlan: (id: number | null, body: Omit<Plan, 'planId'>) => request<Plan>(id ? 'PUT' : 'POST', id ? `/api/ref/plans/${id}` : '/api/ref/plans', body),
  drugs: (search?: string, all?: boolean) => request<Drug[]>('GET', `/api/drugs${q({ q: search, all })}`),
  saveDrug: (id: number | null, body: Omit<Drug, 'drugId' | 'ingredient' | 'schedule' | 'monitored' | 'manufacturer' | 'isDemo' | 'form' | 'strengthUnitName'>) =>
    request<Drug>(id ? 'PUT' : 'POST', id ? `/api/drugs/${id}` : '/api/drugs', body),
  patients: (search: string) => request<PatientRow[]>('GET', `/api/patients${q({ q: search })}`),
  patient: (id: number) => request<PatientProfile>('GET', `/api/patients/${id}`),
  savePatient: (id: number | null, body: PatientFields) => request<{ patientId: number }>(id ? 'PUT' : 'POST', id ? `/api/patients/${id}` : '/api/patients', body),
  addAllergy: (patientId: number, body: { ingredientId?: number | null; allergyClass?: string | null; description?: string | null; reaction?: string | null; severity?: string }) =>
    request<void>('POST', `/api/patients/${patientId}/allergies`, body),
  removeAllergy: (id: number) => request<void>('DELETE', `/api/allergies/${id}`),
  prescribers: (search?: string) => request<Prescriber[]>('GET', `/api/prescribers${q({ q: search })}`),
  savePrescriber: (id: number | null, body: Omit<Prescriber, 'prescriberId'>) => request<Prescriber>(id ? 'PUT' : 'POST', id ? `/api/prescribers/${id}` : '/api/prescribers', body),
  queue: (status?: string) => request<QueueRow[]>('GET', `/api/prescriptions${q({ status })}`),
  prescription: (id: number) => request<Rx>('GET', `/api/prescriptions/${id}`),
  check: (body: { patientId: number; writtenDate: string; planId?: number | null; items: ItemInput[] }) =>
    request<{ sigExpanded: string; daysSupply?: number; warnings: Warning[] }[]>('POST', '/api/prescriptions/check', body),
  receive: (body: { patientId: number; prescriberId: number; writtenDate: string; channel: string; planId?: number | null; notes?: string | null; items: ItemInput[] }) =>
    request<Rx>('POST', '/api/prescriptions', body),
  match: (itemId: number, drugId: number) => request<Rx>('POST', `/api/prescriptions/items/${itemId}/match`, { drugId }),
  review: (itemId: number) => request<Rx>('POST', `/api/prescriptions/items/${itemId}/review`),
  hold: (itemId: number, reason?: string) => request<Rx>('POST', `/api/prescriptions/items/${itemId}/hold`, { reason: reason ?? null }),
  cancel: (itemId: number, reason: string) => request<Rx>('POST', `/api/prescriptions/items/${itemId}/cancel`, { reason }),
  dispense: (itemId: number, paymentMethod?: string, quantity?: number) =>
    request<{ dispenseId: number; price: Price; monitored: boolean; refillsRemaining: number }>('POST', `/api/prescriptions/items/${itemId}/dispense`, { paymentMethod: paymentMethod ?? null, quantity: quantity ?? null }),
  stock: (drugId?: number) => request<{ lotId: number; drugId: number; drug: string; din: string; lotNumber: string; expiryDate: string; quantityOnHand: number; unitCost: number; expired: boolean; expiresSoon: boolean }[]>('GET', `/api/stock${q({ drugId })}`),
  purchases: () => request<{ purchaseId: number; supplier: string; invoiceNumber: string; invoiceDate: string; receivedAt: string; drugsAmount: number; suppliesAmount: number; suppliesHst: number; total: number; lots: number }[]>('GET', '/api/purchases'),
  receivePurchase: (body: { supplierId: number; invoiceNumber: string; invoiceDate: string; suppliesAmount: number; suppliesHst: number; lines: { drugId: number; lotNumber: string; expiryDate: string; quantity: number; unitCost: number }[] }) =>
    request<{ purchaseId: number }>('POST', '/api/purchases', body),
}
export type Price = { drugCost: number; markup: number; dispensingFee: number; total: number; planPays: number; patientPays: number }

export const today = () => new Date().toLocaleDateString('en-CA', { timeZone: 'America/Toronto' })
export const dateTime = (iso: string) => new Date(iso.endsWith('Z') ? iso : iso + 'Z').toLocaleString('en-CA', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'America/Toronto' })
export const money = (n: number) => n.toLocaleString('en-CA', { style: 'currency', currency: 'CAD' })
export const drugLabel = (d: Drug) => `${d.brandName}${d.strength ? ` ${d.strength} ${d.strengthUnitName ?? d.strengthUnit}` : ''} · ${d.form}`
