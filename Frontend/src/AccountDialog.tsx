import { useEffect, useRef, useState, type FormEvent } from 'react';
import { auth, ApiError, type AuthUser } from './api';
import { en as t } from './strings';

export function AccountDialog({ user, close, changed }: { user: AuthUser | null; close: () => void; changed: (user: AuthUser | null) => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [mode, setMode] = useState<'login' | 'bootstrap' | 'mfa'>(user ? 'mfa' : 'login');
  const [email, setEmail] = useState(''); const [password, setPassword] = useState('');
  const [code, setCode] = useState(''); const [setupKey, setSetupKey] = useState('');
  const [workspace, setWorkspace] = useState(''); const [secret, setSecret] = useState('');
  const [recovery, setRecovery] = useState<string[]>([]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  useEffect(() => { const node = dialog.current!; const previous = document.activeElement; node.showModal(); node.querySelector<HTMLInputElement>('input')?.focus(); return () => { node.close(); if (previous instanceof HTMLElement) previous.focus(); }; }, []);
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      if (mode === 'bootstrap') {
        await auth.bootstrap(email, password, setupKey, workspace, Intl.DateTimeFormat().resolvedOptions().timeZone);
        changed(await auth.login(email, password, '')); close();
      } else if (mode === 'login') { changed(await auth.login(email, password, code)); close(); }
      else if (!secret) { setSecret(await auth.beginMfa(password)); setPassword(''); }
      else { setRecovery(await auth.confirmMfa(code)); changed(null); setSecret(''); }
    } catch (failure) { setError(failure instanceof ApiError && failure.code === 'login_rejected' ? t.loginError : t.accountError); }
    finally { setBusy(false); }
  }
  return <dialog ref={dialog} className="account-dialog" aria-labelledby="account-heading" onCancel={close}>
    <div className="dialog-top"><h2 id="account-heading">{recovery.length ? t.recoveryCodes : mode === 'mfa' ? t.mfa : mode === 'bootstrap' ? t.setupInstance : t.signIn}</h2><button type="button" className="icon-button" aria-label={t.close} onClick={close}>×</button></div>
    {recovery.length > 0 ? <><p>{t.recoveryHelp}</p><div className="recovery-codes">{recovery.map(value => <code key={value}>{value.match(/.{1,8}/g)?.join('-')}</code>)}</div><button className="primary" onClick={close}>{t.done}</button></>
    : mode === 'mfa' && user?.mfaEnabled ? <p>{t.mfaEnabled}</p>
    : <form onSubmit={submit}>
      {mode !== 'mfa' && <label>{t.email}<input autoComplete="username" type="email" required maxLength={254} value={email} onChange={event => setEmail(event.target.value)}/></label>}
      {!secret && <label>{t.password}<input type="password" autoComplete={mode === 'bootstrap' ? 'new-password' : 'current-password'} required minLength={mode === 'bootstrap' ? 12 : 1} maxLength={1024} value={password} onChange={event => setPassword(event.target.value)}/></label>}
      {mode === 'login' && <label>{t.optionalCode}<input autoComplete="one-time-code" maxLength={48} value={code} onChange={event => setCode(event.target.value)}/></label>}
      {mode === 'bootstrap' && <><label>{t.workspaceName}<input required maxLength={120} value={workspace} onChange={event => setWorkspace(event.target.value)}/></label><label>{t.setupKey}<input type="password" required autoComplete="off" maxLength={128} value={setupKey} onChange={event => setSetupKey(event.target.value)}/></label><p className="form-help">{t.setupHelp}</p></>}
      {mode === 'mfa' && <><p>{secret ? t.mfaSecretHelp : t.mfaPasswordHelp}</p>{secret && <><code className="mfa-secret">{secret}</code><label>{t.authenticatorCode}<input autoComplete="one-time-code" inputMode="numeric" required pattern="[0-9]{6}" maxLength={6} value={code} onChange={event => setCode(event.target.value)}/></label></>}</>}
      {error && <p role="alert" className="form-error">{error}</p>}
      <button className="primary" disabled={busy} type="submit">{busy ? t.working : mode === 'bootstrap' ? t.createAccount : mode === 'mfa' ? secret ? t.enableMfa : t.continue : t.signIn}</button>
      {mode !== 'mfa' && <button className="text-button" type="button" onClick={() => {setMode(mode === 'login' ? 'bootstrap' : 'login'); setError('');}}>{mode === 'login' ? t.setupInstance : t.backToSignIn}</button>}
    </form>}
  </dialog>;
}


