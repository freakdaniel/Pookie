'use strict';

// Runs before Electron.NET starts its authenticated loopback transport or creates windows.
module.exports.onStartup = () => {
  const { app, ipcMain, session, webContents } = require('electron');
  const profile = process.env.POOKIE_ELECTRON_PROFILE;
  const origin = process.env.POOKIE_ELECTRON_ORIGIN;
  const apiOrigin = process.env.POOKIE_ELECTRON_API_ORIGIN;
  if (!profile || !origin || !apiOrigin) return false;
  app.commandLine.appendSwitch('disable-features', 'HardwareMediaKeyHandling,MediaSessionService');
  app.commandLine.appendSwitch('autoplay-policy', 'no-user-gesture-required');
  app.setPath('userData', profile);
  app.setPath('sessionData', profile);
  const sessions = new Map();
  const roots = new Set();
  const messages = new Map();

  const validUrl = value => {
    try {
      const url = new URL(value);
      return !url.username && !url.password && (url.protocol === 'https:' || url.origin === origin);
    } catch { return false; }
  };
  // SoundCloud reserves a blank window synchronously during the click, then
  // asynchronously navigates it to its OAuth provider. Returning deny for the
  // initial blank URL makes window.open return null and triggers "enable popups".
  const validPopupUrl = value => value === '' || value === 'about:blank' || validUrl(value);
  app.on('web-contents-created', (_, contents) => {
    if (contents.getType() !== 'window') return;
    roots.add(contents.id);
    contents.once('destroyed', () => { roots.delete(contents.id); sessions.delete(contents.id); messages.delete(contents.id); });
    contents.on('will-navigate', (event, url) => { if (!validUrl(url)) event.preventDefault(); });
    contents.on('will-redirect', (event, url) => { if (!validUrl(url)) event.preventDefault(); });
    contents.on('will-attach-webview', event => event.preventDefault());
    // Authentication providers may open HTTPS popups. Explicit isolation applies there too.
    contents.setWindowOpenHandler(({ url }) => validPopupUrl(url) ? {
      action: 'allow', overrideBrowserWindowOptions: {
        show: origin === 'https://soundcloud.com',
        webPreferences: { nodeIntegration: false, contextIsolation: true, sandbox: true,
          webSecurity: true, partition: 'persist:soundcloud', preload: undefined }
      }
    } : { action: 'deny' });
  });

  function trusted(event) {
    try {
      return roots.has(event.sender.id) && event.senderFrame === event.sender.mainFrame &&
        new URL(event.senderFrame.url).origin === origin;
    } catch { return false; }
  }
  ipcMain.on('pookie:browser-post', (event, packet) => {
    if (!trusted(event) || typeof packet !== 'string' || packet.length > 10000) return;
    const queue = messages.get(event.sender.id) || [];
    if (queue.length >= 4096) { event.sender.destroy(); return; }
    queue.push(packet);
    messages.set(event.sender.id, queue);
  });
  ipcMain.handle('pookie:browser-drain', event => {
    if (!trusted(event)) return [];
    const queue = messages.get(event.sender.id) || [];
    messages.set(event.sender.id, []);
    return queue;
  });
  ipcMain.handle('pookie:prototype-session', async event => {
    if (!trusted(event)) return {};
    const account = sessions.get(event.sender.id) || {};
    const cookies = await event.sender.session.cookies.get({ url: origin, name: 'oauth_token' });
    const protection = await event.sender.session.cookies.get({ url: origin, name: 'datadome' });
    return { ...account, accessToken: account.accessToken || cookies[0]?.value || '',
      dataDomeClientId: account.dataDomeClientId || protection[0]?.value || '',
      runtime: { electron: process.versions.electron, chromium: process.versions.chrome,
        nodeIntegration: event.sender.getLastWebPreferences().nodeIntegration,
        contextIsolation: event.sender.getLastWebPreferences().contextIsolation,
        sandbox: event.sender.getLastWebPreferences().sandbox } };
  });
  ipcMain.handle('pookie:prototype-flush', async event => {
    if (!trusted(event)) return {};
    event.sender.session.flushStorageData();
    await event.sender.session.cookies.flushStore();
    return { flushed: true };
  });
  let quitting = false;
  app.on('before-quit', event => {
    if (quitting) return;
    event.preventDefault();
    quitting = true;
    const persistent = session.fromPartition('persist:soundcloud');
    persistent.flushStorageData();
    persistent.cookies.flushStore().catch(() => {}).finally(() => app.quit());
  });
  app.once('ready', () => {
    const browserSession = session.fromPartition('persist:soundcloud');
    const rules = require('./blacklist.json');
    browserSession.webRequest.onBeforeRequest({ urls: ['http://*/*', 'https://*/*'] }, (details, callback) => {
      let cancel = false;
      try {
        const url = new URL(details.url);
        const host = url.hostname.toLowerCase().replace(/\.$/, '');
        cancel = rules.some(rule => (host === rule.host || host.endsWith('.' + rule.host)) &&
          (url.pathname + url.search).toLowerCase().startsWith((rule.pathPrefix || '').toLowerCase()));
      } catch {}
      callback({ cancel });
    });
    browserSession.setPermissionRequestHandler((_, __, callback) => callback(false));
    browserSession.setPermissionCheckHandler(() => false);
    browserSession.on('will-download', event => event.preventDefault());
    // Observe only this host's SoundCloud API calls. Leave the actual request untouched.
    browserSession.webRequest.onBeforeSendHeaders({ urls: [apiOrigin + '/*'] }, (details, callback) => {
      try {
        const contents = webContents.fromId(details.webContentsId);
        if (contents && roots.has(contents.id) && new URL(contents.getURL()).origin === origin) {
          const url = new URL(details.url);
          const headers = new Map(Object.entries(details.requestHeaders).map(([key, value]) => [key.toLowerCase(), value]));
          const previous = sessions.get(contents.id) || {};
          const token = /^OAuth ([A-Za-z0-9._-]{1,4096})$/i.exec(headers.get('authorization') || '');
          const id = url.searchParams.get('client_id');
          const protection = headers.get('x-datadome-clientid');
          sessions.set(contents.id, { ...previous,
            ...(/^[A-Za-z0-9]{1,256}$/.test(id || '') ? { clientId: id } : {}),
            ...(token ? { accessToken: token[1] } : {}),
            ...(/^[A-Za-z0-9._~+/%=-]{1,4096}$/.test(protection || '') ? { dataDomeClientId: protection } : {}),
            ...(/^[A-Za-z0-9.!_-]{1,128}$/.test(url.searchParams.get('app_version') || '') ? { appVersion: url.searchParams.get('app_version') } : {}),
            ...(/^[A-Za-z0-9-]{1,35}$/.test(url.searchParams.get('app_locale') || '') ? { appLocale: url.searchParams.get('app_locale') } : {})
          });
        }
      } catch { /* Observation must never interrupt the website. */ }
      callback({ requestHeaders: details.requestHeaders });
    });
  });
  return true;
};
