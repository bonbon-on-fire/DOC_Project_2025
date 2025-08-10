import { test, expect } from '@playwright/test';

test('Debug scroll container', async ({ page }) => {
  await page.goto('http://localhost:5173/');
  await page.waitForLoadState('networkidle');
  
  // Create a conversation
  const chatInput = page.getByRole('textbox', { name: 'Start a new conversation...' });
  await chatInput.fill('Test message for scroll debug');
  const sendButton = page.getByRole('button', { name: 'New Chat' });
  await sendButton.click();
  
  // Wait for conversation
  await page.waitForURL('**/chat', { timeout: 10000 });
  await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
  await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });
  await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, { timeout: 20000 });
  
  // Debug the scroll containers
  const containers = await page.locator('.overflow-y-auto').count();
  console.log('Found scroll containers:', containers);
  
  for (let i = 0; i < containers; i++) {
    const container = page.locator('.overflow-y-auto').nth(i);
    const info = await container.evaluate((el, index) => ({
      index,
      tagName: el.tagName,
      className: el.className,
      scrollHeight: el.scrollHeight,
      clientHeight: el.clientHeight,
      scrollTop: el.scrollTop,
      hasChildren: el.children.length,
      isVisible: el.offsetParent !== null
    }), i);
    
    console.log(`Container ${i}:`, info);
  }
  
  // Check if we can manually scroll the correct container
  const mainContainer = page.locator('.overflow-y-auto').first();
  const beforeScroll = await mainContainer.evaluate(el => el.scrollTop);
  
  await mainContainer.evaluate(el => {
    el.scrollTo({ top: el.scrollHeight, behavior: 'instant' });
  });
  
  await page.waitForTimeout(100);
  const afterScroll = await mainContainer.evaluate(el => el.scrollTop);
  
  console.log('Manual scroll test:', { beforeScroll, afterScroll });
  expect(afterScroll).toBeGreaterThan(beforeScroll);
});
