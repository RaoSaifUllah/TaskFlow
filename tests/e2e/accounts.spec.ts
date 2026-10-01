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
