import { HttpClient } from '@angular/common/http';
import { Component, ElementRef, HostBinding, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { VoiceService, speakable } from './voice.service';
import { UiAction, UiPointerService } from './ui-pointer.service';
import { IconComponent } from './icon.component';

interface ToolUse { name: string; args: string; ms: number; ok: boolean; }
interface Msg { role: 'user' | 'assistant'; content: string; html?: string; tools?: ToolUse[]; ms?: number; error?: boolean; model?: string; voice?: boolean; action?: UiAction | null; at?: string; }
interface ChatResponse { answer: string; tools: ToolUse[]; model: string; ms: number; error: string | null; action?: UiAction | null; }

const STORE_KEY = 'jarvis.assistant.chat';
const clock = () => new Date().toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' });
const TOOL_LABELS: Record<string, string> = {
  get_btc_price: 'BTC price', get_btc_change: 'BTC history', get_fyers_account: 'Fyers account', get_fyers_orders_today: 'Fyers orders',
  find_broker_account: 'Broker account', get_positions: 'Positions', get_open_issues: 'Open issues', get_portfolio_summary: 'Portfolio',
  get_signal: 'Signal', get_signal_accounts: 'Signal accounts', get_recent_signals: 'Recent signals', get_pnl_history: 'P&L history',
  get_rankings: 'Rankings', get_strategy_pnl: 'Strategy P&L', get_basket_summary: 'Baskets', get_infrastructure: 'Infrastructure',
  get_broker_rejections: 'Rejections', get_exits_and_cleanup: 'Exits & cleanup', get_market_indices: 'Market indices',
  get_today_activity: "Today's activity", get_expiring_subscriptions: 'Subscriptions', get_symbol_exposure: 'Exposure',
  get_unhedged_positions: 'Hedges', get_help: 'Documentation', show_on_screen: 'On-screen highlight',
};

/**
 * Floating AI assistant (bottom-right on every page). Questions go to /api/assistant/chat, where the model
 * (GPT-OSS 20B on Groq) calls read-only tools on the app's own data; the reply lists which tools it used.
 *
 * Voice: say "Jarvis …" (wake word, opt-in) or tap the mic. The orb glides to the bottom-centre, the screen dims,
 * a Siri-style glow rises from the bottom (driven by the mic level), and the answer is shown AND spoken.
 */
@Component({
  selector: 'app-assistant-chat',
  standalone: true,
  imports: [IconComponent],
  template: `
    <!-- ================= voice overlay ================= -->
    <div class="vo-dim" [class.on]="voiceOn() && !pointer.active()" (click)="voice.dismiss()"></div>
    <div class="vo-glow" [class.on]="voiceOn()" [class.thinking]="vstate() === 'thinking'" [class.speaking]="vstate() === 'speaking'"
         [style.--lvl]="voice.level()" aria-hidden="true"><i></i></div>
    @if (voiceOn()) {
      <div class="vo-card" role="status" aria-live="polite">
        <div class="vo-state">{{ stateLabel() }}</div>
        @if (voice.transcript() || lastQuestion()) {
          <div class="vo-q">“{{ vstate() === 'listening' ? voice.transcript() : lastQuestion() }}”</div>
        }
        @if (voiceAnswer() && vstate() !== 'listening') { <div class="vo-a md" [innerHTML]="voiceAnswer()"></div> }
        <div class="vo-hint">Esc or click anywhere to close · {{ voice.wakeEnabled() ? 'say “Jarvis” to ask again' : 'tap the mic to ask again' }}</div>
      </div>
    }

    <!-- ================= chat panel ================= -->
    @if (open() && !voiceOn()) {
      <section class="panel" [class.wide]="wide()" role="dialog" aria-label="Jarvis Assistant">
        <header>
          <span class="avatar orb">
            <picture>
              <source srcset="assistant/orb-still.png" media="(prefers-reduced-motion: reduce)" />
              <source srcset="assistant/orb.webp" type="image/webp" />
              <img src="assistant/orb.gif" alt="" draggable="false" />
            </picture>
            <i class="presence" [class.off]="!configured()"></i>
          </span>
          <div class="ttl">
            <b>Jarvis Assistant</b>
            <small>{{ configured() ? 'Online' : 'Not configured' }} · {{ model() }}</small>
          </div>
          <div class="hdr-actions">
            @if (voice.recognitionSupported) {
              <button class="ic" [class.on]="voice.wakeEnabled()" (click)="toggleWake()"
                      [attr.aria-pressed]="voice.wakeEnabled()"
                      [title]="voice.wakeEnabled() ? 'Wake word on: say “Jarvis …”. Click to turn off.' : 'Turn on the wake word “Jarvis” (the browser streams mic audio to its speech service while on)'">
                <app-icon name="audio-lines" [size]="15" />
              </button>
            }
            <button class="ic" title="New conversation" (click)="reset()" [disabled]="!msgs().length"><app-icon name="square-pen" [size]="15" /></button>
            <button class="ic hide-sm" [title]="wide() ? 'Compact view' : 'Expand'" (click)="wide.set(!wide())"><app-icon [name]="wide() ? 'minimize' : 'maximize'" [size]="15" /></button>
            <button class="ic" title="Close (Esc)" (click)="open.set(false)"><app-icon name="x" [size]="16" /></button>
          </div>
        </header>
        @if (voice.wakeEnabled()) {
          <div class="banner"><span class="live"></span> Listening for <b>“Jarvis”</b> <span class="sub">e.g. “Jarvis, what is the BTC price?”</span></div>
        }
        @if (voice.error()) { <div class="banner err"><app-icon name="alert-circle" [size]="13" /> {{ voice.error() }}</div> }

        <div class="body" #body>
          @if (msgs().length === 0) {
            <div class="welcome">
              <h3>{{ greeting() }}</h3>
              <p>Ask about positions, P&amp;L, signals, markets or platform health. Answers come from live, read-only app data{{ voice.recognitionSupported ? ', and you can type or speak' : '' }}.</p>
              @for (g of suggestionGroups; track g.title) {
                <div class="sg">
                  <div class="sg-t"><app-icon [name]="g.icon" [size]="12" /> {{ g.title }}</div>
                  @for (q of g.items; track q) {
                    <button class="sq" (click)="ask(q)"><span>{{ q }}</span><app-icon name="chevron-right" [size]="13" /></button>
                  }
                </div>
              }
            </div>
          }
          @for (m of msgs(); track $index) {
            @if (m.role === 'user') {
              <div class="row me">
                <div class="bubble">@if (m.voice) {<app-icon name="mic" [size]="12" class="vtag" />}{{ m.content }}</div>
                @if (m.at) { <span class="time">{{ m.at }}</span> }
              </div>
            } @else {
              <div class="row ai" [class.err]="m.error">
                <span class="avatar sm orb"><img src="assistant/orb-still.png" alt="" /></span>
                <div class="content">
                  <div class="answer md" [innerHTML]="m.html"></div>
                  @if (m.tools?.length) {
                    <div class="sources">
                      <span class="lbl"><app-icon name="database" [size]="11" /> Sources</span>
                      @for (t of m.tools ?? []; track $index) {
                        <span class="src" [class.bad]="!t.ok" [title]="t.name + ' ' + t.args + ' · ' + t.ms + ' ms'">{{ toolLabel(t.name) }}</span>
                      }
                    </div>
                  }
                  <div class="foot">
                    @if (m.action) {
                      <button class="act primary" (click)="point(m.action)" title="Open the page and highlight it again"><app-icon name="map-pin" [size]="12" /> Show on page</button>
                    }
                    <button class="act" (click)="copy(m, $index)" title="Copy answer"><app-icon [name]="copied() === $index ? 'check' : 'copy'" [size]="12" /> {{ copied() === $index ? 'Copied' : 'Copy' }}</button>
                    <span class="sp"></span>
                    @if (m.model && !m.model.endsWith('gpt-oss-20b')) { <span class="time" title="The primary model was busy or rate-limited; a fallback model answered">via {{ m.model.replace('openai/', '') }}</span> }
                    @if (m.at) { <span class="time">{{ m.at }}</span> }
                    @if (m.ms) { <span class="time">· {{ (m.ms / 1000).toFixed(1) }}s</span> }
                  </div>
                </div>
              </div>
            }
          }
          @if (busy()) {
            <div class="row ai">
              <span class="avatar sm orb"><img src="assistant/orb-still.png" alt="" /></span>
              <div class="thinking"><span class="dots"><i></i><i></i><i></i></span> Checking live data…</div>
            </div>
          }
        </div>

        <form class="composer" (submit)="$event.preventDefault(); ask(draft)">
          <div class="field">
            <textarea #box rows="1" placeholder="Ask Jarvis anything about your book…" [value]="draft" aria-label="Message"
                      (input)="draft = $any($event.target).value; grow($any($event.target))"
                      (keydown.enter)="onEnter($any($event))"></textarea>
            @if (voice.recognitionSupported) {
              <button type="button" class="ghost" title="Speak your question" (click)="talk()" [disabled]="busy()"><app-icon name="mic" [size]="16" /></button>
            }
            <button type="submit" class="send" [disabled]="busy() || !draft.trim()" title="Send (Enter)"><app-icon name="arrow-up" [size]="16" /></button>
          </div>
          <div class="note"><span>Read-only · live app data · verify before acting</span><span class="hide-sm">Shift+Enter for new line</span></div>
        </form>
      </section>
    }

    <!-- ================= floating orb ================= -->
    <button class="fab" [class.on]="open()" [class.wake]="voice.wakeEnabled() && !voiceOn()" [class.talking]="vstate() === 'speaking'"
            (click)="fabClick()" [title]="voiceOn() ? 'Stop' : open() ? 'Close assistant' : 'Ask Jarvis Assistant'"
            [attr.aria-label]="voiceOn() ? 'Stop voice' : open() ? 'Close assistant' : 'Open Jarvis Assistant'">
      <!-- animated orb (WebP 615 KB; GIF fallback; still frame when the OS asks for reduced motion) -->
      <picture>
        <source srcset="assistant/orb-still.png" media="(prefers-reduced-motion: reduce)" />
        <source srcset="assistant/orb.webp" type="image/webp" />
        <img src="assistant/orb.gif" alt="" draggable="false" />
      </picture>
      @if (open() && !voiceOn()) { <span class="x">✕</span> }
    </button>
  `,
  styles: [`
    :host { position: fixed; right: 22px; bottom: 22px; z-index: 900; font-size: 13px; }

    /* ---------------- floating orb (glides to bottom-centre in voice mode) ---------------- */
    .fab {
      position: relative; z-index: 4; width: 58px; height: 58px; padding: 0; border-radius: 50%; border: 0; cursor: pointer; overflow: hidden;
      background: #0b0820; box-shadow: 0 10px 28px rgba(124, 58, 237, .45), 0 0 0 1px rgba(167, 139, 250, .35);
      transition: transform .75s cubic-bezier(.22, 1, .36, 1), box-shadow .3s ease;
      &:hover { box-shadow: 0 12px 32px rgba(56, 189, 248, .5), 0 0 0 1px rgba(125, 211, 252, .5); }
      &:focus-visible { outline: 2px solid #38bdf8; outline-offset: 3px; }
      picture, img { display: block; width: 100%; height: 100%; object-fit: cover; pointer-events: none; }
      .x { position: absolute; inset: 0; display: grid; place-items: center; color: #fff; font-size: 18px;
           background: rgba(11, 8, 32, .45); text-shadow: 0 1px 4px rgba(0, 0, 0, .6); }
      /* wake word on: a soft breathing ring tells you the mic is listening for "Jarvis" */
      &.wake { box-shadow: 0 10px 28px rgba(124, 58, 237, .45), 0 0 0 2px rgba(34, 197, 94, .85); animation: breathe 2.6s ease-in-out infinite; }
    }
    @keyframes breathe { 50% { box-shadow: 0 10px 28px rgba(124, 58, 237, .45), 0 0 0 5px rgba(34, 197, 94, .25); } }
    /* centre of the orb is at (100vw - 22px - 29px) → move it to 50vw, lift it, and enlarge it */
    :host(.voice) .fab {
      transform: translate(calc(-50vw + 51px), -64px) scale(1.75);
      box-shadow: 0 0 40px rgba(168, 85, 247, .65), 0 0 90px rgba(56, 189, 248, .35);
    }
    :host(.voice) .fab.talking picture { animation: talk .9s ease-in-out infinite; }
    @keyframes talk { 50% { transform: scale(1.06); } }

    /* ---------------- dim + Siri-style glow + caption ---------------- */
    .meta .point { cursor: pointer; border: 0; font: inherit; }
    .vo-dim {
      position: fixed; inset: 0; z-index: 1; background: rgba(2, 6, 23, .62); backdrop-filter: blur(3px);
      opacity: 0; pointer-events: none; transition: opacity .45s ease;
      &.on { opacity: 1; pointer-events: auto; }
    }
    .vo-glow {
      --lvl: 0;
      position: fixed; left: -15vw; right: -15vw; bottom: -90px; z-index: 2; pointer-events: none;
      height: calc(170px + var(--lvl) * 220px);
      border-radius: 50% 50% 0 0 / 100% 100% 0 0;
      background: linear-gradient(90deg, #ff2d95, #a855f7, #22d3ee, #3b82f6, #f472b6, #8b5cf6, #ff2d95);
      background-size: 300% 100%;
      filter: blur(42px) saturate(1.35);
      opacity: 0; transform: translateY(120px);
      transition: opacity .55s ease, transform .75s cubic-bezier(.22, 1, .36, 1), height .12s linear;
      animation: flow 7s linear infinite;
      /* bright thin edge, like Siri's bottom light */
      i { position: absolute; left: 12%; right: 12%; top: 34%; height: 6px; border-radius: 6px;
          background: linear-gradient(90deg, transparent, #fff, #f0abfc, #a5f3fc, #fff, transparent); filter: blur(6px); opacity: .85; }
      &.on { opacity: .95; transform: none; }
      &.thinking { animation-duration: 1.8s; }
      &.speaking { animation: flow 3s linear infinite, swell 1.1s ease-in-out infinite; }
    }
    @keyframes flow { to { background-position: 300% 0; } }
    @keyframes swell { 50% { height: 260px; opacity: 1; } }

    .vo-card {
      position: fixed; z-index: 3; left: 50%; bottom: 190px; transform: translateX(-50%);
      width: min(680px, calc(100vw - 32px)); max-height: 46vh; overflow-y: auto; text-align: center; color: #e2e8f0;
      animation: rise .45s cubic-bezier(.22, 1, .36, 1);
      .vo-state { font-size: 11px; letter-spacing: .18em; text-transform: uppercase; color: #a5b4fc; margin-bottom: 8px; }
      .vo-q { font-size: 22px; font-weight: 300; color: #fff; line-height: 1.35; margin-bottom: 14px; text-shadow: 0 2px 18px rgba(0,0,0,.5); }
      .vo-a { text-align: left; margin: 0 auto; padding: 14px 18px; border-radius: 16px; font-size: 14px; line-height: 1.55;
              background: rgba(15, 23, 42, .55); border: 1px solid rgba(255, 255, 255, .12); backdrop-filter: blur(10px);
              box-shadow: 0 20px 50px rgba(0, 0, 0, .35); color: #f1f5f9; }
      .vo-hint { margin-top: 10px; font-size: 11px; color: #94a3b8; }
    }
    @keyframes rise { from { opacity: 0; transform: translate(-50%, 14px); } }
    .vo-card .md, .panel .md { white-space: normal; }
    /* answer HTML comes from [innerHTML], so these rules must pierce the component's style scoping */
    .vo-card .md ::ng-deep, .panel .md ::ng-deep {
      :is(p, ul, ol) { margin: 0 0 6px; } :is(p, ul, ol):last-child { margin-bottom: 0; } ul, ol { padding-left: 18px; }
      code { font-family: var(--mono); font-size: 11.5px; background: rgba(148, 163, 184, .15); padding: 0 4px; border-radius: 4px; }
      table { border-collapse: collapse; font-size: 12px; margin: 4px 0; }
      th, td { border: 1px solid rgba(148, 163, 184, .35); padding: 3px 7px; text-align: left; } }

    /* ---------------- chat panel ---------------- */
    .panel {
      --brand: var(--teal-dark, #0e9aa5); --brand-ink: #fff;
      position: absolute; right: 0; bottom: 70px; width: min(420px, calc(100vw - 32px)); height: min(640px, calc(100vh - 110px));
      display: flex; flex-direction: column; background: var(--surface); color: var(--text); font-family: var(--sans);
      border: 1px solid var(--border); border-radius: 14px; overflow: hidden;
      box-shadow: 0 1px 2px rgba(15, 23, 42, .06), 0 24px 64px -12px rgba(15, 23, 42, .32);
      animation: pop .18s cubic-bezier(.22, 1, .36, 1); transition: width .25s ease, height .25s ease;
      &.wide { width: min(760px, calc(100vw - 32px)); height: calc(100vh - 110px); }
    }
    @keyframes pop { from { opacity: 0; transform: translateY(10px) scale(.985); } }

    header {
      display: flex; align-items: center; gap: 10px; padding: 12px 10px 12px 14px; color: #f8fafc;
      background: var(--header-bg, #0d2219); border-bottom: 1px solid rgba(255, 255, 255, .06);
      .ttl { flex: 1; min-width: 0; display: flex; flex-direction: column; line-height: 1.25;
        b { font-size: 14px; font-weight: 600; letter-spacing: .01em; }
        small { font-size: 11px; color: rgba(226, 232, 240, .66); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } }
      .hdr-actions { display: flex; gap: 2px; }
      .ic { display: grid; place-items: center; width: 30px; height: 30px; border: 0; border-radius: 8px; cursor: pointer;
        color: rgba(226, 232, 240, .78); background: transparent; transition: background .15s, color .15s;
        &:hover:not(:disabled) { background: rgba(255, 255, 255, .1); color: #fff; }
        &:disabled { opacity: .35; cursor: default; }
        &.on { background: rgba(34, 197, 94, .18); color: #4ade80; }
        &:focus-visible { outline: 2px solid var(--teal, #14b8c4); outline-offset: 1px; } }
    }
    .avatar { position: relative; flex: none; width: 34px; height: 34px; border-radius: 50%; background: #0b0820;
      box-shadow: 0 0 0 1px rgba(255, 255, 255, .18);
      picture, img { display: block; width: 100%; height: 100%; object-fit: cover; border-radius: 50%; }
      .presence { position: absolute; right: -1px; bottom: -1px; width: 10px; height: 10px; border-radius: 50%;
        background: #22c55e; box-shadow: 0 0 0 2px #0d2219; &.off { background: #f59e0b; } }
      &.sm { width: 24px; height: 24px; margin-top: 2px; box-shadow: 0 0 0 1px var(--border); } }

    .banner { display: flex; align-items: center; gap: 6px; padding: 7px 14px; font-size: 11.5px;
      color: var(--text-2); background: var(--surface-2); border-bottom: 1px solid var(--border);
      .sub { color: var(--muted); margin-left: auto; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      .live { width: 7px; height: 7px; border-radius: 50%; background: #22c55e; animation: pulse 1.6s infinite; }
      &.err { color: #b91c1c; background: #fef2f2; border-color: #fecaca; } }

    .body { flex: 1; overflow-y: auto; padding: 16px 14px 8px; display: flex; flex-direction: column; gap: 14px; scroll-behavior: smooth; }
    .panel.wide .body { padding: 20px 28px 8px; }

    /* welcome + grouped suggestions */
    .welcome { h3 { margin: 2px 0 4px; font-size: 17px; font-weight: 600; letter-spacing: -.01em; }
      > p { margin: 0 0 14px; color: var(--text-2); font-size: 12.5px; line-height: 1.5; } }
    .panel.wide .welcome { display: grid; grid-template-columns: 1fr 1fr; gap: 0 18px; h3, > p { grid-column: 1 / -1; } }
    .sg { margin-bottom: 12px; }
    .sg-t { display: flex; align-items: center; gap: 5px; margin: 0 0 6px; font-size: 10.5px; font-weight: 600; letter-spacing: .07em;
      text-transform: uppercase; color: var(--muted); }
    .sq { display: flex; align-items: center; justify-content: space-between; gap: 8px; width: 100%; margin-bottom: 4px; padding: 8px 10px;
      border: 1px solid var(--border); border-radius: 9px; background: var(--surface); color: var(--text); font: inherit; font-size: 12.5px;
      text-align: left; cursor: pointer; transition: border-color .15s, background .15s;
      app-icon { color: var(--muted); flex: none; }
      &:hover { border-color: var(--brand); background: var(--surface-2); app-icon { color: var(--brand); } } }

    /* messages */
    .row { display: flex; gap: 8px; max-width: 100%; }
    .row.me { flex-direction: column; align-items: flex-end; align-self: flex-end; max-width: 86%;
      .bubble { background: var(--brand); color: var(--brand-ink); padding: 8px 12px; border-radius: 14px 14px 4px 14px;
        line-height: 1.5; white-space: pre-wrap; word-break: break-word; }
      .vtag { margin-right: 5px; vertical-align: -1px; opacity: .85; } }
    .row.ai { align-items: flex-start; }
    .content { flex: 1; min-width: 0; }
    .answer { padding: 10px 12px; border: 1px solid var(--border); border-radius: 4px 14px 14px 14px; background: var(--surface-2);
      line-height: 1.55; word-break: break-word; }
    .row.err .answer { border-color: #fecaca; background: #fef2f2; color: #991b1b; }
    .panel .md ::ng-deep {
      table { width: 100%; display: block; overflow-x: auto; border-collapse: collapse; font-size: 12px; font-variant-numeric: tabular-nums; }
      th, td { border: 0; border-bottom: 1px solid var(--border); padding: 4px 8px; }
      th { background: transparent; color: var(--text-2); font-weight: 600; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; }
      strong { font-weight: 600; } }
    .sources { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; margin-top: 6px;
      .lbl { display: inline-flex; align-items: center; gap: 4px; font-size: 10.5px; color: var(--muted); margin-right: 2px; }
      .src { font-size: 10.5px; padding: 1px 7px; border-radius: 999px; background: var(--surface-2); border: 1px solid var(--border);
        color: var(--text-2); cursor: help; &.bad { color: #b91c1c; border-color: #fca5a5; } } }
    .foot { display: flex; align-items: center; gap: 4px; margin-top: 4px; min-height: 22px;
      .sp { flex: 1; }
      .act { display: inline-flex; align-items: center; gap: 4px; height: 22px; padding: 0 7px; border: 1px solid transparent; border-radius: 6px;
        background: transparent; color: var(--muted); font: inherit; font-size: 11px; cursor: pointer;
        &:hover { color: var(--text); background: var(--surface-2); border-color: var(--border); }
        &.primary { color: var(--brand); border-color: color-mix(in srgb, var(--brand) 35%, transparent);
          &:hover { background: color-mix(in srgb, var(--brand) 10%, transparent); } } } }
    .row.ai .foot .act:not(.primary) { opacity: 0; transition: opacity .15s; }
    .row.ai:hover .foot .act, .row.ai:focus-within .foot .act { opacity: 1; }
    .time { font-size: 10.5px; color: var(--muted); font-variant-numeric: tabular-nums; }
    .row.me .time { margin-top: 3px; }
    .thinking { display: inline-flex; align-items: center; gap: 8px; padding: 8px 12px; border: 1px solid var(--border);
      border-radius: 4px 14px 14px 14px; background: var(--surface-2); color: var(--text-2); font-size: 12px; }
    .dots { display: inline-flex; gap: 3px; i { width: 5px; height: 5px; border-radius: 50%; background: var(--brand); animation: blink 1s infinite; }
      i:nth-child(2) { animation-delay: .15s; } i:nth-child(3) { animation-delay: .3s; } }
    @keyframes blink { 50% { opacity: .25; } }

    /* composer */
    .composer { padding: 10px 12px 10px; border-top: 1px solid var(--border); background: var(--surface); }
    .field { display: flex; align-items: flex-end; gap: 4px; padding: 5px 5px 5px 12px; border: 1px solid var(--border-strong, var(--border));
      border-radius: 12px; background: var(--surface); transition: border-color .15s, box-shadow .15s;
      &:focus-within { border-color: var(--brand); box-shadow: 0 0 0 3px color-mix(in srgb, var(--brand) 18%, transparent); }
      textarea { flex: 1; align-self: center; resize: none; max-height: 132px; min-height: 22px; padding: 5px 0; border: 0; outline: none;
        background: transparent; color: var(--text); font: inherit; font-size: 13px; line-height: 1.45; }
      button { flex: none; display: grid; place-items: center; width: 32px; height: 32px; border: 0; border-radius: 9px; cursor: pointer;
        &:disabled { cursor: default; } }
      .ghost { background: transparent; color: var(--text-2); &:hover:not(:disabled) { background: var(--surface-2); color: var(--brand); } &:disabled { opacity: .4; } }
      .send { background: var(--brand); color: #fff; &:disabled { background: var(--border); color: var(--muted); } } }
    .note { display: flex; justify-content: space-between; gap: 8px; margin-top: 6px; padding: 0 2px; font-size: 10.5px; color: var(--muted); white-space: nowrap;
      span:first-child { overflow: hidden; text-overflow: ellipsis; } }
    @media (max-width: 520px) { .hide-sm { display: none !important; } }

    @media (prefers-reduced-motion: reduce) {
      .panel, .dots i, .banner .live, .vo-card, .fab.wake, :host(.voice) .fab.talking picture { animation: none; }
      .vo-glow { animation: none; transition: opacity .3s; }
      .fab { transition: none; }
    }
  `],
})
export class AssistantChatComponent {
  private http = inject(HttpClient);
  private router = inject(Router);
  readonly voice = inject(VoiceService);
  readonly pointer = inject(UiPointerService);
  @ViewChild('body') body?: ElementRef<HTMLElement>;
  @ViewChild('box') box?: ElementRef<HTMLTextAreaElement>;

  open = signal(false);
  busy = signal(false);
  msgs = signal<Msg[]>(this.restore());
  draft = '';

  // voice overlay
  vstate = this.voice.state;
  voiceOn = computed(() => ['listening', 'thinking', 'speaking'].includes(this.vstate()) || this.lingering());
  lingering = signal(false);                       // keep the answer on screen briefly after speaking
  lastQuestion = signal('');
  voiceAnswer = signal('');
  stateLabel = computed(() => ({ listening: 'Listening…', thinking: 'Thinking…', speaking: 'Jarvis', wake: 'Jarvis', idle: 'Jarvis' }[this.vstate()]));
  @HostBinding('class.voice') get voiceClass() { return this.voiceOn(); }

  model = signal('gpt-oss-20b');
  configured = signal(true);
  wide = signal(false);
  copied = signal<number | null>(null);

  suggestionGroups = [
    { title: 'Markets', icon: 'trending-up', items: ['What is the current BTC price?', 'Where are NIFTY, BANK NIFTY and SENSEX right now?', "What is Vijay's current P&L?"] },
    { title: 'Accounts', icon: 'users', items: ['What is the status of broker account 1023?', 'How many positions are open in 1023?', 'Top 5 losing accounts'] },
    { title: 'Operations', icon: 'alert-triangle', items: ['How many issues need solving right now?', 'Why are orders getting rejected on ANT?', 'Which short positions are not hedged?'] },
    { title: 'Platform', icon: 'wrench', items: ['Point at the container that is not working', 'Is the data pipeline healthy?', 'What does exit partial mean?'] },
  ];

  greeting() {
    const h = new Date().getHours();
    return `${h < 12 ? 'Good morning' : h < 17 ? 'Good afternoon' : 'Good evening'}. How can I help?`;
  }

  /** Friendly names for the "Sources" row (tool → what it reads). */
  toolLabel(name: string) { return TOOL_LABELS[name] ?? name.replace(/^get_/, '').replace(/_/g, ' '); }

  copy(m: Msg, i: number) {
    navigator.clipboard?.writeText(m.content).then(() => { this.copied.set(i); setTimeout(() => this.copied.set(null), 1500); }, () => {});
  }

  /** Auto-grow the composer up to its max-height. */
  grow(t: HTMLTextAreaElement) { t.style.height = 'auto'; t.style.height = Math.min(t.scrollHeight, 132) + 'px'; }

  constructor() {
    // re-arm the wake word if it was on last time (the browser remembers the mic permission for this site)
    if (this.voice.recognitionSupported && this.voice.wasWakeEnabled()) this.voice.setWake(true, q => this.askByVoice(q));
  }

  toggle() {
    this.open.update(v => !v);
    if (this.open()) {
      this.http.get<{ configured: boolean; model: string }>('/api/assistant/status').subscribe({
        next: s => { this.model.set(s.model.replace('openai/', '')); this.configured.set(s.configured); },
        error: () => {},
      });
      this.scroll();
      setTimeout(() => this.box?.nativeElement.focus(), 50);
    }
  }

  fabClick() { if (this.voiceOn()) this.closeVoice(); else this.toggle(); }
  toggleWake() { this.voice.setWake(!this.voice.wakeEnabled(), q => this.askByVoice(q)); }
  talk() { this.voiceAnswer.set(''); this.lastQuestion.set(''); this.voice.listenOnce(q => this.askByVoice(q)); }

  @HostListener('document:keydown.escape') onEsc() { if (this.voiceOn()) this.closeVoice(); }
  closeVoice() { this.lingering.set(false); this.voice.dismiss(); }

  onEnter(e: KeyboardEvent) { if (e.shiftKey) return; e.preventDefault(); this.ask(this.draft); }
  reset() { this.msgs.set([]); this.save(); }

  ask(text: string) { this.send(text, false); }

  /** Open the page and highlight what the assistant pointed at (UiPointerService). */
  point(a: UiAction) { this.pointer.run(a); }

  /** Spoken question: show it big, get the answer, show it in the overlay and read it aloud. */
  private askByVoice(q: string) {
    this.lastQuestion.set(q);
    this.voiceAnswer.set('');
    this.lingering.set(false);
    this.send(q, true, async (answerMd, isError) => {
      this.voiceAnswer.set(isError ? esc(answerMd) : markdown(answerMd));
      this.lingering.set(true);                                     // keep the card up while/after speaking
      await this.voice.speak(isError ? answerMd : speakable(answerMd));
      setTimeout(() => { if (this.vstate() !== 'listening') this.lingering.set(false); }, 4000);
    });
  }

  private send(text: string, viaVoice: boolean, onAnswer?: (answer: string, isError: boolean) => void) {
    const q = text.trim();
    if (!q || this.busy()) return;
    this.draft = '';
    if (this.box) this.box.nativeElement.style.height = '';
    this.msgs.update(m => [...m, { role: 'user', content: q, voice: viaVoice, at: clock() }]);
    this.busy.set(true);
    this.scroll();
    const history = this.msgs().filter(m => !m.error).map(m => ({ role: m.role, content: m.content }));
    this.http.post<ChatResponse>('/api/assistant/chat', { messages: history, page: this.router.url }).subscribe({
      next: r => {
        if (r.error) { this.push({ role: 'assistant', content: r.error, html: esc(r.error), error: true, tools: r.tools, ms: r.ms }); onAnswer?.(r.error, true); }
        else {
          this.push({ role: 'assistant', content: r.answer, html: markdown(r.answer), tools: r.tools, ms: r.ms, model: r.model, action: r.action });
          if (r.action?.targets?.length) this.point(r.action);          // "show me the container that's down" → open + highlight
          onAnswer?.(r.answer, false);
        }
      },
      error: e => {
        const msg = e?.error?.error ?? e?.message ?? 'Request failed';
        this.push({ role: 'assistant', content: 'Request failed', html: esc(msg), error: true });
        onAnswer?.('Sorry, the request failed.', true);
      },
    });
  }

  private push(m: Msg) { m.at ??= clock(); this.busy.set(false); this.msgs.update(list => [...list, m].slice(-40)); this.save(); this.scroll(); }
  private scroll() { setTimeout(() => { const b = this.body?.nativeElement; if (b) b.scrollTop = b.scrollHeight; }, 0); }
  private save() { try { sessionStorage.setItem(STORE_KEY, JSON.stringify(this.msgs())); } catch { /* storage unavailable */ } }
  private restore(): Msg[] { try { return JSON.parse(sessionStorage.getItem(STORE_KEY) ?? '[]'); } catch { return []; } }
}

// ------------------------------------------------------------------ tiny, safe markdown (escape first, then format)

function esc(s: string) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
function inline(s: string) {
  return s.replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>').replace(/(^|[^*])\*([^*\n]+)\*/g, '$1<i>$2</i>');
}
export function markdown(src: string): string {
  const lines = esc(src.replace(/\r/g, '')).split('\n');
  const out: string[] = [];
  let i = 0;
  while (i < lines.length) {
    const l = lines[i];
    if (/^\s*\|.*\|\s*$/.test(l) && i + 1 < lines.length && /^\s*\|?\s*:?-{2,}/.test(lines[i + 1])) {      // table
      const row = (r: string) => r.trim().replace(/^\||\|$/g, '').split('|').map(c => inline(c.trim()));
      const head = row(l); i += 2;
      const body: string[][] = [];
      while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) body.push(row(lines[i++]));
      out.push(`<table><tr>${head.map(h => `<th>${h}</th>`).join('')}</tr>${body.map(r => `<tr>${r.map(c => `<td>${c}</td>`).join('')}</tr>`).join('')}</table>`);
      continue;
    }
    if (/^\s*([-*•]|\d+\.)\s+/.test(l)) {                                                                   // list
      const ordered = /^\s*\d+\./.test(l);
      const items: string[] = [];
      while (i < lines.length && /^\s*([-*•]|\d+\.)\s+/.test(lines[i])) items.push(inline(lines[i++].replace(/^\s*([-*•]|\d+\.)\s+/, '')));
      out.push(`<${ordered ? 'ol' : 'ul'}>${items.map(x => `<li>${x}</li>`).join('')}</${ordered ? 'ol' : 'ul'}>`);
      continue;
    }
    if (l.trim() === '') { i++; continue; }
    const para: string[] = [];
    while (i < lines.length && lines[i].trim() !== '' && !/^\s*([-*•]|\d+\.)\s+/.test(lines[i]) && !/^\s*\|.*\|\s*$/.test(lines[i]))
      para.push(inline(lines[i++].replace(/^#{1,6}\s*/, '')));
    out.push(`<p>${para.join('<br>')}</p>`);
  }
  return out.join('');
}
