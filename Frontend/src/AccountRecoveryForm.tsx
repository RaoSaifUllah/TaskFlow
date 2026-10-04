import { useState, type FormEvent } from 'react';
import { auth } from './api';
import { en as t } from './strings';

export function AccountRecoveryForm({ mode, back, reset }: { mode: 'request' | 'reset' | 'verify'; back: () => void; reset: () => void }) {
  const [email, setEmail] = useState(''); const [token, setToken] = useState(''); const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [notice, setNotice] = useState('');
  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError(''); setNotice('');
    try {
      if (mode === 'request') { await auth.requestReset(email); setNotice(t.resetRequested); }
      else if (mode === 'reset') { await auth.reset(token, password); reset(); setToken(''); setPassword(''); setNotice(t.resetDone); }
      else { await auth.verify(token); setToken(''); setNotice(t.verificationDone); }
    } catch { setError(t.challengeError); } finally { setBusy(false); }
  }
  return <form onSubmit={submit}>
    {mode === 'request' ? <label>{t.email}<input type="email" required maxLength={254} autoComplete="email" value={email} onChange={event=>setEmail(event.target.value)}/></label>
      : <label>{t.challengeToken}<input type="password" required maxLength={128} autoComplete="off" value={token} onChange={event=>setToken(event.target.value)}/></label>}
    {mode === 'reset' && <label>{t.newPassword}<input type="password" required minLength={12} maxLength={1024} autoComplete="new-password" value={password} onChange={event=>setPassword(event.target.value)}/></label>}
    {error && <p role="alert" className="form-error">{error}</p>}{notice && <p role="status">{notice}</p>}
    <button className="primary" disabled={busy}>{busy ? t.working : mode === 'request' ? t.requestReset : mode === 'reset' ? t.resetPassword : t.verifyEmail}</button>
    <button className="text-button" type="button" onClick={back}>{t.backToSignIn}</button>
  </form>;
}
