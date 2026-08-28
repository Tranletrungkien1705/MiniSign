const base = '/api/v1'
async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin',
    ...opts, body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text(); const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}
export const api = {
  dashboard: () => req('/dashboard'),
  certs: () => req('/certs'),
  createCert: (b) => req('/certs', { method: 'POST', body: b }),
  revoke: (id) => req(`/certs/${id}/revoke`, { method: 'POST' }),
  sign: (b) => req('/sign', { method: 'POST', body: b }),
  verify: (b) => req('/verify', { method: 'POST', body: b }),
  signlogs: (certId) => req(`/signlogs${certId ? `?certId=${certId}` : ''}`)
}
export const fmtDate = (s) => s ? new Date(s).toLocaleDateString('vi-VN') : '—'
export const fmtDateTime = (s) => s ? new Date(s).toLocaleString('vi-VN') : '—'
