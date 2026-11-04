const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/Pookie.App/Browser/Scripts/audio-normalization.js'),'utf8');
function processor(db) {
  let Processor;
  vm.runInNewContext(source,{sampleRate:48000,AudioWorkletProcessor:class {},registerProcessor:(name,type)=>{Processor=type;}});
  return new Processor({processorOptions:{gainDb:db}});
}
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
