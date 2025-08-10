import { defineConfig } from '@playwright/test';

export default defineConfig({
    webServer: {
        command: 'npm run dev -- --port 5173 --strict-port',
        url: 'http://localhost:5173',
        reuseExistingServer: true,
        timeout: 120_000,
        env: {
            PUBLIC_API_BASE_URL: 'http://localhost:5099'
        }
    },
    use: {
        baseURL: 'http://localhost:5173'
    },
    testDir: 'e2e'
});
