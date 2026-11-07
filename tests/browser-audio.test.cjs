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

function normalizationFixture(db, blockedModule=false, emeError=null, options={}) {
  const messages=[], audios=[], contexts=[], sessions=[];
  const urls=new Map(), stored=new Map(), requests=new Map(), deleted=[]; let node, blocked=options.blockedFrom;
  const events=new EventTarget();
  const caches={open:async()=>({match:async key=>stored.get(key)?.clone(),put:async(key,response)=>{stored.set(key,response.clone());}}),keys:async()=>[],delete:async name=>{stored.clear();deleted.push(name);return true;}};
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
  const window={MediaSource:Source,AudioContext:Context,AudioWorkletNode:Node,addEventListener:events.addEventListener.bind(events),removeEventListener:events.removeEventListener.bind(events),caches:options.cache?caches:undefined};window.top=window;
  const manifest='#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n'+
    '#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,KEYFORMAT="urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed",URI="data:;base64,'+
    Buffer.alloc(32).toString('base64')+'"\n'+Array.from({length:options.segments||1},(_,i)=>'#EXTINF:'+(120/(options.segments||1))+',\nsegment'+i+'.mp4\n').join('')+'#EXT-X-ENDLIST';
  class LocalURL extends URL {
    static createObjectURL(source){const url='blob:fixture-'+urls.size;urls.set(url,source);return url;}
    static revokeObjectURL(url){urls.delete(url);}
  }
  const access={createMediaKeys:async()=>({createSession:()=>{
    const session=new EventTarget();session.keyStatuses=new Map([[1,'usable']]);
    session.generateRequest=async()=>session.dispatchEvent(new Event('keystatuseschange'));
    session.close=async()=>{session.closed=true;};sessions.push(session);return session;
  }})};
  vm.runInNewContext(script,{window,caches,Response,location:{origin:'https://soundcloud.com'},URL:LocalURL,Blob,AbortController,TextDecoder,
    atob,btoa,Float32Array,HTMLMediaElement:Media,MediaSource:Source,AudioContext:Context,AudioWorkletNode:Node,
    navigator:{requestMediaKeySystemAccess:async()=>{if(emeError)throw Object.assign(new Error('EME unavailable'),{name:emeError});return access;}},
    document:{addEventListener:()=>{},querySelectorAll:()=>[],createElement:()=>{const audio=new Media();audios.push(audio);return audio;},body:{append:()=>{}}},
    fetch:async url=>{requests.set(url,(requests.get(url)||0)+1);const segment=/segment(\d+)\.mp4$/.exec(url);if(segment&&options.status)return new Response(null,{status:options.status});if(segment&&blocked!=null&&Number(segment[1])>=blocked)throw new TypeError('Network disconnected');return new Response(url.endsWith('.m3u8') ? manifest : new Uint8Array(32));},
    setInterval:()=>1,clearInterval:()=>{},setTimeout,clearTimeout});
  const id='2'.repeat(32);
  const command=(action,values={})=>window.__pookieAudioRequest({id,audio:{action,playback_id:id,position:0,volume:70,...values}},m=>messages.push(m));
  const start=(paused=false)=>command('start',{source:'https://media.sndcdn.com/track.m3u8',authorization:'fixture',normalization_gain_db:db,paused});
  return {command,start,messages,audios,contexts,sessions,stored,requests,deleted,reconnect(){blocked=null;events.dispatchEvent(new Event("online"));},get node(){return node;}};
}

test('missing or disabled Widevine is reported as unsupported at EME instead of a decoding failure',async()=>{
  for(const error of ['NotSupportedError','NotAllowedError','SecurityError']){
    const f=normalizationFixture(0,false,error);await f.start();
    const failed=f.messages.findLast(m=>m.kind==='audio-state' && m.audio.error);
    assert.equal(failed.audio.error,'unsupported');assert.equal(failed.audio.stage,'eme');
    assert.equal(f.sessions.length,0);assert.equal(f.audios[0].paused,true);
    assert.equal(f.messages.at(-1).status,0);await f.command('stop');
  }
});

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

async function until(ready) {
  const start=Date.now();
  while(!ready()){if(Date.now()-start>4000)throw new Error('Fixture did not settle');await new Promise(resolve=>setTimeout(resolve,10));}
}
test('encrypted segments download to the end while paused and reconnect without replacing the audio element',async()=>{
  const f=normalizationFixture(0,false,null,{cache:true,segments:12,blockedFrom:2});
  await f.start(true);
  const first=f.audios[0];
  await until(()=>[...f.requests].some(([url])=>url.endsWith('segment2.mp4')));
  assert.equal(first.paused,true);assert.equal(first.currentTime,0);
  assert.ok(!f.messages.some(m=>m.audio?.error));
  f.reconnect();
  await until(()=>f.stored.size===12);
  await f.command('volume',{volume:80});
  const state=f.messages.findLast(m=>m.kind==='audio-state').audio;
  assert.equal(state.buffered_end,120);assert.equal(state.playing,false);
  assert.equal(f.audios.length,1);assert.equal(first.currentTime,0);
  assert.equal([...f.requests].find(([url])=>url.endsWith('segment0.mp4'))[1],1);
  await f.command('stop');await until(()=>f.deleted.length===1);
});
test('stop cancels a disconnected encrypted prefetch and removes its disk cache',async()=>{
  const f=normalizationFixture(0,false,null,{cache:true,segments:12,blockedFrom:2});
  await f.start(true);
  await until(()=>[...f.requests].some(([url])=>url.endsWith('segment2.mp4')));
  await f.command('stop');await until(()=>f.deleted.length===1);
  const count=[...f.requests.values()].reduce((sum,n)=>sum+n,0);
  await new Promise(resolve=>setTimeout(resolve,300));
  assert.equal([...f.requests.values()].reduce((sum,n)=>sum+n,0),count);
});

test('permanent media authorization errors terminate instead of retrying indefinitely',async()=>{
  const f=normalizationFixture(0,false,null,{status:403});
  await f.start();
  const state=f.messages.findLast(m=>m.kind==='audio-state').audio;
  assert.equal(state.error,'network');
  assert.equal([...f.requests].find(([url])=>url.endsWith('segment0.mp4'))[1],1);
  await f.command('stop');
});
