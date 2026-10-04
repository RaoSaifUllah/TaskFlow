import { test, expect } from '@playwright/test';
test('account dialog supports keyboard navigation and first-account setup fields', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', {name:'Sign in',exact:true}).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Email',{exact:true})).toBeFocused();
  await dialog.getByRole('button', {name:'Set up this instance',exact:true}).click();
  await expect(dialog.getByLabel('One-time setup key',{exact:true})).toHaveAttribute('type','password');
  await expect(dialog.getByLabel('Passphrase',{exact:true})).toHaveAttribute('minlength','12');
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('button', {name:'Sign in',exact:true})).toBeFocused();
});

test('invitation registration masks token and enforces passphrase length', async ({ page }) => {
  await page.route('**/api/v1/auth/**', route => route.fulfill({status:401,json:{code:'session_required'}}));
  await page.goto('/');
  await page.getByRole('button', {name:'Sign in',exact:true}).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', {name:'Join with an invitation',exact:true}).click();
  await expect(dialog.getByLabel('Invitation token',exact())).toHaveAttribute('type','password');
  await expect(dialog.getByLabel('Passphrase',exact())).toHaveAttribute('minlength','12');
  await dialog.getByRole('button', {name:'Back to sign in'}).click();
  await expect(dialog.getByLabel('Email',exact())).toBeVisible();
});
function exact() { return {exact:true}; }

test('reset request gives a nonenumerating response and masks confirmation token', async ({ page }) => {
  await page.route('**/api/v1/auth/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith('/csrf')) return route.fulfill({json:{csrfToken:'synthetic-csrf-for-local-browser-tests'}});
    if (path.endsWith('/reset/request')) return route.fulfill({status:202,body:''});
    return route.fulfill({status:401,json:{code:'session_required'}});
  });
  await page.goto('/');
  await page.getByRole('button',{name:'Sign in',exact:true}).click();
  const dialog=page.getByRole('dialog');
  await dialog.getByRole('button',{name:'Request password reset',exact:true}).click();
  await dialog.getByLabel('Email',{exact:true}).fill('synthetic@example.invalid');
  await dialog.getByRole('button',{name:'Request password reset',exact:true}).click();
  await expect(dialog.getByRole('status')).toContainText('If an active account matches');
  await dialog.getByRole('button',{name:'Back to sign in'}).click();
  await dialog.getByRole('button',{name:'I have a reset token'}).click();
  await expect(dialog.getByLabel('Reset or verification token',{exact:true})).toHaveAttribute('type','password');
  await expect(dialog.getByLabel('New passphrase',{exact:true})).toHaveAttribute('minlength','12');
});
