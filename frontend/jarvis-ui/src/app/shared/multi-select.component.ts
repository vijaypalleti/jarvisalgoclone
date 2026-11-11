import { Component, ElementRef, HostListener, computed, effect, inject, input, output, signal } from '@angular/core';

export interface MsOption { value: string; label: string; hint?: string; }

/**
 * Searchable multi-select dropdown (like the production Strategy filter):
 * trigger shows "All …" / the single name / "N … selected" + a total-count badge; the panel has a search box,
 * checkbox rows with a right-aligned hint (e.g. STRAT code), and Clear / Apply. Changes apply only on Apply.
 */
@Component({
  selector: 'app-multi-select',
  standalone: true,
  template: `
    <button type="button" class="trigger" [class.open]="open()" [class.has]="selected().length > 0" (click)="toggle()">
      <span class="txt">{{ summary() }}</span>
      <span class="cnt">{{ options().length }}</span>
      <span class="caret">{{ open() ? '▴' : '▾' }}</span>
    </button>
    @if (open()) {
      <div class="panel" (keydown.escape)="open.set(false)">
        <input class="search" [placeholder]="'Search ' + options().length + ' ' + noun() + '…'" [value]="query()"
               (input)="query.set($any($event.target).value)" #q />
        @if (draft().size > 0) {
          <div class="bar">{{ draft().size }} selected
            <button type="button" class="lnk" (click)="selectVisible(false)">Unselect shown</button>
          </div>
        } @else if (query()) {
          <div class="bar"><button type="button" class="lnk" (click)="selectVisible(true)">Select all {{ filtered().length }} shown</button></div>
        }
        <div class="list">
          @for (o of filtered(); track o.value) {
            <label class="row" [class.on]="draft().has(o.value)">
              <input type="checkbox" [checked]="draft().has(o.value)" (change)="flip(o.value)" />
              <span class="lbl">{{ o.label }}</span>
              @if (o.hint && o.hint !== o.label) { <span class="hint">{{ o.hint }}</span> }
            </label>
          } @empty { <div class="none">No {{ noun() }} match "{{ query() }}"</div> }
        </div>
        <div class="foot">
          <button type="button" class="clear" (click)="clearDraft()">Clear</button>
          <button type="button" class="apply" (click)="apply()">Apply</button>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { position: relative; display: inline-block; }
    .trigger {
      display: inline-flex; align-items: center; gap: 8px; height: 34px; min-width: 210px; padding: 0 10px 0 12px;
      border: 1px solid var(--border); border-radius: 8px; background: var(--surface); color: var(--text); font-size: 12.5px; cursor: pointer;
      .txt { flex: 1; text-align: left; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 190px; }
      .cnt { background: var(--cyan-bg, #ecfeff); color: #0e7490; font-size: 10px; font-weight: 700; padding: 1px 7px; border-radius: 999px; }
      .caret { color: var(--muted); font-size: 10px; }
      &.has .txt { font-weight: 600; }
      &.open, &:focus-visible { border-color: #0891b2; box-shadow: 0 0 0 3px rgba(8, 145, 178, .12); outline: none; }
    }
    .panel {
      position: absolute; top: calc(100% + 6px); left: 0; z-index: 60; width: 340px;
      background: var(--surface); border: 1px solid var(--border); border-radius: 12px; box-shadow: 0 16px 40px rgba(15, 23, 42, .16);
      padding: 10px 0 0; display: flex; flex-direction: column;
    }
    .search { margin: 0 10px 8px; height: 34px; padding: 0 12px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface-2); font-size: 12.5px; color: var(--text); outline: none;
      &:focus { border-color: #0891b2; } }
    .bar { display: flex; justify-content: space-between; align-items: center; padding: 0 14px 6px; font-size: 11px; color: var(--text-2); }
    .lnk { border: 0; background: none; color: #0e7490; font-size: 11px; cursor: pointer; padding: 0; &:hover { text-decoration: underline; } }
    .list { max-height: 280px; overflow-y: auto; padding: 0 6px; border-top: 1px solid var(--border); padding-top: 6px; }
    .row {
      display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: 8px; cursor: pointer; font-size: 13px;
      &:hover { background: var(--surface-2); }
      &.on { background: #ecfeff; .lbl { color: #0e7490; font-weight: 600; } .hint { color: #0e7490; font-weight: 600; } }
      input { width: 16px; height: 16px; accent-color: #0891b2; margin: 0; flex-shrink: 0; }
      .lbl { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .hint { font-size: 11px; color: var(--muted); font-family: var(--mono); }
    }
    .none { padding: 16px; text-align: center; color: var(--muted); font-size: 12px; }
    .foot { display: flex; justify-content: space-between; align-items: center; gap: 10px; padding: 10px; border-top: 1px solid var(--border); margin-top: 6px; }
    .clear { border: 0; background: none; color: var(--text-2); font-size: 12.5px; font-weight: 600; padding: 8px 12px; cursor: pointer; border-radius: 8px; &:hover { background: var(--surface-2); } }
    .apply { flex: 0 0 165px; height: 38px; border: 0; border-radius: 10px; color: #fff; font-weight: 700; font-size: 13px; cursor: pointer;
      background: linear-gradient(90deg, #06b6d4, #14b8a6); box-shadow: 0 6px 14px rgba(13, 148, 136, .25); &:hover { filter: brightness(1.05); } }
    :host-context([data-theme='dark']) .row.on { background: #083344; }
  `],
})
export class MultiSelectComponent {
  private host: ElementRef<HTMLElement> = inject(ElementRef);

  options = input<MsOption[]>([]);
  selected = input<string[]>([]);
  /** plural noun for texts, e.g. "strategies" */
  noun = input('items');
  allLabel = input('All');
  applied = output<string[]>();

  open = signal(false);
  query = signal('');
  draft = signal<Set<string>>(new Set());

  filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    return q ? this.options().filter(o => (o.label + ' ' + (o.hint ?? '')).toLowerCase().includes(q)) : this.options();
  });
  summary = computed(() => {
    const s = this.selected();
    if (s.length === 0) return this.allLabel();
    if (s.length === 1) return this.options().find(o => o.value === s[0])?.label ?? s[0];
    return `${s.length} ${this.noun()} selected`;
  });

  constructor() {
    // focus the search box when the panel opens
    effect(() => { if (this.open()) queueMicrotask(() => this.host.nativeElement.querySelector<HTMLInputElement>('.search')?.focus()); });
  }

  toggle() {
    if (!this.open()) { this.draft.set(new Set(this.selected())); this.query.set(''); }
    this.open.update(v => !v);
  }
  clearDraft() { this.draft.set(new Set()); }
  flip(v: string) { this.draft.update(s => { const n = new Set(s); n.has(v) ? n.delete(v) : n.add(v); return n; }); }
  selectVisible(on: boolean) {
    this.draft.update(s => { const n = new Set(s); for (const o of this.filtered()) on ? n.add(o.value) : n.delete(o.value); return n; });
  }
  apply() {
    // keep catalogue order
    const d = this.draft();
    this.applied.emit(this.options().filter(o => d.has(o.value)).map(o => o.value));
    this.open.set(false);
  }

  @HostListener('document:mousedown', ['$event'])
  onOutside(e: MouseEvent) { if (this.open() && !this.host.nativeElement.contains(e.target as Node)) this.open.set(false); }
}
