import { test, expect } from '@playwright/test';

test('Debug scroll hierarchy', async ({ page }) => {
  await page.goto('http://localhost:5173/');
  await page.waitForLoadState('networkidle');
  
  // Create a conversation with tall content
  const chatInput = page.getByRole('textbox', { name: 'Start a new conversation...' });
  await chatInput.fill(`Tall message for debugging
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
.
End of tall message`);
  const sendButton = page.getByRole('button', { name: 'New Chat' });
  await sendButton.click();
  
  // Wait for conversation
  await page.waitForURL('**/chat', { timeout: 10000 });
  await expect(page.getByTestId('message-list')).toBeVisible({ timeout: 20000 });
  await expect(page.getByTestId('message-content').first()).toBeVisible({ timeout: 20000 });
  await expect(page.getByTestId('message-count')).toHaveText(/\b2 messages\b/, { timeout: 20000 });
  
  // Check the specific containers and their hierarchy
  const container0 = page.locator('.overflow-y-auto').nth(0);
  const container1 = page.locator('.overflow-y-auto').nth(1);
  
  const info0 = await container0.evaluate(el => ({
    className: el.className,
    scrollHeight: el.scrollHeight,
    clientHeight: el.clientHeight,
    innerHTML: el.innerHTML.substring(0, 200) + '...',
    parentClassName: el.parentElement?.className || 'no parent'
  }));
  
  const info1 = await container1.evaluate(el => ({
    className: el.className,
    scrollHeight: el.scrollHeight,
    clientHeight: el.clientHeight,
    innerHTML: el.innerHTML.substring(0, 200) + '...',
    parentClassName: el.parentElement?.className || 'no parent'
  }));
  
  console.log('Container 0 details:', info0);
  console.log('Container 1 details:', info1);
  
  // Test scroll on the taller container (Container 0)
  const beforeScroll0 = await container0.evaluate(el => el.scrollTop);
  await container0.evaluate(el => {
    el.scrollTo({ top: el.scrollHeight, behavior: 'instant' });
  });
  const afterScroll0 = await container0.evaluate(el => el.scrollTop);
  
  // Test scroll on Container 1
  const beforeScroll1 = await container1.evaluate(el => el.scrollTop);
  await container1.evaluate(el => {
    el.scrollTo({ top: el.scrollHeight, behavior: 'instant' });
  });
  const afterScroll1 = await container1.evaluate(el => el.scrollTop);
  
  console.log('Container 0 scroll test:', { beforeScroll0, afterScroll0 });
  console.log('Container 1 scroll test:', { beforeScroll1, afterScroll1 });
});
