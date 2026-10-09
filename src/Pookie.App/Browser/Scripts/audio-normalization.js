'use strict';

// A fixed correction supplied before playback. No analysis or priming.
// The limiter only protects boosted sample peaks from clipping.
class PookieGainProcessor extends AudioWorkletProcessor {
  constructor(options) {
    super();
    const db=options.processorOptions?.gainDb ?? 0;
    if (!Number.isFinite(db) || db < -24 || db > 6) throw new Error('Invalid track gain');
    this.gain=Math.pow(10,db/20); this.limit=1;
    this.release=1-Math.exp(-128/(sampleRate*.5));
  }
  process(inputs,outputs) {
    const input=inputs[0],output=outputs[0];
    if (!input?.length) return true;
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
