'use strict';

// Runs only in Pookie's own login window. Reads no form fields or browser profiles.
(() => {
  const allowedOrigin = 'https://soundcloud.com';
  if (location.origin !== allowedOrigin || window !== window.top || window.__pookieLoginCapture) return;
  window.__pookieLoginCapture = true;
  let clientId = '';
  let accessToken = '';
  let dataDomeClientId = '';
  let appVersion = '';
  let appLocale = '';
  let sent = false;

  const validProtectionSession = value => typeof value === 'string' && /^[A-Za-z0-9._~+/%=-]{1,4096}$/.test(value);
  function cookieValue(name) {
    const cookie = document.cookie.split(';').map(value => value.trim()).find(value => value.startsWith(name + '='));
    return cookie ? decodeURIComponent(cookie.slice(name.length + 1)) : '';
  }

  function websiteContext() {
    try {
      // Same source/priority as the site's sessionByHeader tag. Read only its dedicated value.
      const name = window.dataDomeOptions?.ddCookieSessionName || 'ddSession_datadome';
      let stored = '';
      try { stored = window.localStorage?.getItem(name) || ''; } catch { }
      const protection = stored || cookieValue('datadome');
      if (validProtectionSession(protection)) dataDomeClientId = protection;
      if (/^[A-Za-z0-9.!_-]{1,128}$/.test(window.__sc_version || '')) appVersion = window.__sc_version;
      if (!appLocale) appLocale = document.documentElement?.lang || '';
    } catch { }
  }

  function send() {
    if (sent || !clientId || !accessToken) return;
    websiteContext();
    // Don't close the login window before the site's protection tag supplies its session.
    if ((window.ddoptions?.sessionByHeader || window.dataDomeOptions?.sessionByHeader) && !dataDomeClientId) return;
    window.infiniframe.host.postData({ id: 'pookie:web-session', command: 'Post', version: 2, data: {
      client_id: clientId, access_token: accessToken, user_agent: navigator.userAgent,
      ...(dataDomeClientId ? { data_dome_client_id: dataDomeClientId } : {}),
      ...(appVersion ? { app_version: appVersion } : {}),
      ...(/^[A-Za-z0-9-]{1,35}$/.test(appLocale) ? { app_locale: appLocale } : {})
    } });
    sent = true;
    accessToken = '';
  }

  function observe(urlValue, headers) {
    if (sent) return;
    try {
      const url = new URL(urlValue, location.href);
      if (url.origin !== 'https://api-v2.soundcloud.com' || url.username || url.password) return;
      const id = url.searchParams.get('client_id') || '';
      if (/^[A-Za-z0-9]{1,256}$/.test(id)) clientId = id;
      const match = /^OAuth ([A-Za-z0-9._-]{1,4096})$/i.exec(headers.get('authorization') || '');
      if (match) accessToken = match[1];
      const protection = headers.get('x-datadome-clientid');
      if (validProtectionSession(protection)) dataDomeClientId = protection;
      const version = url.searchParams.get('app_version');
      if (/^[A-Za-z0-9.!_-]{1,128}$/.test(version || '')) appVersion = version;
      const locale = url.searchParams.get('app_locale');
      if (/^[A-Za-z0-9-]{1,35}$/.test(locale || '')) appLocale = locale;
      send();
    } catch { /* Observation must not interrupt SoundCloud. */ }
  }

  const originalFetch = window.fetch;
  window.fetch = function(input, init) {
    try {
      const headers = new Headers(input instanceof Request ? input.headers : undefined);
      if (init?.headers) new Headers(init.headers).forEach((value, name) => headers.set(name, value));
      observe(input instanceof Request ? input.url : String(input), headers);
    } catch { }
    return originalFetch.apply(this, arguments);
  };

  const requests = new WeakMap();
  const originalOpen = XMLHttpRequest.prototype.open;
  const originalSetHeader = XMLHttpRequest.prototype.setRequestHeader;
  const originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function(method, url) {
    requests.set(this, { url: String(url), headers: new Headers() });
    return originalOpen.apply(this, arguments);
  };
  XMLHttpRequest.prototype.setRequestHeader = function(name, value) {
    try { requests.get(this)?.headers.append(name, value); } catch { }
    return originalSetHeader.apply(this, arguments);
  };
  XMLHttpRequest.prototype.send = function() {
    const request = requests.get(this);
    if (request) observe(request.url, request.headers);
    return originalSend.apply(this, arguments);
  };

  function probe() {
    if (sent) return;
    try {
      const hydration = window.__sc_hydration;
      const id = Array.isArray(hydration) ? hydration.find(entry => entry.hydratable === 'apiClient')?.data?.id : '';
      if (typeof id === 'string' && /^[A-Za-z0-9]{1,256}$/.test(id)) clientId = id;
      // Optional fallback for the website's JS-readable cookie, only inside this window.
      const token = cookieValue('oauth_token');
      if (/^[A-Za-z0-9._-]{1,4096}$/.test(token)) accessToken = token;
      send();
    } catch { }
  }
  probe();
  const timer = setInterval(() => { probe(); if (sent) clearInterval(timer); }, 300);
})();
