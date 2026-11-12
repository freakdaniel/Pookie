const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const querySource = fs.readFileSync(require('node:path').join(__dirname, '../src/Pookie.SoundCloud/Requests/TrackReadRequest.cs'), 'utf8');
let script = fs.readFileSync(require('node:path').join(__dirname, '../src/Pookie.App/Browser/Scripts/browser-requests.js'), 'utf8');
for (const name of ['Comments', 'Replies', 'Sidebar'])
  script = script.replace(`__POOKIE_TRACK_${name.toUpperCase()}__`, JSON.stringify(querySource.match(new RegExp(`const string ${name} = """([\\s\\S]*?)"""`))[1]));
const commentQuerySource = fs.readFileSync(require('node:path').join(__dirname, '../src/Pookie.SoundCloud/Requests/CommentWriteRequest.cs'), 'utf8');
for (const name of ['Create', 'Delete'])
  script = script.replace(`__POOKIE_COMMENT_${name.toUpperCase()}__`, JSON.stringify(commentQuerySource.match(new RegExp(`const string ${name} = """([\\s\\S]*?)"""`))[1]));
const tick = () => new Promise(resolve => setImmediate(resolve));
const command = (n, operation = 'like', liked = true) => ({id:n.toString(16).padStart(32,'0'), operation, user_id:42, track_id:90, liked});
function harness(fetch, origin = 'https://soundcloud.com', frame = false, loaded = true, stored = true, prepared = true, extraAccount = {}, resources = []) {
  const messages = [], events = new Map(), calls = [], timers = [];
  const window = {__pookiePost:data=>messages.push(data.data), addEventListener:(key,handler)=>events.set(key,handler),
    crypto:require('node:crypto').webcrypto,
    ddSbh:true, dataDomeOptions:{}, localStorage:{getItem:()=> stored ? 'own-browser-session' : null,
      setItem:()=>{throw Error('Website storage must not be written by Pookie');}},
    performance:{getEntriesByType:()=>resources},
    fetch:async (...args)=>{calls.push(args); return fetch(...args);}};
  window.top = frame ? {} : window;
  const document={cookie:'',readyState:loaded?'complete':'loading',documentElement:{lang:'ru'}};
  vm.runInNewContext(script.replace('__POOKIE_ACCOUNT__', JSON.stringify({client_id:'fixtureid', access_token:'secret-token', app_version:'123', app_locale:'en', ...extraAccount})), {window, document, location:{origin,href:origin+'/you/likes'}, URL, AbortController, TextDecoder, TextEncoder, FormData, Blob, atob,
    setTimeout, setInterval:callback=>timers.push(callback), clearInterval:()=>{}});
  if (prepared) events.get('dd_post_done')?.({detail:{}});
  return {window,document,messages,calls,timers,emit:(name,type)=>events.get(name)?.({detail:{challengeType:type}})};
}
const response = (status, data = {}) => ({status,ok:status>=200&&status<300,json:async()=>data,text:async()=>JSON.stringify(data)});

test('profile verification prefers the website token for the confirmed account', async () => {
  for (const [savedId, cookieUser, accepted] of [[42,42,true],[42,99,false],[undefined,42,false]]) {
    const h = harness((url, options) => {
      if (new URL(url).pathname !== '/me') return response(401);
      return options.headers.Authorization === 'OAuth refreshed-token' ? response(200, {id:cookieUser,username:'Fixture'}) : response(401);
    }, 'https://soundcloud.com', false, true, true, true, {verified_user_id:savedId});
    h.document.cookie = 'oauth_token=refreshed-token';
    h.window.__pookieRequest(command(1,'me')); await tick();
    const verified = h.messages.at(-1);
    assert.equal(verified.status,accepted ? 200 : 401);
    assert.equal(verified.session?.access_token,accepted ? 'refreshed-token' : undefined);
    assert.equal(Boolean(verified.session_rejected), Boolean(savedId && cookieUser !== savedId));
    assert.equal(h.calls.length,1);
    assert.equal(h.calls[0][1].headers.Authorization,savedId ? 'OAuth refreshed-token' : 'OAuth secret-token');

    h.window.__pookieRequest(command(2)); await tick();
    const writes = h.calls.filter(([,options])=>options.method==='PUT');
    assert.equal(writes.length,1);
    assert.equal(writes[0][1].headers.Authorization,accepted ? 'OAuth refreshed-token' : 'OAuth secret-token');
    assert.equal(h.messages.at(-1).status,401);
  }
});

test('profile verification refreshes the same-origin cookie once after 401', async () => {
  for (const [refreshStatus, replacement, userId, expected] of [
    [204, 'rotated-token', 42, 200], [204, 'rotated-token', 99, 401], [204, '', 42, 401],
    [400, '', 42, 401], [403, '', 42, 403], [429, '', 42, 429], [503, '', 42, 503]
  ]) {
    const h = harness((url, options) => {
      if (url === 'https://soundcloud.com/api/refresh-token') {
        assert.equal(options.method,'POST');
        assert.equal(options.credentials,'include');
        assert.equal(options.redirect,'error');
        assert.equal(options.headers,undefined);
        assert.equal(options.body,undefined);
        if (replacement) h.document.cookie = 'oauth_token=' + replacement;
        if (refreshStatus === 403) {
          h.emit('dd_response_displayed','device_check_invisible_mode');
          h.emit('dd_response_passed','device_check_invisible_mode');
        }
        return response(refreshStatus, { access_token: 'unused-response-token' });
      }
      assert.equal(new URL(url).pathname,'/me');
      return options.headers.Authorization === 'OAuth rotated-token' ? response(200, { id: userId }) : response(401);
    }, 'https://soundcloud.com', false, true, true, true, { verified_user_id: 42 });
    h.document.cookie = 'oauth_token=expired-website-token';
    h.window.__pookieRequest(command(1,'me')); await tick();
    const result = h.messages.at(-1);
    assert.equal(result.status,expected);
    assert.equal(result.session?.access_token,expected === 200 ? replacement : undefined);
    assert.deepEqual(h.calls.map(([,options])=>options.method),replacement ? ['GET','POST','GET'] : ['GET','POST']);
    assert.equal(h.calls[0][1].headers.Authorization,'OAuth expired-website-token');
  }
});

test('expired chat polling joins the website refresh and resumes queued requests under the same account', async () => {
  const h = harness((url, options) => {
    assert.equal(options.method,'GET');
    const path = new URL(url).pathname;
    if (path === '/me' && options.headers.Authorization === 'OAuth secret-token' && !expired)
      return response(200, {id:42});
    if (options.headers.Authorization !== 'OAuth rotated-token') return response(401);
    return response(200, path === '/me' ? {id:42} : {collection:[]});
  }, 'https://soundcloud.com', false, true, true, true, {verified_user_id:42});
  let expired = false, refreshes = 0;
  let finishRefresh;
  const refreshing = new Promise(resolve => { finishRefresh = resolve; });
  const modules = {
    tokenRefreshProxyWebi: function tokenRefreshProxyWebi(module) {
      module.exports = {getTokenExpiryManager:() => ({
        async waitOrRefreshToken() {
          refreshes++;
          await refreshing;
          h.document.cookie = 'oauth_token=rotated-token';
          return true;
        }
      })};
    }
  };
  const require = id => { const module = {exports:{}}; modules[id](module); return module.exports; };
  require.m = modules;
  h.window.webpackJsonp = {push:entry => Object.values(entry[1])[0]({}, {}, require)};
  h.document.cookie = 'oauth_token=secret-token';
  h.window.__pookieRequest(command(1400,'me')); await tick();
  expired = true;
  const poll = id => ({id:command(id).id,operation:'chat',chat:{kind:'list',user_id:42}});
  h.window.__pookieRequest(poll(1401));
  h.window.__pookieRequest(poll(1402));
  await tick();
  assert.equal(refreshes,1);
  assert.equal(h.calls.length,3);
  finishRefresh(); await tick();
  assert.equal(refreshes,1);
  assert.deepEqual(h.calls.slice(1).map(([url,options])=>[new URL(url).pathname,options.headers.Authorization]),[
    ['/users/42/conversations','OAuth secret-token'], ['/me','OAuth secret-token'],
    ['/me','OAuth rotated-token'], ['/users/42/conversations','OAuth rotated-token'], ['/users/42/conversations','OAuth rotated-token']
  ]);
  const completions = h.messages.filter(message=>message.kind==='complete').slice(1);
  assert.deepEqual(completions.map(message=>message.status),[200,200]);
  assert.equal(completions[0].session.access_token,'rotated-token');
  assert.equal(completions[0].session.verified_user_id,42);
  assert.equal(completions[1].session,undefined);
});

test('a chat 401 only rejects the session if the website also rejects account recovery', async () => {
  for (const [profileStatus, refreshStatus, rejected] of [[200,0,false],[401,503,false],[401,401,true]]) {
    let verified = false;
    const h = harness(url => {
      const path = new URL(url).pathname;
      if (path === '/api/refresh-token') return response(refreshStatus);
      if (path === '/me') return response(verified ? profileStatus : 200, {id:42});
      return response(401);
    }, 'https://soundcloud.com', false, true, true, true, {verified_user_id:42});
    h.window.__pookieRequest(command(1403,'me')); await tick();
    verified = true;
    h.window.__pookieRequest({id:command(1404).id,operation:'chat',chat:{kind:'list',user_id:42}});
    await tick();
    assert.equal(h.calls.length,refreshStatus ? 4 : 3);
    assert.equal(h.messages.at(-1).status,401);
    assert.equal(Boolean(h.messages.at(-1).session_rejected),rejected);
  }
});

test('web playlist metadata edits preserve duplicate track IDs and use the numeric web route', async () => {
  let playlist={id:77,user:{id:42},title:'Before',sharing:'private',description:'',track_count:3,tracks:[{id:8},{id:7},{id:8}]};
  const h=harness((url,options)=>{
    assert.equal(new URL(url).pathname,'/playlists/77');
    if(options.method==='PUT'){
      const data=JSON.parse(options.body).playlist;
      assert.equal(data.tracks,undefined); playlist={...playlist,...data}; return response(200,playlist);
    }
    return response(200,playlist);
  });
  h.window.__pookieRequest({id:'01'.repeat(16),operation:'playlist-edit',edit:{kind:'update',user_id:42,playlist_id:77,
    update:{user_id:42,playlist_id:77,title:'Updated 🎵',description:'Two\nlines',sharing:'public',expected_track_ids:[8,7,8]}}});
  await tick(); assert.deepEqual(h.calls.map(([,options])=>options.method),['GET','PUT','GET']);
  assert.deepEqual(playlist.tracks,[{id:8},{id:7},{id:8}]); assert.equal(h.messages.at(-1).status,200);
});

test('web metadata editing never replaces a partial playlist order', async () => {
  let playlist={id:77,user:{id:42},title:'Before',sharing:'private',description:'',track_count:20,tracks:[{id:8},{id:7}]};
  const h=harness((_,options)=>{
    if(options.method==='PUT'){
      const data=JSON.parse(options.body).playlist;assert.equal(data.tracks,undefined);playlist={...playlist,...data};
    }
    return response(200,playlist);
  });
  h.window.__pookieRequest({id:'09'.repeat(16),operation:'playlist-edit',edit:{kind:'update',user_id:42,playlist_id:77,
    update:{user_id:42,playlist_id:77,title:'New',description:'',sharing:'private'}}});
  await tick();assert.equal(playlist.track_count,20);assert.deepEqual(playlist.tracks,[{id:8},{id:7}]);
  assert.equal(h.messages.at(-1).status,200);assert.equal(h.calls.length,3);
});

test('extended playlist metadata has bounded fields and explicit clear/type mapping', async () => {
  const snapshot={id:77,user:{id:42},is_album:true,track_count:20,tracks:[{id:8}]};
  const h=harness((_,options)=>{
    if(options.method==='PUT') {
      const data=JSON.parse(options.body).playlist;
      assert.equal(data.tracks,undefined);assert.equal(data.release_date,null);
      assert.equal(data.playlist_type,'ep');assert.equal(data.set_type,'album');
      assert.equal(data.genre,'Rock');assert.equal(data.tag_list,'rock "indie rock"');
      assert.equal(data.permalink,'updated');assert.equal(data.license,'cc-by');
      assert.equal(data.purchase_url,'https://example.com/buy');assert.equal(data.label_name,'Label');
    }
    return response(200,snapshot);
  });
  const update={user_id:42,playlist_id:77,title:'Title',description:'',sharing:'private',metadata:{
    permalink:'updated',genre:'Rock',tag_list:'rock "indie rock"',release_date:'',playlist_type:'ep',
    purchase_url:'https://example.com/buy',label_name:'Label',license:'cc-by'}};
  h.window.__pookieRequest({id:'21'.repeat(16),operation:'playlist-edit',edit:{kind:'update',user_id:42,playlist_id:77,update}});
  await tick();assert.deepEqual(h.calls.map(([,options])=>options.method),['GET','PUT','GET']);
  const count=h.calls.length;
  for(const metadata of [{release_date:'2026-02-30'},{permalink:'../me'},{purchase_url:'javascript:alert(1)'},{admin:true},{license:'unknown'}]) {
    h.window.__pookieRequest({id:'22'.repeat(16),operation:'playlist-edit',edit:{kind:'update',user_id:42,playlist_id:77,update:{...update,metadata}}});
    await tick();assert.equal(h.calls.length,count);
  }
});

test('web playlist edits reject foreign owners, incomplete removal and changed source order before writing', async () => {
  for(const mode of ['owner','incomplete','stale']){
    const playlist={id:77,user:{id:mode==='owner'?9:42},track_count:mode==='incomplete'?3:2,tracks:[{id:8},{id:7}]};
    const h=harness(()=>response(200,playlist));
    const edit={kind:mode==='stale'?'update':'remove',user_id:42,playlist_id:77,
      ...(mode==='stale'?{update:{user_id:42,playlist_id:77,title:'New',description:'',sharing:'private',track_ids:[7,8],expected_track_ids:[7,8]}}:{track_id:8})};
    h.window.__pookieRequest({id:'02'.repeat(16),operation:'playlist-edit',edit});
    await tick(); assert.equal(h.calls.length,1); assert.equal(h.calls[0][1].method,'GET');
  }
});

test('web playlist removal retains the remaining order and delete is sent once', async () => {
  for(const kind of ['remove','delete']){
    let playlist={id:77,user:{id:42},track_count:3,tracks:[{id:8},{id:7},{id:8}]};
    const h=harness((_,options)=>{
      if(options.method==='PUT'){const ids=JSON.parse(options.body).playlist.tracks;assert.deepEqual(ids,[7]);playlist={...playlist,track_count:ids.length,tracks:ids.map(id=>({id}))};}
      return response(options.method==='DELETE'?204:200,playlist);
    });
    h.window.__pookieRequest({id:'03'.repeat(16),operation:'playlist-edit',edit:{kind,user_id:42,playlist_id:77,...(kind==='remove'?{track_id:8}:{})}});
    await tick(); assert.equal(h.calls.filter(([,options])=>options.method!=='GET').length,1);
    assert.equal(h.messages.at(-1).status,kind==='delete'?204:200);
  }
});

test('web artwork is a JPEG multipart update with a browser-generated content type', async () => {
  const h=harness((_,options)=>{
    if(options.method==='PUT'){
      assert.equal(options.headers['Content-Type'],undefined);
      const image=options.body.get('playlist[artwork_data]'); assert.equal(image.type,'image/jpeg'); assert.equal(image.size,4);
    }
    return response(200,{id:77,user:{id:42},track_count:0,tracks:[],artwork_url:'https://i1.sndcdn.com/new.jpg'});
  });
  h.window.__pookieRequest({id:'04'.repeat(16),operation:'playlist-edit',edit:{kind:'artwork',user_id:42,playlist_id:77,artwork:'/9j/2Q=='}});
  await tick(); assert.equal(h.calls.filter(([,options])=>options.method==='PUT').length,1);assert.equal(h.messages.at(-1).status,200);
});

test('invalid playlist edit envelopes and unconfirmed writes cannot produce another mutation', async () => {
  const h=harness((_,options)=>{if(options.method==='PUT')throw Error('Lost acknowledgement');return response(200,{id:77,user:{id:42},track_count:0,tracks:[]});});
  const edit={kind:'update',user_id:42,playlist_id:77,update:{user_id:42,playlist_id:77,title:'Title',description:'',sharing:'public'}};
  h.window.__pookieRequest({id:'05'.repeat(16),operation:'playlist-edit',edit});
  await tick();assert.equal(h.calls.filter(([,options])=>options.method==='PUT').length,1);
  h.window.__pookieRequest({id:'06'.repeat(16),operation:'playlist-edit',url:'https://api-v2.soundcloud.com/me',edit});
  h.window.__pookieRequest({id:'07'.repeat(16),operation:'playlist-edit',edit:{...edit,update:{...edit.update,title:''}}});
  h.window.__pookieRequest({id:'08'.repeat(16),operation:'playlist-edit',edit:{kind:'artwork',user_id:42,playlist_id:77,artwork:'invalid'}});
  await tick();assert.equal(h.calls.length,2);
});

test('playlist creation verifies owner then writes a seeded playlist once with numeric IDs', async () => {
  const playlist = {user_id:42,track_id:90,title:'Новая 🎵',sharing:'private'};
  const h = harness((url, options) => new URL(url).pathname === '/me' ? response(200,{id:42}) :
    response(201,{id:77,title:playlist.title,sharing:playlist.sharing,user:{id:42},tracks:[{id:90}]}));
  h.window.__pookieRequest({id:'a1'.repeat(16),operation:'playlist-create',playlist});
  await tick();
  assert.equal(h.calls.length,2);
  assert.equal(h.calls[0][1].method,'GET');
  const [url,options] = h.calls[1];
  assert.equal(new URL(url).pathname,'/playlists'); assert.equal(options.method,'POST');
  assert.deepEqual(JSON.parse(options.body),{playlist:{title:playlist.title,sharing:'private',tracks:[90]}});
  assert.equal(h.messages.at(-1).status,201);
  const body=h.messages.filter(m=>m.kind==='json-chunk').map(m=>m.chunk).join('');
  assert.equal(JSON.parse(body).id,77);
});
test('playlist creation rejects wrong owners, mixed commands and invalid title/privacy before mutation', async () => {
  const h=harness(()=>response(200,{id:8}));
  const playlist={user_id:42,track_id:90,title:'New',sharing:'public'};
  h.window.__pookieRequest({id:'b1'.repeat(16),operation:'playlist-create',playlist});
  await tick(); assert.equal(h.calls.length,1); assert.equal(h.calls[0][1].method,'GET');
  for(const [i,change] of [{title:''},{title:'x'.repeat(256)},{title:'line\nbreak'},{sharing:'other'},{user_id:0}].entries())
    h.window.__pookieRequest({id:(3000+i).toString(16).padStart(32,'0'),operation:'playlist-create',playlist:{...playlist,...change}});
  h.window.__pookieRequest({id:'b2'.repeat(16),operation:'playlist-create',playlist,url:'https://api-v2.soundcloud.com/me'});
  h.window.__pookieRequest({id:'b3'.repeat(16),operation:'me',playlist});
  await tick(); assert.equal(h.calls.length,1);
});
test('playlist writes are not replayed after a lost response or ordinary rejection', async () => {
  for(const lost of [false,true]){
    const h=harness(url=>{
      if(new URL(url).pathname==='/me')return response(200,{id:42});
      if(lost)throw Error('Lost response');
      return response(403);
    });
    h.window.__pookieRequest({id:'c1'.repeat(16),operation:'playlist-create',playlist:{user_id:42,track_id:90,title:'New',sharing:'public'}});
    await tick(); assert.equal(h.calls.filter(([,o])=>o.method==='POST').length,1);
  }
});

test('removed Android routes and stream fallbacks never reach the web API', async () => {
  const h = harness(async()=>response(200, {}));
  const paths = ['/configuration/android', '/tracks/90/streams/https', '/tracks/90/streams/hls'];
  for (const [i,path] of paths.entries())
    h.window.__pookieRequest({id:(910+i).toString(16).padStart(32,'0'), operation:'api-get', url:'https://api-v2.soundcloud.com'+path});
  h.window.__pookieRequest({id:'f'.repeat(32), operation:'api-get', url:'https://api-v2.soundcloud.com/tracks/90/streams/hls/snippet'});
  await tick();
  assert.deepEqual(h.calls.map(call=>new URL(call[0]).pathname),[]);
  assert.ok(h.calls.every(call=>call[1].method==='GET'));
});
test('an initially restricted document reports a hard block without waiting for protection readiness or sending an API request', async () => {
  const h = harness(async()=>{throw Error('Blocked document must not send requests');}, 'https://soundcloud.com', false, true, true, false);
  h.document.title='Access temporarily restricted';
  for (const timer of h.timers) timer();
  const blocked=h.messages.find(m=>m.kind==='blocked');
  assert.equal(blocked.interactive,true);assert.equal(blocked.challenge_type,'hard_block');
  h.window.__pookieRequest(command(900,'me'));
  await tick();
  assert.equal(h.calls.length,0);assert.equal(h.messages.at(-1).status,403);
});
function historyResponse(url, options) {
  const path = new URL(url).pathname;
  if (options.method === 'GET' && path === '/me') return response(200, {id:42});
  if (options.method === 'GET' && path === '/tracks/90')
    return response(200, {id:90,user_id:43,duration:180000,track_authorization:'fixture-track-authorization'});
  return response(path === '/me/play-history' ? 204 : 201);
}

test('playback history writes a track and optional real context through the live browser session', async () => {
  const h = harness(historyResponse);
  const play = (id, context) => ({id:id.toString(16).padStart(32,'0'), operation:'play-history', track_id:90, context_urn:context});
  h.window.__pookieRequest(play(850, null));
  h.window.__pookieRequest(play(851, 'soundcloud:playlists:11'));
  h.window.__pookieRequest(play(852, 'soundcloud:system-playlists:your-moods:42:1'));
  await tick();
  assert.equal(h.calls.length, 10);
  const contexts = h.calls.filter(([url,options]) => new URL(url).pathname === '/me/play-history' && options.method === 'POST');
  const plays = h.calls.filter(([url,options]) => new URL(url).pathname === '/me' && options.method === 'POST');
  assert.equal(contexts.length, 3);
  assert.equal(plays.length, 3);
  for (const [url, options] of [...contexts, ...plays]) {
    assert.equal(new URL(url).origin, 'https://api-v2.soundcloud.com');
    assert.equal(options.method, 'POST');
    assert.equal(options.headers.Authorization, 'OAuth secret-token');
    assert.equal(options.headers['apollographql-client-name'], undefined);
  }
  assert.deepEqual(JSON.parse(contexts[0][1].body), {track_urn:'soundcloud:tracks:90'});
  assert.deepEqual(JSON.parse(contexts[1][1].body), {track_urn:'soundcloud:tracks:90', context_urn:'soundcloud:playlists:11'});
  assert.equal(contexts[0][1].credentials, 'include');
  assert.equal(JSON.parse(plays[1][1].body).events[0].payload.in_playlist, 'soundcloud:playlists:11');
  assert.equal(JSON.parse(plays[0][1].body).events[0].payload.in_playlist, undefined);
  assert.equal(JSON.parse(plays[2][1].body).events[0].payload.in_playlist, undefined);
  for (const [,options] of plays) {
    const body = JSON.parse(options.body);
    assert.equal(body.auth_token, 'secret-token');
    assert.equal(body.events.length, 1);
    const event = body.events[0];
    assert.equal(event.event, 'audio'); assert.equal(event.version, 'v1.27.47');
    assert.equal(event.payload.action, 'play'); assert.equal(event.payload.source, 'pookie');
    assert.equal(event.payload.track, 'soundcloud:tracks:90');
    assert.equal(event.payload.track_owner, 'soundcloud:users:43');
    assert.equal(event.payload.user, 'soundcloud:users:42');
    assert.equal(event.payload.client_id, 46941);
    assert.equal(event.payload.track_authorization, 'fixture-track-authorization');
    assert.match(event.payload.session_id, /^[0-9a-f-]{36}$/);
    assert.equal(event.payload.anonymous_id, event.payload.session_id);
  }
  assert.ok(!JSON.stringify(h.messages).includes('secret-token'));
  assert.equal(h.messages.at(-1).status, 201);
  for (const context of ['', 'https://evil.test', 'soundcloud:tracks:90', 'soundcloud:playlists:0',
    'soundcloud:playlists:9007199254740992', 'soundcloud:playlists:11\n', 'soundcloud:system-playlists:bad/uri'])
    h.window.__pookieRequest(play(853, context));
  for (const extra of [{url:'https://api-v2.soundcloud.com/me/play-history'}, {user_id:42}, {audio:{}}, {detail:{}}, {comment:{}}, {track_id:0}])
    h.window.__pookieRequest({...play(854, null), ...extra});
  h.window.__pookieRequest({...command(855, 'me'), context_urn:'soundcloud:playlists:11'});
  await tick();
  assert.equal(h.calls.length, 10);
});

test('REST JSON writes survive the actual API CORS allowlist while GraphQL keeps its client header', async () => {
  const allowed = new Set(['authorization', 'content-type', 'accept']);
  const h = harness((url, options) => {
    if (new URL(url).origin === 'https://api-v2.soundcloud.com')
      for (const header of Object.keys(options.headers))
        if (!allowed.has(header.toLowerCase())) throw new TypeError('Failed to fetch: disallowed CORS header');
    return historyResponse(url, options);
  });
  h.window.__pookieRequest({id:'a'.repeat(32), operation:'play-history', track_id:90});
  h.window.__pookieRequest({id:'b'.repeat(32), operation:'comment-write', comment:{kind:'create', track_id:90, body:'Fixture'}});
  await tick();
  assert.equal(h.calls.length, 5);
  assert.deepEqual(h.messages.filter(m=>m.kind==='complete').map(m=>m.status), [201, 201]);
  const rest = h.calls.filter(([url,options]) => new URL(url).origin === 'https://api-v2.soundcloud.com' && options.method === 'POST');
  for (const [,options] of rest) assert.equal(options.headers['Content-Type'], 'application/json');
  assert.equal(h.calls.find(([url]) => new URL(url).origin === 'https://graph.soundcloud.com')[1].headers['apollographql-client-name'], 'webi');
});

test('history reporting does not treat the legacy 204 as success when the audio event fails', async () => {
  const h = harness((url, options) => new URL(url).pathname === '/me' && options.method === 'POST' ? response(400) : historyResponse(url, options));
  h.window.__pookieRequest({id:'c'.repeat(32),operation:'play-history',track_id:90});
  await tick();
  assert.equal(h.messages.at(-1).status, 400);
  assert.equal(h.calls.filter(([,options]) => options.method === 'POST').length, 2);
});

test('history uses the refreshed account and a browser session without writing website storage', async () => {
  const h = harness(historyResponse);
  h.document.cookie = 'sc_anonymous_id=existing-anonymous-id';
  h.window.__pookieRequest({id:'d'.repeat(32),operation:'play-history',track_id:90});
  await tick();
  h.window.__pookieUpdateSession({client_id:'freshclient',access_token:'fresh-token',app_version:'124'});
  h.window.__pookieRequest({id:'f'.repeat(32),operation:'play-history',track_id:90});
  await tick();
  const plays = h.calls.filter(([url,options]) => new URL(url).pathname === '/me' && options.method === 'POST');
  const first = JSON.parse(plays[0][1].body), last = JSON.parse(plays[1][1].body);
  assert.equal(last.auth_token, 'fresh-token');
  assert.equal(last.events[0].payload.anonymous_id, 'existing-anonymous-id');
  assert.equal(first.events[0].payload.session_id, last.events[0].payload.session_id);
  assert.equal(h.calls.filter(([url,options]) => new URL(url).pathname === '/me' && options.method === 'GET').length, 2);
  assert.ok(!JSON.stringify(h.messages).includes('fresh-token'));
});

test('history reads use browser cookies and bypass the HTTP cache after playback', async () => {
  const h = harness(() => response(200, {collection:[]}));
  h.window.__pookieRequest({id:'e'.repeat(32), operation:'api-get', url:'https://api-v2.soundcloud.com/me/play-history/tracks?limit=30'});
  await tick();
  assert.equal(h.calls[0][1].credentials, 'include');
  assert.equal(h.calls[0][1].cache, 'no-store');
  assert.equal(h.calls[0][1].headers['apollographql-client-name'], undefined);
  assert.equal(h.messages.at(-1).status, 200);
});

test('comment writes use fixed GraphQL mutations, return JSON and keep body out of event metadata', async () => {
  const h=harness(()=>response(200,{data:{createTrackComment:{comment:{urn:'soundcloud:comments:7',body:'Текст 🎧'}}}}));
  const write=(id,comment)=>({id:id.toString(16).padStart(32,'0'),operation:'comment-write',comment});
  h.window.__pookieRequest(write(800,{kind:'create',track_id:90,body:'Текст 🎧',timestamp:12345}));
  h.window.__pookieRequest(write(801,{kind:'delete',track_id:90,comment_id:7}));
  await tick();
  assert.equal(h.calls.length,2);
  const created=JSON.parse(h.calls[0][1].body),deleted=JSON.parse(h.calls[1][1].body);
  assert.match(created.query,/mutation PookieCreateTrackComment/);
  assert.deepEqual(created.variables.input,{trackUrn:'soundcloud:tracks:90',body:'Текст 🎧',timestamp:12345});
  assert.match(deleted.query,/mutation PookieDeleteTrackComment/);
  assert.deepEqual(deleted.variables.input,{commentUrn:'soundcloud:comments:7'});
  for (const [url,options] of h.calls) { assert.equal(new URL(url).origin,'https://graph.soundcloud.com'); assert.equal(options.method,'POST'); }
  assert.ok(h.messages.some(m=>m.kind==='json-chunk'));assert.equal(h.messages.at(-1).status,200);
  for(const comment of [{kind:'create',track_id:90,body:' '},{kind:'create',track_id:90,body:'a'.repeat(1001)},
    {kind:'create',track_id:90,body:'bad\x00'},{kind:'create',track_id:90,body:'text',timestamp:-1},
    {kind:'delete',track_id:90,comment_id:0},{kind:'delete',track_id:90,comment_id:7,body:'text'}]) h.window.__pookieRequest(write(802,comment));
  h.window.__pookieRequest({...write(803,{kind:'create',track_id:90,body:'text'}),url:'https://evil.test'});
  await tick();assert.equal(h.calls.length,2);
});

test('comment sorting is server-side and rejects arbitrary enum values', async () => {
  const h=harness(()=>response(200,{data:{trackComments:{comments:[],pageInfo:{endCursor:null}}}}));
  for(const [index,sort] of ['NEWEST','OLDEST','TIMESTAMP','INVALID'].entries())
    h.window.__pookieRequest({...command(810+index,'track-read'),detail:{kind:'comments',track_id:90,sort,cursor:'next'}});
  await tick();assert.equal(h.calls.length,3);
  assert.deepEqual(h.calls.map(([,o])=>JSON.parse(o.body).variables.options.sort),['NEWEST','OLDEST','TIMESTAMP']);
});

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
  assert.equal(h.messages.filter(m=>m.kind==='pass-ignored'&&m.challenge_type==='hard_block').length,1);
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
  assert.equal(count,1); assert.equal(h.messages.findLast(m=>m.kind==='checking').interactive,false);
  h.emit('dd_response_passed'); await tick();
  assert.equal(count,2); assert.equal(h.messages.at(-1).status,200);
});

test('fetch aborted by an interactive challenge waits; pending commands preserve order', async () => {
  let count=0;
  const h=harness(async()=>{if(++count===1){h.emit('dd_blocked','block'); throw Error('secret-url');} return response(204);});
  h.window.__pookieRequest(command(1)); h.window.__pookieRequest(command(2,'like',false)); await tick();
  assert.equal(count,1); assert.equal(h.messages.findLast(m=>m.kind==='checking').interactive,true);
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
test('track pages can read comments and related tracks without allowing arbitrary track actions', async () => {
  const h=harness(()=>response(200,{collection:[]}));
  const paths=['/tracks/42/comments?limit=30','/tracks/42/related?limit=6'];
  for (const [index,path] of paths.entries()) h.window.__pookieRequest(apiRead(index+1,path));
  await tick(); assert.equal(h.calls.length,2);
  for (const [index,path] of ['/tracks/0/comments','/tracks/42/comments/delete','/tracks/42/reposts'].entries())
    h.window.__pookieRequest(apiRead(index+10,path));
  await tick(); assert.equal(h.calls.length,2);
});
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
  const paths = ['/mixed-selections', '/me/library/all', '/me/library/stations', '/me/play-history/contexts', '/me/play-history/tracks',
    '/users/42/followings', '/users/42/tracks', '/playlists/42', '/tracks?ids=1,2',
    '/system-playlists/soundcloud%3Asystem-playlists%3Aartist-stations%3A1%3A2'];
  for (let i=0;i<paths.length;i++) { h.window.__pookieRequest(apiRead(100+i, paths[i])); await tick(); }
  assert.equal(h.calls.length, paths.length);
  for (const path of ['/me/play-history', '/me/followings/42', '/playlists/42/privacy']) {
    h.window.__pookieRequest(apiRead(200, path)); await tick();
  }
  assert.equal(h.calls.length, paths.length);
});

test('track reads use fixed read-only GraphQL documents and bounded thread options', async () => {
  const h=harness(()=>response(200,{data:{trackComments:{comments:[],pageInfo:{endCursor:null}}}}));
  for (const [index,kind] of ['comments','replies','sidebar'].entries())
    h.window.__pookieRequest({...command(700+index,'track-read'),detail:{kind,track_id:90,comment_id:kind==='replies'?12:0,cursor:kind==='sidebar'?null:'next'}});
  await tick();
  assert.equal(h.calls.length,3);
  for(const [url,options] of h.calls) {
    assert.equal(new URL(url).origin,'https://graph.soundcloud.com');
    assert.equal(new URL(url).pathname,'/graphql');
    assert.equal(options.method,'POST');
    assert.equal(options.headers['Content-Type'],'application/json');
    assert.match(JSON.parse(options.body).query,/query PookieTrack/);
    assert.doesNotMatch(JSON.parse(options.body).query,/mutation/);
    assert.equal(JSON.parse(options.body).variables.trackUrn,'soundcloud:tracks:90');
  }
  assert.equal(JSON.parse(h.calls[0][1].body).variables.options.repliesFirst,16);
  assert.equal(JSON.parse(h.calls[1][1].body).variables.commentUrn,'soundcloud:comments:12');
  assert.equal(JSON.parse(h.calls[1][1].body).variables.options.after,'next');
  assert.equal(h.messages.at(-1).status,200);
});

test('track reads reject arbitrary operations, endpoints, IDs and excessive cursors', async () => {
  const h=harness(()=>response(200));
  const base={...command(800,'track-read'),detail:{kind:'comments',track_id:90,comment_id:0,cursor:null}};
  for(const c of [ {...base,detail:{...base.detail,kind:'mutation'}}, {...base,url:'https://evil.test'},
    {...base,detail:{...base.detail,track_id:0}}, {...base,detail:{...base.detail,cursor:'x'.repeat(2049)}},
    {...base,detail:{...base.detail,cursor:'bad\n'}}, {...base,detail:{...base.detail,kind:'sidebar',cursor:'next'}},
    {...base,detail:{...base.detail,kind:'replies',comment_id:0}} ]) h.window.__pookieRequest(c);
  await tick();assert.equal(h.calls.length,0);
});

test('repost uses fixed PUT/DELETE endpoint and rejects unsafe writes', async () => {
  const h=harness(()=>response(204));
  const repost=(n,value)=>({id:n.toString(16).padStart(32,'0'),operation:'repost',user_id:0,track_id:90,reposted:value});
  h.window.__pookieRequest(repost(901,true)); h.window.__pookieRequest(repost(902,false));
  await tick();
  assert.deepEqual(h.calls.map(c=>c[1].method),['PUT','DELETE']);
  assert.equal(new URL(h.calls[0][0]).pathname,'/me/track_reposts/90');
  assert.equal(h.calls[0][1].headers.Authorization,'OAuth secret-token');
  for (const value of [{...repost(903,true),track_id:0}, {...repost(904,true),track_id:Number.MAX_SAFE_INTEGER+1},
    {...repost(905,true),reposted:'true'}, {...repost(906,true),user_id:42},
    {id:repost(907,true).id,operation:'api-get',url:'https://api-v2.soundcloud.com/me/track_reposts/90'}])
    h.window.__pookieRequest(value);
  await tick(); assert.equal(h.calls.length,2);
});

test('repost ID read shares protected JSON transport', async () => {
  const h=harness(()=>response(200,{collection:[42,'90'],next_href:null}));
  h.window.__pookieRequest({id:command(908).id,operation:'api-get',url:'https://api-v2.soundcloud.com/me/track_reposts/ids?limit=200'});
  await tick(); assert.equal(h.calls[0][1].method,'GET');
  assert.equal(h.messages.at(-1).status,200);
  assert.equal(JSON.parse(h.messages.filter(m=>m.kind==='json-chunk').map(m=>m.chunk).join('')).collection[1],'90');
});

const playlistAdd = n => ({id:command(n).id,operation:'playlist-add',user_id:42,playlist_id:11,track_id:90});
test('playlist add reads fresh contents and appends without losing placeholders or order', async () => {
  const h=harness((url, options)=>response(200, options.method==='GET' ?
    {id:11,user:{id:42},track_count:3,tracks:[{id:8},{id:7},{id:8}]} : {}));
  h.window.__pookieRequest(playlistAdd(1001));await tick();await tick();
  assert.deepEqual(h.calls.map(call=>call[1].method),['GET','PUT']);
  assert.equal(new URL(h.calls[0][0]).searchParams.get('representation'),'full');
  assert.equal(new URL(h.calls[1][0]).pathname,'/playlists/11');
  assert.deepEqual(JSON.parse(h.calls[1][1].body),{playlist:{tracks:[8,7,8,90]}});
  assert.equal(h.messages.at(-1).status,200);
});
test('playlist add is idempotent and refuses other owners, albums and incomplete snapshots', async () => {
  const cases=[
    {id:11,user:{id:42},track_count:1,tracks:[{id:90}]},
    {id:11,user:{id:7},track_count:0,tracks:[]},
    {id:11,user:{id:42},track_count:2,tracks:[{id:8}]},
    {id:11,user:{id:42},is_album:true,track_count:0,tracks:[]},
    {id:11,user:{id:42},track_count:1,tracks:[{id:0}]},
    {id:11,user:{id:42},track_count:500,tracks:Array.from({length:500},(_,i)=>({id:i+100}))}
  ];
  for(const [i,playlist] of cases.entries()) {
    const h=harness(()=>response(200,playlist));h.window.__pookieRequest(playlistAdd(1010+i));await tick();await tick();
    assert.equal(h.calls.length,1);assert.equal(h.messages.at(-1).status,i===0?200:0);
  }
});
test('follow delegates signature to the current website helper and uses POST/DELETE', async () => {
  const h=harness(()=>response(200));let argumentsSeen;
  const modules={
    signer(module){ module.exports={__FOLLOWS_SIGNATURE_VERSION__:'fixture',sign:(...args)=>{argumentsSeen=args;return 'site-signature';}}; },
    secret(module){ module.exports={__FOLLOWS_SIGNATURE_SECRET__:'fixture-site-value'}; }
  };
  const require=id=>{const module={exports:{}};modules[id](module,{},require);return module.exports;}; require.m=modules;
  h.window.webpackJsonp={push:entry=>Object.values(entry[1])[0]({}, {}, require)};
  const follow=n=>({id:command(n).id,operation:'follow',user_id:42,artist_id:7,following:true});
  h.window.__pookieRequest(follow(1101));h.window.__pookieRequest({...follow(1102),following:false});await tick();await tick();
  assert.deepEqual(h.calls.map(call=>call[1].method),['POST','DELETE']);
  assert.equal(new URL(h.calls[0][0]).pathname,'/me/followings/7');
  assert.equal(new URL(h.calls[0][0]).searchParams.get('signature'),'site-signature');
  assert.equal(new URL(h.calls[1][0]).searchParams.has('signature'),false);
  assert.deepEqual(argumentsSeen,[42,7,'fixtureid','fixture-site-value']);
  for(const value of [{...follow(1103),artist_id:42},{...follow(1104),artist_id:0},{...follow(1105),following:'true'},
    {...playlistAdd(1106),playlist_id:0},{...playlistAdd(1107),track_id:Number.MAX_SAFE_INTEGER+1}]) h.window.__pookieRequest(value);
  await tick();assert.equal(h.calls.length,2);
});
test('follow signing returns only a bounded signature without making an API request', async () => {
  const h=harness(()=>{throw Error('Signing must not make an API request');});
  const modules={
    signer(module){module.exports={__FOLLOWS_SIGNATURE_VERSION__:'fixture',sign:()=> 'fixture-signature'};},
    secret(module){module.exports={__FOLLOWS_SIGNATURE_SECRET__:'fixture-secret'};}
  };
  const require=id=>{const module={exports:{}};modules[id](module,{},require);return module.exports;};require.m=modules;
  h.window.webpackJsonp={push:entry=>Object.values(entry[1])[0]({}, {}, require)};
  h.window.__pookieRequest({id:command(1150).id,operation:'follow-signature',user_id:42,artist_id:7});await tick();
  const text=h.messages.filter(message=>message.kind==='json-chunk').map(message=>message.chunk).join('');
  assert.equal(JSON.parse(text).signature,'fixture-signature');assert.equal(h.calls.length,0);
  assert.equal(h.messages.at(-1).status,200);assert.equal(JSON.stringify(h.messages).includes('fixture-secret'),false);
  const before=h.messages.length;
  h.window.__pookieRequest({id:command(1151).id,operation:'follow-signature',user_id:42,artist_id:42});await tick();
  assert.equal(h.messages.length,before);
});

test('native refresh updates the live browser token and rejects refresh secrets and foreign client sessions', async () => {
  const h=harness(()=>response(200,{id:42}), 'https://soundcloud.com', false, true, true, true, {session_id:'family'});
  const update=(id, session)=>({id:command(id).id,operation:'session-update',session});
  h.window.__pookieRequest(update(1160,{client_id:'fixtureid',access_token:'rotated-token',session_id:'family'}));
  h.window.__pookieRequest(command(1161,'me'));await tick();
  assert.equal(h.calls[0][1].headers.Authorization,'OAuth rotated-token');
  for (const session of [
    {client_id:'foreign',access_token:'foreign-token',session_id:'family'},
    {client_id:'fixtureid',access_token:'bad-token',session_id:'other-family'},
    {client_id:'fixtureid',access_token:'bad-token',session_id:'family',refresh_token:'private-refresh'},
    {client_id:'fixtureid',access_token:'bad-token',session_id:'family',mobile_identity:{android_id:'device'}}
  ]) h.window.__pookieRequest(update(1162,session));
  h.window.__pookieRequest(command(1163,'me'));await tick();
  assert.equal(h.calls[1][1].headers.Authorization,'OAuth rotated-token');
  assert.equal(JSON.stringify(h.messages).includes('private-refresh'),false);
});

test('following and own playlist reads are allowed while writes cannot masquerade as GET', async () => {
  const h=harness(()=>response(200,[]));
  for(const [i,path] of ['/users/42/playlists','/users/42/followings/ids','/me/followings/7'].entries())
    h.window.__pookieRequest({id:command(1200+i).id,operation:'api-get',url:'https://api-v2.soundcloud.com'+path});
  await tick();await tick();assert.equal(h.calls.length,2);
});

test('a refreshed website session updates authorization without replacing the page', async () => {
  const h = harness(() => response(200, {id:42, username:'fixture'}));
  h.window.__pookieUpdateSession({client_id:'newclient', access_token:'rotated-token'});
  h.window.__pookieRequest(command(99, 'me'));
  await tick();
  assert.equal(h.calls[0][1].headers.Authorization, 'OAuth rotated-token');
  assert.equal(new URL(h.calls[0][0]).searchParams.get('client_id'), 'newclient');
});


test('playlist likes and reposts use dedicated endpoints and reject mixed identities', async () => {
  const h=harness(()=>response(204));
  const like=(n,value)=>({id:command(n).id,operation:'playlist-like',user_id:42,playlist_id:11,track_id:0,liked:value});
  const repost=(n,value)=>({id:command(n).id,operation:'playlist-repost',user_id:0,playlist_id:11,track_id:0,reposted:value});
  for(const request of [like(1301,true),like(1302,false),repost(1303,true),repost(1304,false)]) h.window.__pookieRequest(request);
  await tick();await tick();
  assert.deepEqual(h.calls.map(call=>call[1].method),['PUT','DELETE','PUT','DELETE']);
  assert.deepEqual(h.calls.map(call=>new URL(call[0]).pathname),[
    '/users/42/playlist_likes/11','/users/42/playlist_likes/11','/me/playlist_reposts/11','/me/playlist_reposts/11']);
  for(const request of [{...like(1305,true),track_id:90},{...like(1306,true),user_id:0},
    {...like(1307,true),playlist_id:Number.MAX_SAFE_INTEGER+1},{...repost(1308,true),user_id:42},
    {...repost(1309,true),reposted:'true'}, {...like(1310,true),liked:'true'}]) h.window.__pookieRequest(request);
  await tick();assert.equal(h.calls.length,4);
});

test('playlist action state reads are allowed, action endpoints cannot masquerade as GET', async () => {
  const h=harness(()=>response(200,{collection:[11],next_href:null}));
  for(const [i,path] of ['/me/playlist_likes/ids','/me/playlist_reposts/ids',
    '/me/playlist_reposts/11','/users/42/playlist_likes/11'].entries())
    h.window.__pookieRequest({id:command(1320+i).id,operation:'api-get',url:'https://api-v2.soundcloud.com'+path});
  await tick();assert.equal(h.calls.length,2);
  assert.equal(h.messages.filter(message=>message.kind==='complete').every(message=>message.status===200),true);
});

test('ordinary playlist write failures are not blindly replayed', async () => {
  for(const operation of ['playlist-like','playlist-repost']) for(const status of [403,401,500]) {
    const h=harness(()=>response(status));
    h.window.__pookieRequest({id:command(1330+status).id,operation,user_id:operation==='playlist-like'?42:0,playlist_id:11,liked:true,reposted:true});
    await tick();assert.equal(h.calls.length,1);assert.equal(h.messages.at(-1).status,status);
  }
});

test('chat reads and writes stay in the authenticated account and send the native contents field', async () => {
  const h = harness((url, options) => response(200, new URL(url).pathname === '/me' ? {id:42} : {collection:[]}));
  const chat = (id, kind, extra={}) => ({id:id.toString(16).padStart(32,'0'), operation:'chat', chat:{kind,user_id:42,recipient_id:kind==='list'?0:90,...extra}});
  h.window.__pookieRequest(chat(1100,'list'));
  h.window.__pookieRequest(chat(1101,'thread'));
  h.window.__pookieRequest(chat(1102,'send',{body:'Привет 🎵'}));
  h.window.__pookieRequest(chat(1103,'read'));
  await tick();
  assert.equal(h.calls.length,5);
  assert.equal(new URL(h.calls[1][0]).pathname,'/users/42/conversations');
  assert.equal(new URL(h.calls[2][0]).pathname,'/users/42/conversations/90/messages');
  assert.equal(h.calls[3][1].method,'POST');
  assert.deepEqual(JSON.parse(h.calls[3][1].body),{contents:'Привет 🎵'});
  assert.equal(h.calls[4][1].body,'read=true');
  assert.equal(h.calls[4][1].headers['Content-Type'],'application/x-www-form-urlencoded; charset=UTF-8');
  assert.equal(h.calls[1][1].cache,'no-store');
  assert.equal(h.calls[2][1].cache,'no-store');
  for(const [,options] of h.calls){assert.equal(options.headers.Authorization,'OAuth secret-token');assert.equal(options.redirect,'error');}
  for(const extra of [{user_id:43},{recipient_id:42},{body:'\0'},{body:'a'.repeat(20001)},{cursor:'https://evil.test/'}])
    h.window.__pookieRequest(chat(1104,'send',{body:'message',...extra}));
  for(const cursor of ['https://api-v2.soundcloud.com/users/43/conversations/90/messages',
    'https://api-v2.soundcloud.com/users/42/conversations/91/messages','https://evil@api-v2.soundcloud.com/users/42/conversations/90/messages'])
    h.window.__pookieRequest(chat(1105,'thread',{cursor}));
  h.window.__pookieRequest({...chat(1106,'list'),url:'https://evil.test/'});
  await tick();
  // A claimed account mismatch may read /me but must never reach another conversation.
  assert.equal(h.calls.length,5);
});

test('an interrupted chat POST is not replayed after a browser check', async () => {
  let h, posts=0;
  h=harness((url,options)=>{
    if(new URL(url).pathname==='/me')return response(200,{id:42});
    posts++;h.emit('dd_blocked','device_check');h.emit('dd_response_passed','device_check');
    throw new TypeError('Connection interrupted after POST');
  });
  h.window.__pookieRequest({id:'c'.repeat(32),operation:'chat',chat:{kind:'send',user_id:42,recipient_id:90,body:'Once'}});
  await tick();
  assert.equal(posts,1);assert.equal(h.messages.at(-1).status,0);
});

test('a chat POST rejected by a device check resumes once after passing without completing the request early', async () => {
  let h, posts=0;
  h=harness((url)=>{
    if(new URL(url).pathname==='/me')return response(200,{id:42});
    if(++posts===1){h.emit('dd_blocked','device_check');return response(403);}
    return response(200,{id:'42:90'});
  });
  const id='d'.repeat(32);
  h.window.__pookieRequest({id,operation:'chat',chat:{kind:'send',user_id:42,recipient_id:90,body:'Resume'}});
  await tick();assert.equal(posts,1);assert.ok(!h.messages.some(message=>message.request_id===id&&message.kind==='complete'));
  h.emit('dd_response_passed','device_check');await tick();
  assert.equal(posts,2);assert.equal(h.messages.at(-1).status,200);
  assert.equal(h.messages.filter(message=>message.kind==='complete').length,1);
});

test('chat rejection also resumes when the check passes before its 403 reaches fetch', async () => {
  let h, posts=0;
  h=harness((url)=>{
    if(new URL(url).pathname==='/me')return response(200,{id:42});
    if(++posts===1){h.emit('dd_blocked','device_check');h.emit('dd_response_passed','device_check');return response(403);}
    return response(200);
  });
  h.window.__pookieRequest({id:'e'.repeat(32),operation:'chat',chat:{kind:'send',user_id:42,recipient_id:90,body:'Resume'}});
  await tick();assert.equal(posts,2);assert.equal(h.messages.at(-1).status,200);
});

test('an ordinary rejected chat POST is not retried and closing a check prevents its retry', async () => {
  for(const cancel of [false,true]){
    let h, posts=0;
    h=harness((url)=>{
      if(new URL(url).pathname==='/me')return response(200,{id:42});
      posts++;if(cancel)h.emit('dd_blocked','device_check');return response(403);
    });
    const id='f'.repeat(32);
    h.window.__pookieRequest({id,operation:'chat',chat:{kind:'send',user_id:42,recipient_id:90,body:'No duplicate'}});
    await tick();
    if(cancel){h.emit('dd_response_unload');h.emit('dd_response_passed','device_check');await tick();}
    assert.equal(posts,1);
  }
});

test('user profile reads allow metadata, urn web links and scoped music/social collections only', async () => {
  const h = harness(() => response(200, {collection:[]}));
  const paths = ['/stream/users/7', '/users/7', '/users/soundcloud:users:7/web-profiles', '/users/soundcloud%3Ausers%3A7/web-profiles',
    '/users/7/tracks', '/users/7/albums', '/users/7/playlists_without_albums', '/stream/users/7/reposts',
    '/users/7/comments', '/users/7/likes', '/users/7/followers', '/users/7/followings'];
  for (const [i,path] of paths.entries()) { h.window.__pookieRequest(apiRead(2000+i,path)); await tick(); }
  assert.equal(h.calls.length,paths.length);
  for (const [i,path] of ['/users/7/emails','/users/7/settings','/users/0','/users/soundcloud:tracks:7/web-profiles','/stream/users/7/delete'].entries()) {
    h.window.__pookieRequest(apiRead(2100+i,path)); await tick();
  }
  assert.equal(h.calls.length,paths.length);
});


test('diagnostics distinguish a post-pass 403 from the initial check and never contain private request data', async () => {
  let count=0;
  const h=harness(async()=>{
    if(++count===1)h.emit('dd_response_displayed','block');
    return response(403,{secret:'private-response-body'});
  });
  const cmd=command(101);
  h.window.__pookieRequest(cmd); await tick();
  h.emit('dd_response_passed','block'); await tick();
  const events=h.messages.filter(m=>m.kind==='request-trace');
  assert.deepEqual(events.map(m=>[m.trace.stage,m.trace.attempt,m.status,m.trace.generation]),[
    ['send',1,0,0],['response',1,403,0],['retry',1,403,0],['send',2,0,1],['response',2,403,1]
  ]);
  assert.ok(events.every(m=>m.request_id===cmd.id&&m.trace.method==='PUT'&&m.trace.fetch_number===1));
  assert.equal(events[1].trace.challenge_active,true);
  assert.equal(events[3].trace.challenge_active,false);
  assert.equal(events[3].trace.has_protection,true);
  assert.equal(h.messages.at(-1).status,403);
  const journal=JSON.stringify(events);
  for(const secret of ['secret-token','own-browser-session','private-response-body','https://','Authorization'])
    assert.equal(journal.includes(secret),false);
});

test('ordinary 403 has a response trace and no challenge retry', async () => {
  const h=harness(()=>response(403));
  h.window.__pookieRequest(command(102)); await tick();
  const events=h.messages.filter(m=>m.kind==='request-trace');
  assert.deepEqual(events.map(m=>m.trace.stage),['send','response']);
  assert.equal(events[1].trace.generation,0);
  assert.equal(events[1].trace.challenge_active,false);
});
