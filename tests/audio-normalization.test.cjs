const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/Pookie.App/Browser/Scripts/audio-normalization.js'),'utf8');
function processor(db,capture=false,messages=[]) {
  let Processor;
  vm.runInNewContext(source,{sampleRate:48000,AudioWorkletProcessor:class {constructor(){this.port={postMessage:m=>messages.push(m)};}},registerProcessor:(name,type)=>{Processor=type;}});
  return new Processor({processorOptions:{gainDb:db,captureAnalysis:capture}});
}
test('analysis captures pre-gain mono only while playing, stops after nine seconds and leaves playback intact',()=>{
  const messages=[],p=processor(-12,true,messages);
  const input=[new Float32Array(128).fill(.2),new Float32Array(128).fill(.6)],output=[new Float32Array(128),new Float32Array(128)];
  p.process([input],[output]);assert.equal(p.count,0);
  p.port.onmessage({data:{active:true}});
  for(let i=0;i<3500;i++)p.process([input],[output]);
  const samples=messages.flatMap(m=>Array.from(m.samples));
  assert.equal(samples.length,144000);assert.equal(p.count,144000);
  let offset=0;for(const m of messages){assert.equal(m.offset,offset);assert.ok(m.samples.length<=1024);offset+=m.samples.length;}
  assert.ok(samples.every(v=>Math.abs(v-.4)<1e-6));
  assert.ok(Math.abs(output[0][0]-.2*Math.pow(10,-12/20))<1e-6);
  const paused=processor(0,true,[]);paused.port.onmessage({data:{active:true}});paused.process([input],[output]);
  const count=paused.count;paused.port.onmessage({data:{active:false}});paused.process([input],[output]);assert.equal(paused.count,count);
  const disabled=processor(0,false,[]);disabled.port.onmessage({data:{active:true}});disabled.process([input],[output]);assert.equal(disabled.count,0);
});
test('the supplied track gain applies immediately and stays fixed across silence and different passages',()=>{
  for(const db of [-12,0,6]) {
    const p=processor(db),factor=Math.pow(10,db/20);
    for(const amplitude of [.03,.3,0,.04,.4]) {
      const input=[new Float32Array(128).fill(amplitude),new Float32Array(128).fill(amplitude)];
      const output=[new Float32Array(128),new Float32Array(128)];
      for(let i=0;i<500;i++)p.process([input],[output]);
      assert.ok(Math.abs(output[0][0]-amplitude*factor)<1e-6);
      assert.ok(Math.abs(output[1][127]-amplitude*factor)<1e-6);
      assert.equal(p.gain,factor);
    }
  }
});
test('fixed boost protects linked sample peaks and sanitizes invalid input',()=>{
  const p=processor(6),left=new Float32Array(128).fill(.9),right=new Float32Array(128).fill(.45);
  left[0]=NaN;right[0]=Infinity;
  const output=[new Float32Array(128),new Float32Array(128)];
  p.process([[left,right]],[output]);
  for(const channel of output)for(const value of channel)assert.ok(Number.isFinite(value)&&Math.abs(value)<=.842);
  for(let i=1;i<128;i++)assert.equal(output[0][i]/2,output[1][i]);
});
test('invalid track corrections are rejected before processing',()=>{
  for(const db of [NaN,Infinity,-25,7])assert.throws(()=>processor(db));
});
