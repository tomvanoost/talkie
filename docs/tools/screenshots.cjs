// Captures the screenshots used in the README and the PDF manual.
// Usage: node docs/tools/screenshots.cjs <web-url> <out-dir> [avatar-dir] [dashboard-login-url] [mailpit-url]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const [BASE, OUT, AVATARS, DASHBOARD, MAILPIT] = process.argv.slice(2);
const PASSWORD = { 'owner@talkie.com': 'fwkh837h@m1', 'admin@talkie.com': 'TalkieAdmin1!' };
const DEMO = process.env.DEMO_PASSWORD || 'Demo1234!';
fs.mkdirSync(OUT, { recursive: true });

async function api(method, url, token, body) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  let payload = body;
  if (body && !(body instanceof FormData)) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body); }
  const res = await fetch(BASE + url, { method, headers, body: payload });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${url} -> ${res.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
const token = async email => (await api('POST', '/api/auth/login', null, { email, password: PASSWORD[email] || DEMO })).token;

const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const tokens = {};
  for (const e of ['owner@talkie.com', 'admin@talkie.com', 'emma@talkie.com', 'liam@talkie.com', 'sophie@talkie.com']) tokens[e] = await token(e);

  // Make the script re-runnable: remove the member it adds below.
  for (const m of await api('GET', '/api/company/members', tokens['owner@talkie.com'])) {
    if (m.email === 'olivia@talkie.com') await api('DELETE', `/api/company/members/${m.id}`, tokens['owner@talkie.com']);
  }

  // The shop shots open an unopened box.
  const shop = await api('GET', '/api/shop', tokens['owner@talkie.com']);
  if (shop.wallet.unopenedBoxes.length === 0) await api('POST', '/api/shop/boxes/common/buy', tokens['owner@talkie.com']);

  if (AVATARS) {
    for (const [email, file] of [['owner@talkie.com', 'owner.png'], ['emma@talkie.com', 'emma.png'], ['sophie@talkie.com', 'sophie.png']]) {
      const p = path.join(AVATARS, file);
      if (!fs.existsSync(p)) continue;
      const form = new FormData();
      form.append('file', new Blob([fs.readFileSync(p)], { type: 'image/png' }), file);
      await api('POST', '/api/account/avatar', tokens[email], form);
    }
  }

  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1440, height: 880 }, deviceScaleFactor: 1 });
  const page = await context.newPage();
  page.on('pageerror', e => console.error('pageerror', e.message));

  const shot = async (name, opts = {}) => {
    if (!opts.keepMouse) await page.mouse.move(1, 879);
    await sleep(opts.wait ?? 600);
    await page.screenshot({ path: path.join(OUT, name + '.png'), ...opts.clip ? { clip: opts.clip } : {} });
    console.log('  📸', name);
  };
  const as = async (email, route) => {
    await page.goto(BASE + '/login');
    await page.waitForSelector('.auth-card, .mud-appbar');
    await page.evaluate(t => localStorage.setItem('talkie.token', t), tokens[email]);
    await page.goto(BASE + route);
    await page.waitForSelector('.mud-appbar');
    await sleep(900);
  };
  const button = name => page.getByRole('button', { name, exact: true });
  const closeDialogs = async () => {
    await page.keyboard.press('Escape');
    await sleep(300);
    const close = page.locator('.mud-dialog button.mud-button-close');
    if (await close.count()) await close.first().click();
    await sleep(300);
  };

  // ---------- Login & create company ----------
  await page.goto(BASE + '/login');
  await page.waitForSelector('.auth-card');
  await page.getByLabel('E-mail').fill('owner@talkie.com');
  await page.getByLabel('Password').fill('fwkh837h@m1');
  await shot('01-login');

  await page.getByText('Create Company', { exact: true }).click();
  await page.getByLabel('Company name').fill('Northwind Studio');
  await page.getByLabel('Owner e-mail').fill('hello@northwind.studio');
  await page.getByLabel('Owner password').fill('Northwind2026');
  await page.getByLabel('Username').fill('nina');
  await page.getByLabel('Display name').fill('Nina Northwind');
  await page.getByRole('button', { name: 'Add role' }).click();
  await page.waitForSelector('.mud-dialog');
  await page.getByLabel('Role name').fill('Designers');
  await page.locator('.color-swatch').nth(11).click();
  await shot('03-role-dialog');
  await button('Save').click();
  await sleep(300);
  for (const [name, i] of [['Developers', 2], ['Management', 7]]) {
    await page.getByRole('button', { name: 'Add role' }).click();
    await page.getByLabel('Role name').fill(name);
    await page.locator('.color-swatch').nth(i).click();
    await button('Save').click();
    await sleep(300);
  }
  await shot('02-create-company');
  await page.getByLabel('Company name').fill('Talkie');
  await shot('02b-reserved-name', { clip: { x: 420, y: 150, width: 600, height: 330 } });

  // ---------- Chat (owner) ----------
  await as('owner@talkie.com', '/');
  await page.waitForSelector('.message');
  await shot('04-chat-channel');
  const liamMsg = page.locator('.message', { hasText: 'Who is up for lunch' }).first();
  await liamMsg.hover();
  await shot('05-message-actions', { keepMouse: true,  clip: { x: 520, y: 150, width: 920, height: 420 } });
  await liamMsg.locator('button[aria-label="React"]').click().catch(() => {});
  await sleep(400);
  await shot('05b-emoji-picker', { keepMouse: true,  clip: { x: 520, y: 150, width: 920, height: 420 } });
  await closeDialogs();

  await page.locator('.chat-item', { hasText: 'Emma Jansen' }).click();
  await page.waitForSelector('.attachment-image');
  await sleep(800);
  await shot('06-direct-message');

  await page.locator('button[aria-label="New conversation"]').click();
  await sleep(400);
  await shot('07-new-conversation-menu', { clip: { x: 230, y: 64, width: 520, height: 300 } });
  await page.getByText('New message', { exact: true }).click();
  await page.waitForSelector('.mud-dialog');
  await page.locator('.mud-dialog input').first().fill('sop');
  await sleep(900);
  await shot('08-new-direct-message');
  await closeDialogs();
  await page.locator('button[aria-label="New conversation"]').click();
  await page.getByText('New group', { exact: true }).click();
  await page.waitForSelector('.mud-dialog');
  await page.getByLabel('Group name').fill('Lunch club');
  for (const e of ['liam@talkie.com', 'sophie@talkie.com', 'noah@talkie.com']) {
    await page.locator('.mud-dialog input').nth(1).fill(e);
    await sleep(500);
    await page.locator('.mud-dialog .mud-dialog-title').click();
    await page.locator('button[aria-label="Add person"]').click();
    await sleep(300);
  }
  await shot('09-new-group');
  await page.locator('.mud-dialog').getByRole('button', { name: 'Cancel' }).click();
  await sleep(400);

  await page.locator('button[aria-label="Create channel"]').click();
  await page.waitForSelector('.mud-dialog');
  await page.getByLabel('Channel name').fill('design-reviews');
  await page.getByLabel('Description').fill('Share mock-ups and get feedback');
  await page.locator('.mud-dialog .role-chip', { hasText: 'Moderator' }).first().click();
  await page.locator('.mud-dialog .role-chip', { hasText: 'Staff' }).first().click();
  const rows = page.locator('.mud-dialog tbody tr');
  await rows.nth(2).locator('input[type=checkbox]').nth(0).check().catch(() => {});
  await rows.nth(2).locator('input[type=checkbox]').nth(1).check().catch(() => {});
  await rows.nth(2).locator('input[type=checkbox]').nth(2).check().catch(() => {});
  await shot('10-create-channel');
  await page.locator('.mud-dialog').getByRole('button', { name: 'Cancel' }).click();
  await sleep(400);

  await page.locator('.channel-item', { hasText: 'general' }).click();
  await sleep(500);
  await page.locator('.chat-header button[aria-label="Search messages"]').click();
  await page.waitForSelector('.mud-dialog');
  await page.getByLabel('What are you looking for?').fill('lunch plans');
  await page.keyboard.press('Enter');
  await sleep(1200);
  await shot('11-semantic-search');
  await closeDialogs();

  await page.locator('.mud-appbar .fx-wrap').last().click();
  await shot('12-user-menu');
  await closeDialogs();

  // ---------- Other pages (owner) ----------
  await as('owner@talkie.com', '/assistant');
  await page.waitForSelector('.bubble');
  await shot('13-ai-assistant');

  await as('owner@talkie.com', '/company');
  await page.waitForSelector('.mud-table-row');
  await shot('14-company-members');
  await page.getByRole('tab', { name: 'Roles' }).click();
  await shot('15-company-roles');
  await page.getByRole('tab', { name: 'Members' }).click();
  await button('Add member').click();
  await page.waitForSelector('.mud-dialog');
  await page.getByLabel('Name', { exact: true }).fill('Olivia');
  await page.getByLabel('Surname').fill('Smit');
  await page.getByLabel('E-mail').fill('olivia@talkie.com');
  await page.locator('.mud-dialog .role-chip', { hasText: 'Member' }).click();
  await shot('16-add-member');
  await page.locator('.mud-dialog').getByRole('button', { name: 'Add member' }).click();
  await page.waitForSelector('text=Temporary password');
  await shot('17-member-added');
  await page.getByRole('button', { name: 'Done' }).click();
  await sleep(500);
  const olivia = page.locator('.mud-table-row', { hasText: 'olivia@talkie.com' });
  await olivia.locator('button[aria-label="Edit"]').click();
  await page.waitForSelector('.mud-dialog');
  await shot('18-edit-member');
  await closeDialogs();
  await olivia.locator('button[aria-label="Delete"]').click();
  await sleep(500);
  await shot('19-delete-member');
  await page.locator('.mud-dialog').getByRole('button', { name: 'Delete', exact: true }).click();
  await sleep(600);

  await as('owner@talkie.com', '/stats');
  await page.waitForSelector('.stat-card');
  await shot('20-stats');

  await as('owner@talkie.com', '/shop');
  await page.waitForSelector('.box-card');
  await shot('21-shop-boxes');
  await page.locator('.box-tap').first().click();
  await page.waitForSelector('.mud-dialog .box-tap');
  for (let i = 0; i < 4; i++) { await page.locator('.mud-dialog .box-tap').click({ force: true }); await sleep(60); }
  await shot('22-box-tapping', { wait: 150 });
  for (let i = 0; i < 6; i++) { await page.locator('.mud-dialog .box-tap').click({ force: true }).catch(() => {}); await sleep(60); }
  await page.waitForSelector('.reward-amount');
  await shot('23-box-reward', { wait: 700 });
  await page.getByRole('button', { name: 'Awesome!' }).click();
  await sleep(400);
  await page.getByRole('tab', { name: 'Exchange' }).click();
  await shot('24-shop-exchange');
  await page.getByRole('tab', { name: 'Avatar effects' }).click();
  await shot('25-shop-effects', { wait: 1100 });
  await page.getByRole('tab', { name: 'Boosters' }).click();
  await shot('26-shop-boosters');

  await as('owner@talkie.com', '/afk');
  await button('Go AFK').click();
  await page.waitForSelector('.mud-dialog');
  await shot('27-afk-confirm');
  await button('Yes').click();
  await page.waitForSelector('.afk-active');
  await shot('28-afk-active', { wait: 3200 });
  await button('Stop AFK').click();
  await sleep(500);

  await as('owner@talkie.com', '/account');
  await page.waitForSelector('.soft-card');
  await shot('29-account');

  // ---------- Members & admin ----------
  await as('liam@talkie.com', '/reports');
  await page.waitForSelector('.mud-table');
  await shot('30-reports-member');
  await button('Report a user').click();
  await page.waitForSelector('.mud-dialog');
  await page.locator('.mud-dialog input').first().fill('noah@talkie.com');
  await sleep(500);
  await page.locator('.mud-dialog .mud-dialog-title').click();
  await page.getByLabel('Reason').fill('Posts spoilers for the new season in #general.');
  await shot('31-report-dialog');
  await page.locator('.mud-dialog').getByRole('button', { name: 'Cancel' }).click();

  await as('liam@talkie.com', '/');
  await page.locator('.channel-item', { hasText: 'announcements' }).click();
  await sleep(900);
  await shot('32-read-only-channel');

  await as('emma@talkie.com', '/');
  await page.locator('.chat-item', { hasText: 'Design team' }).click();
  await sleep(900);
  await page.locator('.message', { hasText: 'color palette' }).hover();
  await shot('33-group-owner', { keepMouse: true });

  await as('admin@talkie.com', '/reports');
  await page.waitForSelector('.mud-table-row');
  await shot('34-reports-admin');
  await page.locator('button[aria-label="Configure app"]').click();
  await page.waitForSelector('.mud-dialog');
  await sleep(500);
  await shot('35-admin-general');
  await page.getByRole('tab', { name: 'Economy' }).click();
  await shot('36-admin-economy');
  await page.getByRole('tab', { name: 'Companies' }).click();
  await shot('37-admin-companies');
  await closeDialogs();
  await page.locator('.mud-appbar .fx-wrap').last().click();
  await shot('38-admin-topbar', { clip: { x: 880, y: 0, width: 560, height: 260 } });
  await closeDialogs();

  // ---------- Tooling ----------
  if (MAILPIT) {
    await page.goto(MAILPIT);
    await sleep(2000);
    await shot('39-mailpit');
  }
  if (DASHBOARD) {
    await page.goto(DASHBOARD);
    await page.waitForLoadState('networkidle').catch(() => {});
    await sleep(4000);
    await shot('40-aspire-dashboard');
  }

  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
