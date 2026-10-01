import { StrictMode, useState, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { en as t } from './strings';
import './style.css';
import { auth, type AuthUser } from './api';
import { AccountDialog } from './AccountDialog';

const views = [t.inbox, t.today, t.upcoming, t.overdue, t.assigned, t.completed];
function App() {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [accountError, setAccountError] = useState('');
  const [accountOpen, setAccountOpen] = useState(false);
  useEffect(() => { let active = true; auth.restore().then(value => {if (active) setUser(value);}).catch(() => {}); return () => {active = false;}; }, []);
  const [dark, setDark] = useState(matchMedia('(prefers-color-scheme: dark)').matches);
  const [open, setOpen] = useState(false);
  const [view, setView] = useState<string>(t.inbox);
  return <div className="app" data-theme={dark ? 'dark' : 'light'}>
    <a className="skip" href="#main">{t.skip}</a>
    <aside className={open ? 'sidebar expanded' : 'sidebar'}>
      <div className="brand"><span className="mark" aria-hidden="true">✓</span>{t.brand}</div>
      <p className="workspace">{t.workspace}</p>
      <nav aria-label={t.navigation}>{views.map((name, i) => <button key={name} aria-current={view === name ? 'page' : undefined} onClick={() => {setView(name); setOpen(false);}}><span aria-hidden="true">{['▣', '☀', '◷', '!', '♙', '✓'][i]}</span>{name}</button>)}</nav>
      <p className="sidebar-footer">{t.tagline}</p>
    </aside>
    <div className="content">
      <header><button className="mobile-menu icon-button" aria-label={t.menu} aria-expanded={open} onClick={() => setOpen(!open)}>☰</button><span>{view}</span><div className="header-actions">{user ? <><button className="text-button user-button" onClick={() => setAccountOpen(true)}>{user.email}</button><button className="text-button" onClick={() => {setAccountError(''); auth.logout().then(() => setUser(null)).catch(() => setAccountError(t.logoutError));}}>{t.signOut}</button></> : <button className="text-button" onClick={() => setAccountOpen(true)}>{t.signIn}</button>}<button className="icon-button" aria-label={t.theme} onClick={() => setDark(!dark)}>{dark ? '☀' : '☾'}</button></div></header>
      <main id="main" tabIndex={-1}>
        {accountError && <p role="alert" className="form-error">{accountError}</p>}<span className="badge">{t.status}</span>
        <h1>{t.title}</h1><p className="intro">{t.intro}</p>
        <section className="empty" aria-labelledby="empty-title"><div className="illustration" aria-hidden="true"><span>✓</span><div/><div/></div><h2 id="empty-title">{t.emptyTitle}</h2><p>{t.emptyBody}</p><p role="status">{view !== t.inbox ? t.unavailable : ''}</p></section>
        <div className="cards"><section><span aria-hidden="true">◈</span><h2>{t.privacy}</h2><p>{t.privacyBody}</p></section><section><span aria-hidden="true">✧</span><h2>{t.plan}</h2><p>{t.planBody}</p></section></div>
      </main>
    </div>
    {accountOpen && <AccountDialog user={user} close={() => setAccountOpen(false)} changed={setUser}/>}
  </div>;
}
createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>);

