import { chromium } from 'playwright';

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  
  page.on('console', msg => console.log('PAGE LOG:', msg.text()));
  page.on('request', request => {
    if (request.url().includes('api/v1/trips')) {
      console.log('>> REQUEST:', request.method(), request.url());
    }
  });
  
  console.log('Navigating...');
  await page.goto('https://green-rock-098e70e00.7.azurestaticapps.net/trips?skip=0&take=10');
  
  console.log('Waiting for Grid...');
  await page.waitForSelector('.e-grid', { timeout: 15000 });
  
  console.log('Waiting for Search input...');
  await page.waitForSelector('.e-searchinput', { timeout: 10000 });
  
  console.log('Typing Tokyo...');
  await page.fill('.e-searchinput', 'Tokyo');
  await page.press('.e-searchinput', 'Enter');
  
  console.log('Waiting 5s to see what happens...');
  await new Promise(r => setTimeout(r, 5000));
  
  await browser.close();
})();
