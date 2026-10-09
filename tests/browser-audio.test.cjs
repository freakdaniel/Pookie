const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../src/Pookie.App/Browser/Scripts/browser-audio.js'), 'utf8')
  .replace('__POOKIE_NORMALIZATION_MODULE__', JSON.stringify(fs.readFileSync(path.join(__dirname,
    '../src/Pookie.App/Browser/Scripts/audio-normalization.js'), 'utf8')));

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

function normalizationFixture(db, blockedModule=false) {
  const messages=[], audios=[], contexts=[], sessions=[];
  const urls=new Map(); let node;
  class Media extends EventTarget {
    constructor(){super();this.style={};this.paused=true;this.readyState=3;this.buffered={length:0};this.time=0;this.ended=false;}
    get currentTime(){return this.time;}
    set currentTime(value){this.time=value;queueMicrotask(()=>this.dispatchEvent(new Event('seeked')));}
    load(){const source=urls.get(this.src);if(source)queueMicrotask(()=>source.dispatchEvent(new Event('sourceopen')));}
    async play(){this.paused=false;this.time=.5;this.dispatchEvent(new Event('playing'));}
    pause(){this.paused=true;this.dispatchEvent(new Event('pause'));}
    remove(){this.removed=true;}
    removeAttribute(){this.src=null;}
    async setMediaKeys(){}
  }
  class Source extends EventTarget {
    constructor(){super();this.readyState='open';}
    static isTypeSupported(){return true;}
    addSourceBuffer(){const buffer=new EventTarget();buffer.appendBuffer=()=>queueMicrotask(()=>buffer.dispatchEvent(new Event('updateend')));return buffer;}
    endOfStream(){this.readyState='ended';}
  }
  class Context {
    constructor(){this.state='running';this.currentTime=0;this.audioWorklet={addModule:async()=>{if(blockedModule)throw Error('CSP');}};contexts.push(this);}
    async resume(){}
    async close(){this.state='closed';}
    createGain(){this.master={gain:{value:0,setTargetAtTime(value){this.value=value;}},connect(){return this;},disconnect(){}};return this.master;}
    createMediaElementSource(){return {connect(other){return other;},disconnect(){}};}
  }
  class Node {
    constructor(){node=this;this.port={postMessage:()=>{}};}
    connect(other){return other;}
    disconnect(){}
  }
  const window={MediaSource:Source,AudioContext:Context,AudioWorkletNode:Node,addEventListener:()=>{}};window.top=window;
  const manifest='#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n'+
    '#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,KEYFORMAT="urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed",URI="data:;base64,'+
    Buffer.alloc(32).toString('base64')+'"\n#EXTINF:120,\nsegment.mp4\n#EXT-X-ENDLIST';
  class LocalURL extends URL {
    static createObjectURL(source){const url='blob:fixture-'+urls.size;urls.set(url,source);return url;}
    static revokeObjectURL(url){urls.delete(url);}
  }
  const access={createMediaKeys:async()=>({createSession:()=>{
    const session=new EventTarget();session.keyStatuses=new Map([[1,'usable']]);
    session.generateRequest=async()=>session.dispatchEvent(new Event('keystatuseschange'));
    session.close=async()=>{session.closed=true;};sessions.push(session);return session;
  }})};
  vm.runInNewContext(script,{window,location:{origin:'https://soundcloud.com'},URL:LocalURL,Blob,AbortController,TextDecoder,
    atob,HTMLMediaElement:Media,MediaSource:Source,AudioContext:Context,AudioWorkletNode:Node,
    navigator:{requestMediaKeySystemAccess:async()=>access},
    document:{addEventListener:()=>{},querySelectorAll:()=>[],createElement:()=>{const audio=new Media();audios.push(audio);return audio;},body:{append:()=>{}}},
    fetch:async url=>new Response(url.endsWith('.m3u8') ? manifest : new Uint8Array(32)),
    setInterval:()=>1,clearInterval:()=>{},setTimeout,clearTimeout});
  const id='2'.repeat(32);
  const command=(action,values={})=>window.__pookieAudioRequest({id,audio:{action,playback_id:id,position:0,volume:70,...values}},m=>messages.push(m));
  const start=(paused=false)=>command('start',{source:'https://media.sndcdn.com/track.m3u8',authorization:'fixture',normalization_gain_db:db,paused});
  return {command,start,messages,audios,contexts,sessions};
}

test('negative DRM correction applies before playback without a graph or calibration',async()=>{
  const f=normalizationFixture(-12);
  await f.start();
  const factor=Math.pow(10,-12/20);
  assert.equal(f.messages.at(-1).status,200);
  assert.equal(f.contexts.length,0);
  assert.ok(Math.abs(f.audios[0].volume-.7*factor)<1e-10);
  await f.command('volume',{volume:100});
  assert.ok(Math.abs(f.audios[0].volume-factor)<1e-10);
  const state=f.messages.findLast(m=>m.kind==='audio-state').audio;
  assert.equal(state.normalization_gain_db,-12);assert.equal(state.playing,true);
  await f.command('stop');
});
test('positive fixed gain is separate from master volume and does not secretly play a paused start',async()=>{
  const f=normalizationFixture(6);
  await f.start(true);
  assert.equal(f.messages.at(-1).status,200);assert.equal(f.audios[0].paused,true);
  assert.equal(f.audios[0].currentTime,0);
  assert.equal(f.audios[0].volume,1);assert.equal(f.contexts[0].master.gain.value,.7);
  await f.command('volume',{volume:0});assert.equal(f.contexts[0].master.gain.value,0);
  await f.command('pause',{paused:false});
  const state=f.messages.findLast(m=>m.kind==='audio-state').audio;
  assert.equal(state.normalization_gain_db,6);assert.equal(state.playing,true);
  await f.command('stop');assert.equal(f.contexts[0].state,'closed');assert.equal(f.sessions[0].closed,true);
});
test('a blocked boost Worklet still allows direct protected playback and mute',async()=>{
  const f=normalizationFixture(6,true);
  await f.start();
  assert.equal(f.messages.at(-1).status,200);assert.equal(f.audios.length,1);
  assert.equal(f.contexts[0].state,'closed');assert.equal(f.audios[0].volume,1);
  await f.command('volume',{volume:0});assert.equal(f.audios[0].volume,0);
  await f.command('stop');
});
