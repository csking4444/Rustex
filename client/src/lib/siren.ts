/**
 * A synthesized two-tone sweep, generated purely via the Web Audio API — no external audio
 * asset required. Loud enough to notice, distinct enough from a regular notification chime to
 * read as "this needs your attention now."
 */
let audioContext: AudioContext | null = null;
let oscillator: OscillatorNode | null = null;
let gainNode: GainNode | null = null;
let sweepIntervalId: number | null = null;

export function startSiren(): void {
  if (audioContext) return; // already playing

  audioContext = new AudioContext();
  oscillator = audioContext.createOscillator();
  gainNode = audioContext.createGain();

  oscillator.type = "sawtooth";
  oscillator.frequency.value = 440;
  gainNode.gain.value = 0.12;

  oscillator.connect(gainNode);
  gainNode.connect(audioContext.destination);
  oscillator.start();

  let rising = true;
  sweepIntervalId = window.setInterval(() => {
    if (!oscillator || !audioContext) return;
    oscillator.frequency.linearRampToValueAtTime(rising ? 880 : 440, audioContext.currentTime + 0.5);
    rising = !rising;
  }, 500);
}

export function stopSiren(): void {
  if (sweepIntervalId !== null) {
    window.clearInterval(sweepIntervalId);
    sweepIntervalId = null;
  }

  oscillator?.stop();
  oscillator?.disconnect();
  gainNode?.disconnect();
  void audioContext?.close();

  oscillator = null;
  gainNode = null;
  audioContext = null;
}
