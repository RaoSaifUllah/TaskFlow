import { useEffect, useState, type FormEvent } from 'react';
import { api, auth, ApiError } from './api';
import { en as t } from './strings';

type Workspace = { id: string; name: string; role: number; version: number };
type Member = { userId: string; email: string; role: number; active: boolean };
type Session = { id: string; deviceName: string; createdAt: string; lastSeenAt: string; current: boolean };
const roles = [t.owner, t.admin, t.member, t.guest];
export function WorkspaceSettings({ signedOut }: { signedOut: () => void }) {
  const [workspaces, setWorkspaces] = useState<Workspace[]>([]);
  const [sessions, setSessions] = useState<Session[]>([]);
  const [selected, setSelected] = useState(''); const [members, setMembers] = useState<Member[]>([]);
  const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false);
  const [error, setError] = useState(''); const [notice, setNotice] = useState('');
  const [name, setName] = useState(''); const [code, setCode] = useState('');
  const [renameName,setRenameName]=useState('');
  const [inviteEmail, setInviteEmail] = useState(''); const [inviteRole, setInviteRole] = useState(2);
  const [inviteToken, setInviteToken] = useState(''); const [acceptToken, setAcceptToken] = useState('');
  useEffect(() => {
    let active = true;
    Promise.all([api<Workspace[]>('workspaces/'), api<Session[]>('auth/sessions')]).then(([spaces, devices]) => {
      if (active) { setWorkspaces(spaces); setSessions(devices); setSelected(spaces[0]?.id ?? ''); }
    }).catch(() => { if (active) setError(t.settingsError); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);
  useEffect(() => {
    let active = true; setMembers([]); setError(''); setInviteToken('');
    const workspace = workspaces.find(value => value.id === selected);
    setRenameName(workspace?.name ?? '');
    if (selected && workspace?.role !== 3) {
      api<Member[]>(`workspaces/${selected}/members`).then(value => { if (active) setMembers(value); })
        .catch(failure => { if (active) setError(message(failure)); });
    }
    return () => { active = false; };
  }, [selected, workspaces]);
  useEffect(() => { if (selected && !workspaces.some(value => value.id === selected)) setSelected(workspaces[0]?.id ?? ''); }, [selected,workspaces]);
  function message(failure: unknown) {
    if (failure instanceof ApiError && (failure.code === 'mfa_required' || failure.code === 'mfa_step_up_required')) return t.stepUpHelp;
    if (failure instanceof ApiError && failure.code === 'last_owner') return t.lastOwnerError;
    if (failure instanceof ApiError && failure.code === 'version_conflict') return t.workspaceConflict;
    return t.settingsError;
  }
  async function change(action: () => Promise<void>) {
    setBusy(true); setError(''); setNotice('');
    try { await action(); } catch (failure) { setMembers([]); setError(message(failure)); } finally { setBusy(false); }
  }
  async function create(event: FormEvent) {
    event.preventDefault(); await change(async () => {
      const result = await api<{ id: string }>('workspaces/', { method: 'POST', body: JSON.stringify({ name }) });
      setWorkspaces(await api<Workspace[]>('workspaces/')); setSelected(result.id); setName(''); setNotice(t.workspaceCreated);
    });
  }
  async function stepUp(event: FormEvent) {
    event.preventDefault(); await change(async () => {
      await api('auth/step-up', { method: 'POST', body: JSON.stringify({ code }) }); setCode('');
      setWorkspaces(await api<Workspace[]>('workspaces/')); setNotice(t.stepUpDone);
    });
  }
  const selectedWorkspace = workspaces.find(value => value.id === selected);
  return <section className="settings" aria-labelledby="settings-heading">
    <h1 id="settings-heading">{t.settings}</h1>
    {loading ? <p role="status">{t.loading}</p> : <>
      {error && <p role="alert" className="form-error">{error}</p>}{notice && <p role="status">{notice}</p>}
      <section className="settings-card"><h2>{t.workspaces}</h2>
        {workspaces.length === 0 ? <p>{t.noWorkspaces}</p> : <label>{t.currentWorkspace}<select value={selected} onChange={event => setSelected(event.target.value)}>{workspaces.map(value => <option key={value.id} value={value.id}>{value.name} · {roles[value.role]}</option>)}</select></label>}
        <form onSubmit={create}><label>{t.workspaceName}<input required maxLength={120} value={name} onChange={event => setName(event.target.value)}/></label><button className="primary" disabled={busy}>{t.createWorkspace}</button></form>
      </section>
      {selectedWorkspace && selectedWorkspace.role<=1 && <section className="settings-card"><h2>{t.workspaceSettings}</h2><form onSubmit={event=> {event.preventDefault();void change(async ()=>{await api(`workspaces/${selected}`,{method:'PATCH',body:JSON.stringify({name:renameName,version:selectedWorkspace.version})});setWorkspaces(await api<Workspace[]>('workspaces/'));});}}><label>{t.workspaceName}<input required maxLength={120} value={renameName} onChange={event=>setRenameName(event.target.value)}/></label><button className="primary" disabled={busy}>{t.renameWorkspace}</button></form>{selectedWorkspace.role===0 && <button className="text-button" disabled={busy} onClick={()=>{if(window.confirm(t.deleteWorkspaceConfirm)) void change(async ()=>{await api(`workspaces/${selected}?version=${selectedWorkspace.version}`,{method:'DELETE'});setWorkspaces(await api<Workspace[]>('workspaces/'));});}}>{t.deleteWorkspace}</button>}</section>}
      <section className="settings-card"><h2>{t.stepUp}</h2><p>{t.stepUpHelp}</p><form onSubmit={stepUp}><label>{t.authenticatorCode}<input required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={event => setCode(event.target.value)}/></label><button className="primary" disabled={busy}>{t.verify}</button></form></section>
      <section className="settings-card"><h2>{t.verifyEmail}</h2><button className="text-button" disabled={busy} onClick={()=>void change(async ()=>{await api('auth/verification/request',{method:'POST'});setNotice(t.verificationRequested);})}>{t.requestVerification}</button></section>
      <section className="settings-card"><h2>{t.acceptInvitation}</h2><form onSubmit={event => { event.preventDefault(); void change(async () => { await api('invites/accept',{method:'POST',body:JSON.stringify({token:acceptToken})}); setAcceptToken(''); setWorkspaces(await api<Workspace[]>('workspaces/')); setNotice(t.invitationAccepted); }); }}><label>{t.invitationToken}<input type="password" required maxLength={128} autoComplete="off" value={acceptToken} onChange={event=>setAcceptToken(event.target.value)}/></label><button className="primary" disabled={busy}>{t.acceptInvitation}</button></form></section>
      {selectedWorkspace && selectedWorkspace.role <= 1 && <section className="settings-card"><h2>{t.inviteMember}</h2><form onSubmit={event => {event.preventDefault(); void change(async () => {const grant=await api<{token:string}>(`workspaces/${selected}/invites`,{method:'POST',body:JSON.stringify({email:inviteEmail,role:inviteRole})}); setInviteToken(grant.token); setInviteEmail('');});}}><label>{t.invitationEmail}<input required type="email" maxLength={254} value={inviteEmail} onChange={event=>setInviteEmail(event.target.value)}/></label><label>{t.role}<select value={inviteRole} onChange={event=>setInviteRole(Number(event.target.value))}>{roles.map((role,index)=><option key={role} value={index} disabled={index===0 && selectedWorkspace.role!==0}>{role}</option>)}</select></label><button className="primary" disabled={busy}>{t.inviteMember}</button></form>{inviteToken && <><p>{t.invitationHelp}</p><code className="mfa-secret">{inviteToken}</code></>}</section>}
      {selectedWorkspace?.role !== 3 && selected && <section className="settings-card"><h2>{t.members}</h2>
        {members.length === 0 ? <p>{t.membersUnavailable}</p> : <ul className="settings-list">{members.map(value => <li key={value.userId}><span className="member-id">{value.email}</span><span>{value.active ? t.active : t.revoked}</span>
          {selectedWorkspace && selectedWorkspace.role <= 1 ? <><label>{t.role}<select aria-label={`${t.role} ${value.email}`} value={value.role} disabled={busy} onChange={event => { const role = Number(event.target.value); void change(async () => { await api(`workspaces/${selected}/members/${value.userId}`, {method:'PATCH',body:JSON.stringify({role,active:value.active})}); setWorkspaces(await api<Workspace[]>('workspaces/')); }); }}>{roles.map((role, index) => <option key={role} value={index}>{role}</option>)}</select></label><button className="text-button" disabled={busy} onClick={() => void change(async () => { await api(`workspaces/${selected}/members/${value.userId}`, {method:'PATCH',body:JSON.stringify({role:value.role,active:!value.active})}); setWorkspaces(await api<Workspace[]>('workspaces/')); })}>{value.active ? t.revokeMembership : t.restoreMembership}</button></> : <span>{roles[value.role]}</span>}
        </li>)}</ul>}
      </section>}
      <section className="settings-card"><h2>{t.sessions}</h2>{sessions.length === 0 ? <p>{t.noSessions}</p> : <ul className="settings-list">{sessions.map(value => <li key={value.id}><div><strong>{value.deviceName}</strong>{value.current && <span> · {t.thisDevice}</span>}<p>{t.lastUsed}: {new Date(value.lastSeenAt).toLocaleString()}</p></div><button disabled={busy} className="text-button" onClick={() => void change(async () => { await api(`auth/sessions/${value.id}`, {method:'DELETE'}); if (value.current) {auth.forget(); signedOut();} else setSessions(await api<Session[]>('auth/sessions')); })}>{t.revokeSession}</button></li>)}</ul>}</section>
    </>}
  </section>;
}


