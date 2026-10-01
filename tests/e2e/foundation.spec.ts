import { test, expect } from '@playwright/test';
test('foundation is honest, responsive and supports theme selection', async ({ page, isMobile }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', {name:'A clearer day starts here.'})).toBeVisible();
  await expect(page.getByText('Task capture is coming in the next milestone.', {exact:false})).toBeVisible();
  const before = await page.locator('.app').getAttribute('data-theme');
  await page.getByRole('button', {name:'Toggle color theme'}).click();
  await expect(page.locator('.app')).toHaveAttribute('data-theme', before === 'light' ? 'dark' : 'light');
  if (isMobile) await page.getByRole('button', {name:'Toggle navigation'}).click();
  await page.getByRole('button', {name:'Today',exact:true}).click();
  await expect(page.getByRole('status')).toContainText('This view will be available');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
