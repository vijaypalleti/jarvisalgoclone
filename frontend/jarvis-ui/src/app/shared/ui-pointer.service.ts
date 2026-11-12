import { Injectable, NgZone, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

/** Sent by the assistant's show_on_screen tool (backend UiAction). */
export interface UiTarget { key: string; label: string; }
export interface UiAction { route: string; query: Record<string, string>; targets: UiTarget[]; focus?: Record<string, string> | null; }

const HOLD_MS = 12_000;

/**
 * "Jarvis, point at the container that's not working":
 *   1. navigate to the page (route + query, e.g. ?tab=infrastructure)
 *   2. publish the request so the page can reset its own filters / select the item (see `request`)
 *   3. wait for elements with [data-ai-target="<key>"], scroll the first into view and pulse them with a label.
 * Pages only need to put data-ai-target on the element and (optionally) react to `request`.
 */
@Injectable({ providedIn: 'root' })
export class UiPointerService {
  private router = inject(Router);
  private zone = inject(NgZone);

  /** Latest pointing request; tabs read it to clear filters that would hide the target. */
  readonly request = signal<UiAction | null>(null);
  /** true while something is highlighted (the voice overlay un-dims so you can see it). */
  readonly active = signal(false);

  private cleanup: (() => void) | null = null;
  private runId = 0;

  async run(a: UiAction): Promise<number> {
    const id = ++this.runId;
    this.clear();
    await this.router.navigate([a.route], { queryParams: a.query ?? {} });
    this.request.set({ ...a });
    const found = await this.waitFor(a.targets.map(t => t.key), id);
    if (id !== this.runId || !found.length) return found.length;
    this.zone.runOutsideAngular(() => this.highlight(a.targets, found, id));
    return found.length;
  }

  clear() {
    this.cleanup?.(); this.cleanup = null;
    this.active.set(false);
  }

  /** Data loads async after navigation; poll for up to 6 s. */
  private waitFor(keys: string[], id: number): Promise<Element[]> {
    return new Promise(resolve => {
      const t0 = performance.now();
      const look = () => {
        if (id !== this.runId) return resolve([]);
        const els = keys.map(k => document.querySelector(`[data-ai-target="${CSS.escape(k)}"]`)).filter((e): e is Element => !!e);
        if (els.length === keys.length || (els.length && performance.now() - t0 > 2500) || performance.now() - t0 > 6000) return resolve(els);
        setTimeout(look, 150);
      };
      look();
    });
  }

  private highlight(targets: UiTarget[], els: Element[], id: number) {
    // page order (top first), so we scroll to the topmost match
    els = [...els].sort((a, b) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1);
    els[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
    const labels = new Map(targets.map(t => [t.key, t.label]));
    const labelOf = (el: Element) => labels.get(el.getAttribute('data-ai-target')!) ?? '';
    const titles = new Map<Element, string | null>();
    for (const el of els) {
      el.classList.add('ai-point');
      titles.set(el, el.getAttribute('title'));
      el.setAttribute('title', 'Jarvis: ' + labelOf(el));      // every target keeps its own label on hover
    }
    // Table rows sit right under each other, so one label per row would cover the row above:
    // one summary label on the top row instead. Cards (containers, accounts) get a label each.
    const rows = els.length > 1 && els.every(e => e.tagName === 'TR');
    const tagged = rows ? [els[0]] : els;
    const tags = tagged.map((el, i) => {
      const tag = document.createElement('div');
      tag.className = 'ai-point-tag';
      tag.textContent = rows
        ? `Jarvis: ${els.length} highlighted · ${labelOf(el)} (hover a row for its details)`
        : (i === 0 && els.length > 1 ? `1 of ${els.length} · ` : '') + 'Jarvis: ' + labelOf(el);
      document.body.appendChild(tag);
      return { el, tag };
    });
    // labels follow their elements while the page scrolls / re-renders
    let raf = 0;
    const place = () => {
      for (const { el, tag } of tags) {
        const live = el.isConnected ? el : document.querySelector(`[data-ai-target="${CSS.escape(el.getAttribute('data-ai-target')!)}"]`);
        if (live && live !== el) { live.classList.add('ai-point'); (tags.find(t => t.tag === tag)!).el = live; }
        const r = (live ?? el).getBoundingClientRect();
        const visible = !!live && r.bottom > 0 && r.top < innerHeight && r.width > 0;
        tag.style.opacity = visible ? '1' : '0';
        tag.style.left = Math.max(8, Math.min(innerWidth - tag.offsetWidth - 8, r.left)) + 'px';
        tag.style.top = Math.max(8, r.top - tag.offsetHeight - 8) + 'px';
      }
      raf = requestAnimationFrame(place);
    };
    raf = requestAnimationFrame(place);
    const stop = () => {
      cancelAnimationFrame(raf); clearTimeout(timer);
      document.removeEventListener('pointerdown', onClick, true);
      for (const t of tags) t.tag.remove();
      for (const [el, title] of titles) { el.classList.remove('ai-point'); title === null ? el.removeAttribute('title') : el.setAttribute('title', title); }
      document.querySelectorAll('.ai-point').forEach(e => e.classList.remove('ai-point'));
    };
    const onClick = (e: Event) => { if ([...titles.keys()].some(el => el.contains(e.target as Node))) this.zone.run(() => this.clear()); };
    document.addEventListener('pointerdown', onClick, true);
    const timer = setTimeout(() => this.zone.run(() => { if (id === this.runId) this.clear(); }), HOLD_MS);
    this.cleanup = stop;
    this.zone.run(() => this.active.set(true));
  }
}
