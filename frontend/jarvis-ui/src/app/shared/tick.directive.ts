import { Directive, ElementRef, effect, inject, input } from '@angular/core';

/** Briefly flashes an element green/red when the bound number goes up/down (live P&L, LTP). */
@Directive({ selector: '[appTick]', standalone: true })
export class TickDirective {
  appTick = input<number | null | undefined>();
  private el = inject(ElementRef<HTMLElement>);
  private prev: number | null | undefined;
  private timer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const v = this.appTick();
      const p = this.prev;
      this.prev = v;
      if (p === undefined || v === null || v === undefined || p === null || v === p) return;
      const cls = this.el.nativeElement.classList;
      cls.remove('tick-up', 'tick-down');
      void this.el.nativeElement.offsetWidth;           // restart the animation
      cls.add(v > p ? 'tick-up' : 'tick-down');
      clearTimeout(this.timer);
      this.timer = setTimeout(() => cls.remove('tick-up', 'tick-down'), 900);
    });
  }
}
