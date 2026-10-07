const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../src/Pookie.App/Browser/Scripts/browser-audio.js'), 'utf8');

test('audio events report the actual buffered interval at the playhead, including seek gaps and removal', async () => {
  const messages = [];
  let publish, ranges = [], resolveAccess, accessStarted;
  const ready = new Promise(resolve => { accessStarted = resolve; });
  const access = new Promise(resolve => { resolveAccess = resolve; });
  const audio = {currentTime:0, readyState:3, networkState:1, paused:false, ended:false, style:{},
    buffered:{get length() { return ranges.length; }, start:i=>ranges[i][0], end:i=>ranges[i][1]},
    addEventListener:()=>{}, pause:()=>{}, load:()=>{}, remove:()=>{}, removeAttribute:()=>{}};
  const window = {MediaSource:{}, addEventListener:()=>{}};
  window.top = window;
  const manifest = '#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n' +
    '#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,KEYFORMAT="urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed",URI="data:;base64,' +
    Buffer.alloc(32).toString('base64') + '"\n#EXTINF:120,\nsegment.mp4\n#EXT-X-ENDLIST';
  vm.runInNewContext(script, {window, location:{origin:'https://soundcloud.com'}, URL, AbortController, TextDecoder,
    atob, HTMLMediaElement:class {}, MediaSource:{isTypeSupported:()=>true},
    navigator:{requestMediaKeySystemAccess:()=>{accessStarted(); return access;}},
    document:{addEventListener:()=>{}, querySelectorAll:()=>[], createElement:()=>audio, body:{append:()=>{}}},
    fetch:async url=>new Response(url.endsWith('.m3u8') ? manifest : new Uint8Array(32)),
    setInterval:callback=>{publish=callback; return 1;}, clearInterval:()=>{}, setTimeout, clearTimeout});
  const id = '1'.repeat(32);
  const starting = window.__pookieAudioRequest({id, audio:{action:'start', playback_id:id,
    source:'https://media.sndcdn.com/track.m3u8', authorization:'fixture', position:0, volume:70, paused:false}}, m=>messages.push(m));
  await ready;
  const buffer = () => { publish(); const s = messages.at(-1).audio; return [s.buffered_start, s.buffered_end]; };
  assert.deepEqual(buffer(), [0, 0]);
  ranges = [[0, 45], [80, 120.05]];
  audio.currentTime = 10;
  assert.deepEqual(buffer(), [0, 45]);
  audio.currentTime = 65;
  assert.deepEqual(buffer(), [0, 0]);
  audio.currentTime = 90;
  assert.deepEqual(buffer(), [80, 120]);
  ranges = [];
  assert.deepEqual(buffer(), [0, 0]);
  await window.__pookieAudioRequest({id, audio:{action:'stop', playback_id:id, position:0, volume:70}}, m=>messages.push(m));
  resolveAccess({});
  await starting;
});
