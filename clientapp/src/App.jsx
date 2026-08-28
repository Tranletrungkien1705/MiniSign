import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Outlet } from 'react-router-dom'
import { api, fmtDate, fmtDateTime } from './api'

function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }

function Layout() {
  return (
    <>
      <nav className="nav"><span className="brand">✍️ MiniSign</span>
        <NavLink to="/" end>Tổng quan</NavLink><NavLink to="/certs">Chứng thư số</NavLink>
        <NavLink to="/sign">Ký tài liệu</NavLink><NavLink to="/verify">Xác thực</NavLink></nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

function Dashboard() {
  const [d, setD] = useState(null); const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  return (
    <>
      <h1>Tổng quan ký số {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis">
        <div className="kpi"><div className="v">{d.certs}</div><div className="l">Chứng thư</div></div>
        <div className="kpi"><div className="v" style={{ color: 'var(--success)' }}>{d.active}</div><div className="l">Còn hiệu lực</div></div>
        <div className="kpi"><div className="v">{d.signs}</div><div className="l">Lượt ký</div></div>
      </div>
    </>
  )
}

function Certs() {
  const [rows, setRows] = useState([]); const [subject, setSubject] = useState(''); const [years, setYears] = useState(3); const [msg, setMsg] = useState(null)
  const load = () => api.certs().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  const create = async () => { try { if (!subject) return; await api.createCert({ subject, years: Number(years) }); setSubject(''); setMsg({ ok: true, text: 'Đã tạo chứng thư (RSA keypair).' }); load() } catch (e) { setMsg({ ok: false, text: e.message }) } }
  const revoke = async (id) => { try { const r = await api.revoke(id); setMsg({ ok: true, text: r.data.msg }); load() } catch (e) { setMsg({ ok: false, text: e.message }) } }
  return (
    <>
      <h1>Chứng thư số</h1>
      <Flash msg={msg} />
      <div className="card"><div className="row">
        <Field label="Chủ thể (CN) — VD: Công ty ABC"><input value={subject} onChange={e => setSubject(e.target.value)} /></Field>
        <Field label="Hiệu lực (năm)"><input type="number" value={years} onChange={e => setYears(e.target.value)} /></Field>
        <div style={{ flex: 'none', alignSelf: 'flex-end' }}><button className="btn" onClick={create}>+ Cấp chứng thư</button></div></div></div>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Chủ thể</th><th>Serial</th><th>Thuật toán</th><th>Hiệu lực</th><th>Trạng thái</th><th></th></tr></thead>
          <tbody>{rows.map(c => (<tr key={c.id}><td>{c.subject}</td><td style={{ fontFamily: 'monospace' }}>{c.serial}</td><td>{c.algorithm}</td>
            <td>{fmtDate(c.notBefore)}–{fmtDate(c.notAfter)}</td><td><Badge text={c.statusText} css={c.statusCss} /></td>
            <td className="right">{c.status === 0 && <button className="btn gray sm" style={{ flex: 'none' }} onClick={() => revoke(c.id)}>Thu hồi</button>}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={6} className="muted" style={{ padding: 20 }}>Chưa có chứng thư.</td></tr>}</tbody></table>
      </div>
    </>
  )
}

function Sign() {
  const [certs, setCerts] = useState([]); const [f, setF] = useState({ certId: '', docName: 'hopdong.pdf', content: '' }); const [res, setRes] = useState(null); const [err, setErr] = useState(null)
  useEffect(() => { api.certs().then(r => { const usable = r.data.filter(c => c.usable); setCerts(usable); if (usable[0]) setF(s => ({ ...s, certId: usable[0].id })) }) }, [])
  const doSign = async () => { try { const r = await api.sign({ certId: Number(f.certId), docName: f.docName, content: f.content }); setRes(r.data); setErr(null) } catch (e) { setErr(e.message); setRes(null) } }
  return (
    <>
      <h1>Ký tài liệu</h1>
      <div className="card">
        <div className="row"><Field label="Chứng thư ký"><select value={f.certId} onChange={e => setF({ ...f, certId: e.target.value })}>{certs.map(c => <option key={c.id} value={c.id}>{c.subject}</option>)}</select></Field>
          <Field label="Tên tài liệu"><input value={f.docName} onChange={e => setF({ ...f, docName: e.target.value })} /></Field></div>
        <Field label="Nội dung tài liệu"><textarea rows={5} value={f.content} onChange={e => setF({ ...f, content: e.target.value })} placeholder="Dán nội dung cần ký…" /></Field>
        <div style={{ marginTop: 12 }}><button className="btn" onClick={doSign} disabled={!f.certId}>Ký số (SHA256withRSA)</button></div>
      </div>
      {err && <Flash msg={{ ok: false, text: err }} />}
      {res && (
        <div className="card" style={{ borderLeft: '5px solid var(--success)' }}>
          <h2>✅ Đã ký</h2>
          <dl className="dl"><dt>Serial CTS</dt><dd style={{ fontFamily: 'monospace' }}>{res.serial}</dd><dt>Thuật toán</dt><dd>{res.algo}</dd>
            <dt>SHA-256</dt><dd style={{ fontFamily: 'monospace', wordBreak: 'break-all', fontSize: 12 }}>{res.hash}</dd>
            <dt>Chữ ký (base64)</dt><dd style={{ fontFamily: 'monospace', wordBreak: 'break-all', fontSize: 11 }}>{res.signature}</dd></dl>
          <p className="muted">Lưu serial + chữ ký + nội dung để xác thực ở tab "Xác thực".</p>
        </div>
      )}
    </>
  )
}

function Verify() {
  const [f, setF] = useState({ serial: '', content: '', signature: '' }); const [res, setRes] = useState(null)
  const doVerify = async () => { const r = await api.verify(f); setRes(r.data) }
  return (
    <>
      <h1>Xác thực chữ ký</h1>
      <div className="card">
        <Field label="Serial chứng thư"><input value={f.serial} onChange={e => setF({ ...f, serial: e.target.value })} /></Field>
        <Field label="Nội dung tài liệu"><textarea rows={4} value={f.content} onChange={e => setF({ ...f, content: e.target.value })} /></Field>
        <Field label="Chữ ký (base64)"><textarea rows={3} value={f.signature} onChange={e => setF({ ...f, signature: e.target.value })} /></Field>
        <div style={{ marginTop: 12 }}><button className="btn" onClick={doVerify}>Xác thực</button></div>
      </div>
      {res && (
        <div className="card" style={{ borderLeft: `5px solid ${res.valid ? 'var(--success)' : 'var(--danger)'}` }}>
          <h2 style={{ color: res.valid ? 'var(--success)' : 'var(--danger)' }}>{res.valid ? '✅ Chữ ký HỢP LỆ' : '❌ Chữ ký KHÔNG hợp lệ'}</h2>
          <p>{res.msg}</p>
          {res.subject && <dl className="dl"><dt>Chủ thể ký</dt><dd>{res.subject}</dd><dt>Serial</dt><dd style={{ fontFamily: 'monospace' }}>{res.serial}</dd>
            {res.signedAt && <><dt>Thời điểm ký</dt><dd>{fmtDateTime(res.signedAt)}</dd></>}</dl>}
        </div>
      )}
    </>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="certs" element={<Certs />} />
        <Route path="sign" element={<Sign />} />
        <Route path="verify" element={<Verify />} />
      </Route>
    </Routes>
  )
}
