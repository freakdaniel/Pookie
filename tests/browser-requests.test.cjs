const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const script = fs.readFileSync(require('node:path').join(__dirname, '../src/Pookie.App/Browser/Scripts/browser-requests.js'), 'utf8');
const tick = () => new Promise(resolve => setImmediate(resolve));
const command = (n, operation = 'like', liked = true) => ({id:n.toString(16).padStart(32,'0'), operation, user_id:42, track_id:90, liked});
function harness(fetch, origin = 'https://soundcloud.com', frame = false, loaded = true, stored = true, prepared = true, extraAccount = {}, resources = []) {
  const messages = [], events = new Map(), calls = [], timers = [];
  const window = {infiniframe:{host:{postData:data=>messages.push(data.data)}}, addEventListener:(key,handler)=>events.set(key,handler),
    ddSbh:true, dataDomeOptions:{}, localStorage:{getItem:()=> stored ? 'own-browser-session' : null,
      setItem:()=>{throw Error('Website storage must not be written by Pookie');}},
    performance:{getEntriesByType:()=>resources},
    fetch:async (...args)=>{calls.push(args); return fetch(...args);}};
  window.top = frame ? {} : window;
  const document={cookie:'',readyState:loaded?'complete':'loading',documentElement:{lang:'ru'}};
  vm.runInNewContext(script.replace('__POOKIE_ACCOUNT__', JSON.stringify({client_id:'fixtureid', access_token:'secret-token', app_version:'123', app_locale:'en', ...extraAccount})), {window, document, location:{origin}, URL, AbortController, TextDecoder, TextEncoder,
    setTimeout, setInterval:callback=>timers.push(callback), clearInterval:()=>{}});
  if (prepared) events.get('dd_post_done')?.({detail:{}});
  return {window,document,messages,calls,timers,emit:(name,type)=>events.get(name)?.({detail:{challengeType:type}})};
}
const response = (status, data = {}) => ({status,ok:status>=200&&status<300,json:async()=>data,text:async()=>JSON.stringify(data)});

test('early tag readiness cannot release requests before page and browser session are ready', () => {
  const h=harness(()=>response(200),'https://soundcloud.com',false,false,false,false);
  h.emit('dd_ready');assert.equal(h.messages.length,0);
  h.document.readyState='complete';h.emit('load');assert.equal(h.messages.length,0);
  h.document.cookie='datadome=generated';h.timers[0]();
  assert.equal(h.messages.length,0);
  h.emit('dd_post_done');
  assert.equal(h.messages.at(-1).kind,'ready');
});

test('saved .NET protection token never recreates cookies or local storage, and cannot release requests', async () => {
  const h=harness(()=>response(200),'https://soundcloud.com',false,true,false,false,{data_dome_client_id:'stale-rejected-token'});
  assert.equal(h.document.cookie,'');
  h.window.__pookieRequest(command(1));h.emit('dd_ready');await tick();
  assert.equal(h.calls.length,0);
  h.document.cookie='datadome=fresh-browser-session';h.emit('dd_post_done');await tick();
  assert.equal(h.calls.length,1);assert.equal(h.messages.at(-1).status,200);
  assert.equal(h.document.cookie,'datadome=fresh-browser-session');
});

test('late injection recognizes only a completed successful protection payload, not cached session or tag script', () => {
  for (const resource of [
    {name:'https://dwt.soundcloud.com/js/',initiatorType:'xmlhttprequest',responseEnd:1,responseStatus:200},
    {name:'https://dwt.soundcloud.com/tags.js',initiatorType:'script',responseEnd:1,responseStatus:200},
    {name:'https://evil.test/js/',initiatorType:'fetch',responseEnd:1,responseStatus:200},
    {name:'https://dwt.soundcloud.com/js/',initiatorType:'fetch',responseEnd:1,responseStatus:403},
    {name:'https://dwt.soundcloud.com/js/',initiatorType:'xmlhttprequest',responseEnd:0,responseStatus:200}
  ]) {
    const h=harness(()=>response(200),'https://soundcloud.com',false,true,true,false,{},[resource]);
    assert.equal(h.messages.some(m=>m.kind==='ready'),resource.name.endsWith('/js/') && resource.name.includes('dwt.soundcloud.com') && resource.responseEnd>0 && resource.responseStatus===200);
  }
});

test('hard block fails the write promptly, ignores a later pass, and prevents further requests', async () => {
  const h=harness(async()=>{h.emit('dd_blocked','hard_block');return response(403);});
  h.window.__pookieRequest(command(1));await tick();
  assert.equal(h.calls.length,1);
  assert.equal(h.messages.filter(m=>m.kind==='blocked').length,1);
  assert.equal(h.messages.at(-1).status,403);
  h.emit('dd_response_displayed','hard_block');h.emit('dd_response_error');h.emit('dd_response_passed');
  h.window.__pookieRequest(command(2));await tick();
  assert.equal(h.calls.length,1);assert.equal(h.messages.at(-1).status,403);
  assert.equal(h.messages.some(m=>m.kind==='passed' || m.kind==='challenge-error'),false);
});

test('hard block before readiness rejects queued commands without an API request', async () => {
  const h=harness(()=>response(200),'https://soundcloud.com',false,true,true,false);
  h.window.__pookieRequest(command(1));h.emit('dd_response_displayed','hard_block');await tick();
  assert.equal(h.calls.length,0);assert.equal(h.messages.at(-1).status,403);
});

test('browser transport uses live fetch and fixed endpoints, serializes commands, returns minimal profile', async () => {
  const h = harness(async()=>response(200,{id:42,username:'person',avatar_url:'avatar',private_secret:'hidden'}));
  const fetch = h.window.fetch;
  h.window.__pookieRequest(command(1)); h.window.__pookieRequest(command(2,'like',false)); h.window.__pookieRequest(command(3,'me'));
  await tick();
  assert.equal(h.window.fetch,fetch);
  assert.deepEqual(h.calls.map(c=>c[1].method),['PUT','DELETE','GET']);
  assert.equal(new URL(h.calls[0][0]).pathname,'/users/42/track_likes/90');
  assert.equal(new URL(h.calls[0][0]).searchParams.get('app_locale'),'ru');
  assert.equal(h.calls[0][1].headers.Authorization,'OAuth secret-token');
  assert.equal(h.calls[0][1].redirect,'error');
  assert.equal('body' in h.calls[0][1],false);
  assert.equal(h.messages.at(-1).user.private_secret,undefined);
  assert.equal(JSON.stringify(h.messages).includes('secret-token'),false);
});

test('403 device check pauses action, then retries exactly once after pass', async () => {
  let count=0;
  const h=harness(async()=>{if(++count===1){h.emit('dd_blocked','device_check'); return response(403);} return response(200);});
  h.window.__pookieRequest(command(1)); await tick();
  assert.equal(count,1); assert.equal(h.messages.at(-1).kind,'checking'); assert.equal(h.messages.at(-1).interactive,false);
  h.emit('dd_response_passed'); await tick();
  assert.equal(count,2); assert.equal(h.messages.at(-1).status,200);
});

test('fetch aborted by an interactive challenge waits; pending commands preserve order', async () => {
  let count=0;
  const h=harness(async()=>{if(++count===1){h.emit('dd_blocked','block'); throw Error('secret-url');} return response(204);});
  h.window.__pookieRequest(command(1)); h.window.__pookieRequest(command(2,'like',false)); await tick();
  assert.equal(count,1); assert.equal(h.messages.at(-1).interactive,true);
  h.emit('dd_response_passed'); await tick();
  assert.deepEqual(h.calls.map(c=>c[1].method),['PUT','PUT','DELETE']);
  assert.equal(JSON.stringify(h.messages).includes('secret-url'),false);
});

test('random check between requests gates the next action, and failed display fails it without sending', async () => {
  const h=harness(async()=>response(200));
  h.emit('dd_response_displayed','device_check_invisible_mode'); h.window.__pookieRequest(command(1)); await tick();
  assert.equal(h.calls.length,0); h.emit('dd_response_error'); await tick();
  assert.equal(h.calls.length,0); assert.equal(h.messages.at(-1).status,0);
  h.window.__pookieRequest(command(2)); await tick(); assert.equal(h.calls.length,1);
});

test('closing a challenge rejects its waiting action instead of leaving it stuck', async () => {
  const h=harness(async()=>response(204));
  h.emit('dd_response_displayed','block'); h.window.__pookieRequest(command(1)); await tick();
  h.emit('dd_response_unload'); await tick();
  assert.equal(h.calls.length,0); assert.equal(h.messages.at(-1).status,0);
});

test('ordinary 403, expired OAuth and network failure never blindly replay a write', async () => {
  for (const status of [403,401,500,0]) {
    const h=harness(async()=>{if(status===0) throw Error('private-header'); return response(status);});
    h.window.__pookieRequest(command(1)); await tick();
    assert.equal(h.calls.length,1); assert.equal(h.messages.at(-1).status,status);
  }
});

test('a second challenge cannot create an endless retry', async () => {
  let count=0;
  const h=harness(async()=>{count++;h.emit('dd_blocked','device_check');h.emit('dd_response_passed');return response(403);});
  h.window.__pookieRequest(command(1)); await tick();
  assert.equal(count,2);assert.equal(h.messages.at(-1).status,403);
});

test('foreign origins, subframes, malformed commands cannot issue requests', async () => {
  assert.equal(harness(()=>{},'https://evil.test').window.__pookieRequest,undefined);
  assert.equal(harness(()=>{},'https://soundcloud.com',true).window.__pookieRequest,undefined);
  const h=harness(()=>response(200));
  for(const c of [{...command(1),operation:'execute'}, {...command(1),track_id:1e30}, {...command(1),user_id:-1},
    {...command(1),liked:'true'}, {...command(1),id:'bad'}]) h.window.__pookieRequest(c);
  await tick();assert.equal(h.calls.length,0);
});

test('liked ID pagination runs in the same browser and waits for checks on later pages', async () => {
  let later=0;
  const h=harness(async url=>{
    if(!new URL(url).searchParams.has('cursor')) return response(200,{collection:[42,'90','invalid'],next_href:'https://api-v2.soundcloud.com/me/track_likes/ids?cursor=2&client_id=wrong'});
    if(++later===1){h.emit('dd_blocked','device_check');return response(403);}
    return response(200,{collection:[91],next_href:null});
  });
  h.window.__pookieRequest(command(1,'liked-ids'));await tick();assert.equal(later,1);
  h.emit('dd_response_passed');await tick();
  assert.deepEqual(Array.from(h.messages.filter(m=>m.kind==='ids').flatMap(m=>Array.from(m.ids))),[42,90,91]);
  assert.equal(h.messages.at(-1).status,200);
  assert.equal(new URL(h.calls.at(-1)[0]).searchParams.get('client_id'),'fixtureid');
});

test('large ID lists use bounded IPC batches without dropping entries', async () => {
  const values=Array.from({length:750},(_,i)=>i+1);
  const h=harness(async()=>response(200,values));
  h.window.__pookieRequest(command(1,'liked-ids'));await tick();
  const batches=h.messages.filter(m=>m.kind==='ids');
  assert.deepEqual(batches.map(m=>m.ids.length),[200,200,200,150]);
  assert.equal(batches.flatMap(m=>Array.from(m.ids)).length,750);
});

test('ID pagination cannot send authorization to another host, endpoint or credentials URL', async () => {
  for(const next of ['https://evil.test/ids','https://api-v2.soundcloud.com/users/999/track_likes/1',
    'https://user@api-v2.soundcloud.com/me/track_likes/ids']) {
    const h=harness(async()=>response(200,{collection:[42],next_href:next}));
    h.window.__pookieRequest(command(1,'liked-ids'));await tick();
    assert.equal(h.calls.length,1);assert.equal(h.messages.at(-1).status,0);
  }
});


test('rotated protection session is reported once, including idle rotation, without exposing OAuth', () => {
  const h=harness(()=>response(200));
  h.window.localStorage.getItem=()=> 'rotated-fixture';
  h.emit('dd_post_done');
  const protection=h.messages.filter(m=>m.kind==='protection-session');
  assert.equal(protection.at(-1).data_dome_client_id,'rotated-fixture');
  h.timers[1]();
  assert.equal(h.messages.filter(m=>m.kind==='protection-session').length,protection.length);
  h.window.localStorage.getItem=()=> 'new-idle-fixture';h.timers[1]();
  assert.equal(h.messages.at(-1).data_dome_client_id,'new-idle-fixture');
  assert.equal(JSON.stringify(h.messages).includes('secret-token'),false);
});

test('invalid protection session does not leave the WebView', () => {
  const h=harness(()=>response(200));
  const count=h.messages.length;
  h.window.localStorage.getItem=()=> 'invalid; Cookie=other';h.timers[1]();
  assert.equal(h.messages.length,count);
});

const apiRead = (n, path = '/search/tracks?q=музыка') => ({ ...command(n, 'api-get'), url: 'https://api-v2.soundcloud.com' + path });
test('search categories use only the five read-only endpoints', async () => {
  const h=harness(()=>response(200,{collection:[]}));
  const paths=['/search','/search/tracks','/search/users','/search/albums','/search/playlists_without_albums'];
  for (const [index,path] of paths.entries()) h.window.__pookieRequest(apiRead(index+1,path+'?q=кис'));
  await tick();
  assert.deepEqual(h.calls.map(call=>new URL(call[0]).pathname),paths);
  for (const [index,path] of ['/search/delete','/search/users/42','/search/tracks/privacy'].entries())
    h.window.__pookieRequest(apiRead(index+10,path));
  await tick();assert.equal(h.calls.length,5);
});
test('generic browser reads transfer large Unicode JSON through ordered bounded chunks', async () => {
  const body = {collection:Array.from({length:30}, (_,i)=>({id:i+1,title:'Привет 🎵 '.repeat(120)})),next_href:null};
  const h=harness(()=>response(200,body));
  h.window.__pookieRequest(apiRead(1)); await tick();
  const chunks=h.messages.filter(m=>m.kind==='json-chunk');
  assert.ok(chunks.length>10);
  assert.deepEqual(chunks.map(m=>m.chunk_index),chunks.map((_,i)=>i));
  assert.ok(chunks.every(m=>m.chunk.length<=1024 && !/[\uD800-\uDBFF]$/.test(m.chunk)));
  assert.deepEqual(JSON.parse(chunks.map(m=>m.chunk).join('')),body);
  assert.equal(h.messages.at(-1).status,200);
  assert.equal(h.calls[0][1].method,'GET');
});

test('generic reads refuse foreign URLs, credentials, fragments and write-only API paths', async () => {
  const h=harness(()=>response(200));
  for (const url of ['http://api-v2.soundcloud.com/me','https://evil.test/me',
    'https://user@api-v2.soundcloud.com/me','https://api-v2.soundcloud.com:8443/me',
    'https://api-v2.soundcloud.com/users/42/track_likes/90','https://api-v2.soundcloud.com/me#secret'])
    h.window.__pookieRequest({...apiRead(1),url});
  await tick();assert.equal(h.calls.length,0);
  h.window.__pookieRequest(apiRead(2,'/media/soundcloud:tracks:42/test/stream/hls?track_authorization=fixture'));
  await tick();assert.equal(h.calls.length,1);
});

test('cancelled fetch does not retry, close the browser, or delay the next read', async () => {
  const h=harness((url, options)=>new URL(url).pathname==='/stream' ? new Promise((_, reject)=>
    options.signal.addEventListener('abort',()=>reject(new Error('aborted')))) : response(200,{id:42}));
  h.window.__pookieRequest(apiRead(1,'/stream'));await tick();
  h.window.__pookieRequest(command(1,'cancel'));h.window.__pookieRequest(apiRead(2,'/tracks/42'));await tick();
  assert.equal(h.calls.length,2);assert.equal(h.calls[0][1].signal.aborted,true);
  assert.equal(h.messages.find(m=>m.kind==='complete'&&m.request_id===command(1).id).status,0);
  assert.equal(h.messages.at(-1).status,200);
});

test('cancelled read waiting for a check never replays after a pass', async () => {
  const h=harness(()=>response(200,{id:42}));
  h.emit('dd_response_displayed','device_check');
  h.window.__pookieRequest(apiRead(1));await tick();
  h.window.__pookieRequest(command(1,'cancel'));await tick();
  h.window.__pookieRequest(apiRead(2,'/tracks/42'));await tick();
  assert.equal(h.calls.length,0);
  h.emit('dd_response_passed');await tick();
  assert.equal(h.calls.length,1);assert.equal(new URL(h.calls[0][0]).pathname,'/tracks/42');
});

test('streamed oversized responses are cancelled without forwarding partial JSON', async () => {
  let bodyCancelled=false;
  const h=harness(()=>({status:200,ok:true,body:{getReader:()=>({
    read:async()=>({done:false,value:new Uint8Array(1024*1024)}),
    cancel:async()=>{bodyCancelled=true;},releaseLock:()=>{}})}}));
  h.window.__pookieRequest(apiRead(1));await tick();
  assert.equal(bodyCancelled,true);assert.equal(h.messages.at(-1).status,0);
  assert.equal(h.messages.some(m=>m.kind==='json-chunk'),false);
});

test('failed API reads never transfer the private error body or repeat 429', async () => {
  for (const status of [401,403,404,429]) {
    const h=harness(()=>response(status,{secret:'private-rejection'}));
    h.window.__pookieRequest(apiRead(1));await tick();
    assert.equal(h.calls.length,1);assert.equal(h.messages.at(-1).status,status);
    assert.equal(JSON.stringify(h.messages).includes('private-rejection'),false);
  }
});

test('library reads use the same browser transport and preserve the read-only allowlist', async () => {
  const h = harness(async () => response(200, {collection:[]}));
  const paths = ['/me/library/all', '/me/library/stations', '/me/play-history/contexts', '/me/play-history/tracks',
    '/users/42/followings', '/users/42/tracks', '/playlists/42', '/tracks?ids=1,2',
    '/system-playlists/soundcloud%3Asystem-playlists%3Aartist-stations%3A1%3A2'];
  for (let i=0;i<paths.length;i++) { h.window.__pookieRequest(apiRead(100+i, paths[i])); await tick(); }
  assert.equal(h.calls.length, paths.length);
  for (const path of ['/me/play-history', '/me/followings/42', '/playlists/42/privacy']) {
    h.window.__pookieRequest(apiRead(200, path)); await tick();
  }
  assert.equal(h.calls.length, paths.length);
});
