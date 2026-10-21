'use strict';

// Fixed playback correction and an optional bounded tap before gain/volume.
// The limiter only protects boosted sample peaks from clipping.
class PookieGainProcessor extends AudioWorkletProcessor {
  constructor(options) {
    super();
    const db=options.processorOptions?.gainDb ?? 0;
    if (!Number.isFinite(db) || db < -24 || db > 6) throw new Error('Invalid track gain');
    this.gain=Math.pow(10,db/20); this.limit=1;
    this.release=1-Math.exp(-128/(sampleRate*.5));
    this.capture=options.processorOptions?.captureAnalysis === true;
    this.active=false; this.count=0; this.chunk=new Float32Array(1024); this.used=0;
    this.ratio=sampleRate/16000; this.remaining=this.ratio; this.sum=0;
    this.port.onmessage=event=>{
      if (typeof event.data?.active === 'boolean') this.active=event.data.active;
      if (event.data?.captureAnalysis === false) { this.capture=false; this.chunk=new Float32Array(0); this.used=0; }
    };
  }
  analyze(input) {
    if (!this.capture || !this.active || this.count>=144000) return;
    for(let frame=0;frame<input[0].length;frame++) {
      let value=0;
      for(const channel of input) value+=Number.isFinite(channel[frame]) ? channel[frame] : 0;
      value/=input.length;
      let available=1;
      while(available>1e-9) {
        const used=Math.min(available,this.remaining);
        this.sum+=value*used; available-=used; this.remaining-=used;
        if(this.remaining>1e-9) continue;
        this.chunk[this.used++]=this.sum/this.ratio; this.sum=0; this.remaining=this.ratio; this.count++;
        if(this.used===1024 || this.count===144000) {
          const samples=this.chunk.slice(0,this.used);
          this.port.postMessage({offset:this.count-this.used,samples},[samples.buffer]); this.used=0;
        }
        if(this.count===144000) return;
      }
    }
  }
  process(inputs,outputs) {
    const input=inputs[0],output=outputs[0];
    if (!input?.length) return true;
    this.analyze(input);
    const length=input[0].length;
    if (this.gain<=1) {
      for(let ch=0;ch<output.length;ch++) for(let frame=0;frame<length;frame++) {
        const sample=input[ch]?.[frame] ?? 0;
        output[ch][frame]=Number.isFinite(sample) ? sample*this.gain : 0;
      }
      return true;
    }
    let peak=0;
    for (const channel of input) for (const sample of channel)
      if (Number.isFinite(sample)) peak=Math.max(peak,Math.abs(sample));
    const limit=peak===0 ? 1 : Math.min(1,.8413951416451951/(peak*this.gain));
    this.limit=limit<this.limit ? limit : this.limit+(limit-this.limit)*this.release;
    const gain=this.gain*this.limit;
    for(let ch=0;ch<output.length;ch++) for(let frame=0;frame<length;frame++) {
      const sample=input[ch]?.[frame] ?? 0;
      output[ch][frame]=Number.isFinite(sample) ? sample*gain : 0;
    }
    return true;
  }
}
registerProcessor('pookie-track-gain',PookieGainProcessor);
