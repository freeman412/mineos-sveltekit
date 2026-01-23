import { defineConfig } from '@playwright/test';

const useChrome = process.env.PLAYWRIGHT_USE_CHROME === '1';

export default defineConfig({
	testDir: './tests',
	timeout: 30_000,
	expect: {
		timeout: 5_000
	},
	use: {
		baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:3000',
		trace: 'retain-on-failure',
		screenshot: 'only-on-failure',
		video: 'retain-on-failure'
	},
	projects: [
		{
			name: useChrome ? 'chrome' : 'chromium',
			use: {
				browserName: 'chromium',
				...(useChrome ? { channel: 'chrome' } : {})
			}
		}
	]
});
