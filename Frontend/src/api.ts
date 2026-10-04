export type AuthUser = { id: string; email: string; timeZone: string; instanceAdmin: boolean; mfaEnabled: boolean };
type Grant = { accessToken: string; user: AuthUser; csrfToken: string; expiresAt: string };
let accessToken: string | null = null;
let refreshPromise: Promise<AuthUser | null> | null = null;
export class ApiError extends Error {
  constructor(public status: number, public code: string) { super(code); }
}
async function decode<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => ({})) as { code?: string };
    throw new ApiError(response.status, problem.code ?? 'service_unavailable');
  }
  return response.status === 204 || response.status === 202 ? undefined as T : response.json() as Promise<T>;
}
async function csrf(): Promise<string> {
  return (await decode<{ csrfToken: string }>(await fetch('/api/v1/auth/csrf', { credentials: 'include' }))).csrfToken;
}
async function authPost<T>(path: string, body?: unknown): Promise<T> {
  return decode<T>(await fetch('/api/v1/auth/' + path, { method: 'POST', credentials: 'include',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': await csrf(), ...(accessToken ? { Authorization: 'Bearer ' + accessToken } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body) }));
}
function accept(grant: Grant): AuthUser { accessToken = grant.accessToken; return grant.user; }
export const auth = {
  forget(): void { accessToken=null; },
  async requestReset(email: string): Promise<void> { await authPost('reset/request',{email}); },
  async reset(token: string,password: string): Promise<void> { await authPost('reset/confirm',{token,password}); accessToken=null; },
  async verify(token: string): Promise<void> { await authPost('verification/confirm',{token}); },
  async login(email: string, password: string, code: string): Promise<AuthUser> {
    return accept(await authPost<Grant>('login', { email, password, code: code.replace(/[\s-]/g, '').toUpperCase() || null, device: 'Web browser' }));
  },
  async register(token: string, email: string, password: string, timeZone: string): Promise<void> { await authPost('register', { token, email, password, timeZone }); },
  async bootstrap(email: string, password: string, token: string, workspaceName: string, timeZone: string): Promise<void> {
    await authPost('bootstrap', { email, password, token, workspaceName, timeZone });
  },
  async restore(): Promise<AuthUser | null> {
    if (refreshPromise) return refreshPromise;
    const rotate = async () => {
      try { return accept(await authPost<Grant>('refresh')); }
      catch (error) { accessToken = null; if (error instanceof ApiError && error.status === 401) return null; throw error; }
    };
    refreshPromise = (navigator.locks ? navigator.locks.request('taskflow-refresh', rotate) : rotate()).finally(() => { refreshPromise = null; });
    return refreshPromise;
  },
  async logout(): Promise<void> {
    try { await authPost('logout'); }
    catch (error) { if (!(error instanceof ApiError && error.status === 401)) throw error; if (await auth.restore()) await authPost('logout'); }
    accessToken = null;
  },
  async beginMfa(password: string): Promise<string> { return (await authPost<{ secret: string }>('mfa/begin', { password })).secret; },
  async confirmMfa(code: string): Promise<string[]> {
    const response = await authPost<{ recoveryCodes: string[] }>('mfa/confirm', { code }); accessToken = null; return response.recoveryCodes;
  },
};
export async function api<T>(path: string, options: RequestInit = {}, retry = true): Promise<T> {
  const response = await fetch('/api/v1/' + path, { ...options, credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...options.headers, ...(accessToken ? { Authorization: 'Bearer ' + accessToken } : {}) } });
  if (response.status === 401 && retry && await auth.restore()) return api<T>(path, options, false);
  return decode<T>(response);
}


