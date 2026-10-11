'use strict';

const { contextBridge, ipcRenderer } = require('electron');
// No raw IPC, Node, tokens from other origins, filesystem or arbitrary host commands.
if (process.isMainFrame && location.origin === process.env.POOKIE_ELECTRON_ORIGIN) {
  // Observe readiness events from document start; bridge installation can occur later.
  contextBridge.executeInMainWorld({ func: () => {
    addEventListener('dd_ready', () => { window.__pookieTagReady = true; });
    addEventListener('dd_post_done', () => { window.__pookieProtectionReady = true; });
    addEventListener('dd_response_passed', () => { window.__pookieProtectionReady = true; });
  } });
  contextBridge.exposeInMainWorld('pookiePrototype', {
    readSession: () => ipcRenderer.invoke('pookie:prototype-session'),
    flush: () => ipcRenderer.invoke('pookie:prototype-flush'),
    post: packet => ipcRenderer.send('pookie:browser-post', packet),
    drain: () => ipcRenderer.invoke('pookie:browser-drain')
  });
}
