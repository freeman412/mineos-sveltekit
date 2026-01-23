import { test, expect } from '@playwright/test';

test('login screen renders', async ({ page }) => {
	await page.goto('/login');

	await expect(page.getByRole('heading', { name: 'MineOS' })).toBeVisible();
	await page.screenshot({ path: 'test-results/login.png', fullPage: true });
});
