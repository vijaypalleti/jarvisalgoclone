import { Injectable, NgZone, inject, signal } from '@angular/core';

/** idle: mic off · wake: listening only for "Jarvis" · listening: capturing the question · thinking: waiting for the answer · speaking: reading it out */
export type VoiceState = 'idle' | 'wake' | 'listening' | 'thinking' | 'speaking';

const WAKE = /\b(jarvis|jarvis's|jervis|jarvas|jarves|javis)\b/i;
const WAKE_PREF = 'jarvis.voice.wake';

/**
 * Voice for the assistant, using the browser's Web Speech API:
 *   • SpeechRecognition (Chrome / Edge): wake word "Jarvis" + the spoken question. NOTE: these browsers send the
 *     audio to their cloud speech service while the mic is listening, so wake-word mode is opt-in.
 *   • speechSynthesis (all modern browsers): reads the answer aloud.
 *   • A Web Audio analyser gives a live mic level (0..1) that drives the bottom glow.
 */
@Injectable({ providedIn: 'root' })
export class VoiceService {
  private zone = inject(NgZone);

  readonly recognitionSupported = typeof window !== 'undefined' && !!((window as any).SpeechRecognition || (window as any).webkitSpeechRecognition);
  readonly ttsSupported = typeof window !== 'undefined' && 'speechSynthesis' in window;

  readonly state = signal<VoiceState>('idle');
  readonly wakeEnabled = signal(false);
  readonly transcript = signal('');          // live words of the question
  readonly level = signal(0);                // mic level 0..1 (for the glow)
  readonly error = signal<string | null>(null);

  private rec: any = null;
  private running = false;
  private paused = false;                    // recognition paused while thinking/speaking (don't hear ourselves)
  private oneShot = false;                   // push-to-talk: stop after one question
  private onCommand?: (q: string) => void;
  private silenceTimer?: ReturnType<typeof setTimeout>;
  private noSpeechTimer?: ReturnType<typeof setTimeout>;
  private audio?: { ctx: AudioContext; stream: MediaStream; raf: number };

  constructor() {
    // pick voices once they load (Chrome loads them async)
    if (this.ttsSupported) speechSynthesis.onvoiceschanged = () => this.voice = this.pickVoice();
  }

  wasWakeEnabled() { try { return localStorage.getItem(WAKE_PREF) === '1'; } catch { return false; } }

  /** Always-on wake word ("Jarvis …"). */
  setWake(on: boolean, onCommand: (q: string) => void) {
    this.onCommand = onCommand;
    try { localStorage.setItem(WAKE_PREF, on ? '1' : '0'); } catch { /* ignore */ }
    if (!this.recognitionSupported) { this.error.set('Voice needs Chrome or Edge (speech recognition is not available in this browser).'); return; }
    this.wakeEnabled.set(on);
    this.error.set(null);
    if (on) { this.oneShot = false; if (this.state() === 'idle') this.state.set('wake'); this.start(); }
    else { this.state.set('idle'); this.stopRecognition(); this.stopLevel(); }
  }

  /** Push-to-talk: skip the wake word and listen for one question now. */
  listenOnce(onCommand: (q: string) => void) {
    if (!this.recognitionSupported) { this.error.set('Voice needs Chrome or Edge.'); return; }
    this.onCommand = onCommand;
    this.oneShot = !this.wakeEnabled();
    this.stopSpeaking();
    this.beginListening('');
    this.paused = false;
    this.start();
  }

  /** Close the voice overlay (Esc / click outside): stop speaking, go back to wake mode or off. */
  dismiss() {
    this.stopSpeaking();
    clearTimeout(this.silenceTimer); clearTimeout(this.noSpeechTimer);
    this.transcript.set('');
    this.stopLevel();
    this.state.set(this.wakeEnabled() ? 'wake' : 'idle');
    this.paused = false;
    if (this.wakeEnabled()) this.start(); else this.stopRecognition();
  }

  /** The answer is being fetched: pause the mic so the TTS isn't transcribed. */
  thinking() { this.state.set('thinking'); this.pauseRecognition(); this.stopLevel(); }

  /** Speak the answer, then return to wake mode (or off). Resolves when done. */
  speak(text: string): Promise<void> {
    return new Promise(resolve => {
      const done = () => this.zone.run(() => {
        if (this.state() === 'speaking') this.dismiss();
        resolve();
      });
      if (!this.ttsSupported || !text.trim()) { done(); return; }
      speechSynthesis.cancel();
      const u = new SpeechSynthesisUtterance(text);
      u.voice = this.voice ?? this.pickVoice();
      u.lang = u.voice?.lang ?? 'en-IN';
      u.rate = 1.04; u.pitch = 0.95;
      u.onend = done; u.onerror = done;
      this.state.set('speaking');
      speechSynthesis.speak(u);
    });
  }

  stopSpeaking() { if (this.ttsSupported) speechSynthesis.cancel(); }

  // ------------------------------------------------------------------ recognition

  private start() {
    if (this.running || this.paused) return;
    if (!this.rec) this.rec = this.create();
    try { this.rec.start(); this.running = true; } catch { /* already started */ }
  }

  private create() {
    const Ctor = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    const r = new Ctor();
    r.continuous = true;
    r.interimResults = true;
    r.lang = 'en-IN';
    r.maxAlternatives = 1;
    r.onresult = (e: any) => this.zone.run(() => this.onResult(e));
    r.onerror = (e: any) => this.zone.run(() => {
      if (e.error === 'not-allowed' || e.error === 'service-not-allowed') {
        this.error.set('Microphone permission was denied. Allow the mic for this site to use voice.');
        this.wakeEnabled.set(false); this.state.set('idle'); this.paused = true;
      } else if (e.error === 'audio-capture') this.error.set('No microphone found.');
      // 'no-speech', 'aborted', 'network' → onend restarts
    });
    r.onend = () => this.zone.run(() => {
      this.running = false;
      const keepGoing = !this.paused && (this.wakeEnabled() || this.state() === 'listening');
      if (keepGoing) setTimeout(() => this.start(), 250);     // Chrome ends continuous sessions periodically
    });
    return r;
  }

  private onResult(e: any) {
    for (let i = e.resultIndex; i < e.results.length; i++) {
      const text: string = e.results[i][0].transcript;
      const final: boolean = e.results[i].isFinal;

      if (this.state() === 'wake' || this.state() === 'speaking') {
        const m = WAKE.exec(text);
        if (!m) continue;
        this.stopSpeaking();
        this.beginListening(text.slice(m.index + m[0].length));          // "Jarvis what is the BTC price" → keep the rest
        if (final && this.clean(this.transcript()).length > 2) this.submit();
        continue;
      }
      if (this.state() === 'listening') {
        const words = text.replace(WAKE, ' ');
        if (words.trim()) { this.transcript.set(this.clean(words)); this.armSilence(); clearTimeout(this.noSpeechTimer); }
        if (final && this.clean(this.transcript()).length > 2) this.submit();
      }
    }
  }

  private beginListening(initial: string) {
    this.state.set('listening');
    this.transcript.set(this.clean(initial));
    this.startLevel();
    clearTimeout(this.noSpeechTimer);
    this.noSpeechTimer = setTimeout(() => { if (this.state() === 'listening' && !this.transcript()) this.dismiss(); }, 8000);
    if (this.transcript()) this.armSilence();
  }

  /** Fallback when the browser is slow to mark the phrase final: ~1.6 s of silence ends the question. */
  private armSilence() {
    clearTimeout(this.silenceTimer);
    this.silenceTimer = setTimeout(() => { if (this.state() === 'listening' && this.clean(this.transcript()).length > 2) this.submit(); }, 1600);
  }

  private submit() {
    clearTimeout(this.silenceTimer); clearTimeout(this.noSpeechTimer);
    const q = this.clean(this.transcript());
    if (this.state() !== 'listening' || !q) return;
    this.thinking();
    if (this.oneShot) { this.oneShot = false; }
    this.onCommand?.(q);
  }

  private pauseRecognition() { this.paused = true; this.stopRecognition(); }
  private stopRecognition() { try { this.rec?.abort(); } catch { /* not running */ } this.running = false; }
  private clean(s: string) { return s.replace(/^[\s,.!?:;-]+/, '').replace(/\s+/g, ' ').trim(); }

  // ------------------------------------------------------------------ mic level for the glow

  private async startLevel() {
    if (this.audio || !navigator.mediaDevices?.getUserMedia) return;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const ctx = new AudioContext();
      const an = ctx.createAnalyser(); an.fftSize = 512;
      ctx.createMediaStreamSource(stream).connect(an);
      const buf = new Uint8Array(an.fftSize);
      const tick = () => {
        an.getByteTimeDomainData(buf);
        let sum = 0; for (const v of buf) { const d = (v - 128) / 128; sum += d * d; }
        const rms = Math.min(1, Math.sqrt(sum / buf.length) * 4);
        this.level.set(this.level() * 0.6 + rms * 0.4);                  // smoothed
        if (this.audio) this.audio.raf = requestAnimationFrame(tick);
      };
      this.audio = { ctx, stream, raf: requestAnimationFrame(tick) };
    } catch { /* level is decoration only */ }
  }

  private stopLevel() {
    if (!this.audio) return;
    cancelAnimationFrame(this.audio.raf);
    this.audio.stream.getTracks().forEach(t => t.stop());
    this.audio.ctx.close().catch(() => {});
    this.audio = undefined;
    this.level.set(0);
  }

  // ------------------------------------------------------------------ TTS voice

  private voice: SpeechSynthesisVoice | null = null;
  private pickVoice(): SpeechSynthesisVoice | null {
    if (!this.ttsSupported) return null;
    const v = speechSynthesis.getVoices();
    const pref = [/en-GB.*(Male|Ryan|George|Daniel)/i, /Google UK English Male/i, /en-IN/i, /en-GB/i, /en-US/i];
    for (const p of pref) { const hit = v.find(x => p.test(`${x.lang} ${x.name}`)); if (hit) return hit; }
    return v[0] ?? null;
  }
}

/** Markdown answer → something pleasant to hear (no symbols, short, rupees/dollars spoken naturally). */
export function speakable(md: string): string {
  let s = md.replace(/\r/g, '');
  // tables: "| a | b |" rows → "a, b." (max 5 rows), drop separator rows
  s = s.replace(/^\s*\|?\s*:?-{2,}.*$/gm, '');
  let rows = 0;
  s = s.replace(/^\s*\|(.+)\|\s*$/gm, (_, r: string) => (rows++ < 6 ? r.split('|').map(c => c.trim()).filter(Boolean).join(', ') + '.' : ''));
  s = s.replace(/`([^`]+)`/g, '$1').replace(/\*\*([^*]+)\*\*/g, '$1').replace(/\*([^*]+)\*/g, '$1').replace(/^#{1,6}\s*/gm, '')
       .replace(/^\s*([-*•]|\d+\.)\s+/gm, '').replace(/_/g, ' ');
  // a minus sign touches the currency ("-₹1,200"); a dash with spaces around it ("(last) – $74,312") is just a separator
  s = s.replace(/\s[–—-]\s/g, ', ')
       .replace(/[-−]₹\s?([\d,]+(?:\.\d+)?)/g, 'minus $1 rupees').replace(/₹\s?-?([\d,]+(?:\.\d+)?)/g, '$1 rupees')
       .replace(/[-−]\$\s?([\d,]+(?:\.\d+)?)/g, 'minus $1 dollars').replace(/\$\s?([\d,]+(?:\.\d+)?)/g, '$1 dollars')
       .replace(/(\d)\s?%/g, '$1 percent').replace(/P&L/gi, 'P and L').replace(/→/g, ' to ').replace(/[|·•]/g, ', ');
  s = s.split('\n').map(l => l.trim()).filter(Boolean).join('. ').replace(/\.\s*\.\s*/g, '. ').replace(/\s+/g, ' ').trim();
  return s.length > 700 ? s.slice(0, s.lastIndexOf('.', 700) + 1 || 700) + ' More details are on screen.' : s;
}
