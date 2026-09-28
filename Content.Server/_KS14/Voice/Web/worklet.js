// Captures microphone audio at the context's native rate and emits 20 ms frames of 16 kHz mono Int16.
//
// Resampling happens here rather than by creating the AudioContext at 16 kHz, because Firefox refuses to connect a
// microphone stream to a context whose rate differs from the device's. Each output sample is the mean of the input
// samples it spans (a box filter), which doubles as a crude anti-aliasing low-pass.

const TARGET_RATE = 16000;
const FRAME_SAMPLES = 320;

class KsVoiceCaptureProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.ratio = sampleRate / TARGET_RATE;
        this.phase = 0;
        this.sum = 0;
        this.count = 0;
        this.frame = new Int16Array(FRAME_SAMPLES);
        this.frameLength = 0;
        this.frameSquares = 0;
    }

    process(inputs) {
        const input = inputs[0];
        if (!input || input.length === 0)
            return true;

        const channel = input[0];
        for (let i = 0; i < channel.length; i++) {
            this.sum += channel[i];
            this.count++;
            this.phase += 1;

            while (this.phase >= this.ratio) {
                this.phase -= this.ratio;
                this.push(this.count > 0 ? this.sum / this.count : 0);
                this.sum = 0;
                this.count = 0;
            }
        }

        return true;
    }

    push(value) {
        const clamped = Math.max(-1, Math.min(1, value));
        const sample = Math.round(clamped * 32767);
        this.frame[this.frameLength++] = sample;
        this.frameSquares += sample * sample;

        if (this.frameLength < FRAME_SAMPLES)
            return;

        const rms = Math.sqrt(this.frameSquares / FRAME_SAMPLES);
        const frame = this.frame;
        this.port.postMessage({ frame: frame.buffer, rms }, [frame.buffer]);

        this.frame = new Int16Array(FRAME_SAMPLES);
        this.frameLength = 0;
        this.frameSquares = 0;
    }
}

registerProcessor("ks-voice-capture", KsVoiceCaptureProcessor);
