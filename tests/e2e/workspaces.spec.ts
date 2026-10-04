import { test, expect } from '@playwright/test';

test('guest settings omit directory and browser stores no tokens', async ({ page }) => {
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/csrf')) return route.fulfill({ json: { csrfToken: 'synthetic-csrf-for-local-browser-tests' } });
    if (path.endsWith('/refresh')) return route.fulfill({ json: { accessToken: 'synthetic-memory-only', user: {id:'guest-fixture',email:'guest@example.invalid',mfaEnabled:false} } });
    if (path.endsWith('/workspaces/')) return route.fulfill({ json: [{id:'workspace-fixture',name:'Shared workspace',role:3}] });
    if (path.endsWith('/sessions')) return route.fulfill({ json: [{id:'session-fixture',deviceName:'Mock browser',current:true,lastSeenAt:'2026-10-04T12:00:00Z'}] });
    if (path.endsWith('/step-up')) return route.fulfill({ status:400,json:{code:'mfa_code_rejected'} });
    if (path.endsWith('/members')) throw new Error('Guest must not request the workspace directory');
    return route.fulfill({status:404,json:{code:'missing'}});
  });
  await page.goto('/');
  await expect(page.getByRole('button', {name:'guest@example.invalid',exact:true})).toBeVisible();
  const menu = page.getByRole('button', {name:'Toggle navigation',exact:true});
  if (await menu.isVisible()) await menu.click();
  await page.getByRole('button', {name:'Workspace and sessions',exact:true}).click();
  await expect(page.getByRole('heading', {name:'Signed-in devices'})).toBeVisible();
  await expect(page.getByRole('heading', {name:'Members',exact:true})).not.toBeVisible();
  await page.getByLabel('Authenticator code', {exact:true}).fill('123456');
  await page.getByRole('button', {name:'Verify code'}).click();
  await expect(page.getByRole('alert')).toContainText('could not be loaded or updated');
  expect(await page.evaluate(() => ({local:Object.keys(localStorage),session:Object.keys(sessionStorage)}))).toEqual({local:[],session:[]});
});

test('failed logout keeps the signed-in screen and reports the failure', async ({ page }) => {
  await page.route('**/api/v1/auth/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/csrf')) return route.fulfill({json:{csrfToken:'synthetic-csrf-for-local-browser-tests'}});
    if (path.endsWith('/refresh')) return route.fulfill({json:{accessToken:'synthetic-memory-only',user:{id:'user-fixture',email:'user@example.invalid',mfaEnabled:false}}});
    return route.fulfill({status:503,json:{code:'service_unavailable'}});
  });
  await page.goto('/');
  await page.getByRole('button', {name:'Sign out',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('Your session is still active');
  await expect(page.getByRole('button', {name:'user@example.invalid',exact:true})).toBeVisible();
});

