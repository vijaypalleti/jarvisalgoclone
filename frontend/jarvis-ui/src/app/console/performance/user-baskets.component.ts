import { Component, computed, inject, signal } from '@angular/core';
import { IconComponent } from '../../shared/icon.component';
import { UserSubscriptionRow } from '../../core/ops.models';
import { UserWorkspaceService, dispatchQty, plural } from './user-workspace.service';

/**
 * Performance → Basket & Strategy (with a user selected): what is CONFIGURED per broker account. A grid of baskets × the
 * user's broker accounts; each cell is that account's subscription (state, multiplier, pause / activate / cancel) or "Assign".
 */
@Component({
  selector: 'app-user-baskets',
  standalone: true,
  imports: [IconComponent],
  template: `
    @if (s.ws()) {
      <div class="banner">
        <p><b>Client-scoped.</b> Assignments, per-account multipliers and subscription state are edited here. Basket composition, lot sizes,
          categories and instruments are <b>shared by every subscriber</b> and are managed on the global screen.</p>
        <button class="btn sm" (click)="s.demoOnly('Global Basket Management')">↗ Global Basket Management</button>
      </div>

      <section class="card mx">
        <header>
          <span>🧺</span><h3>Baskets × broker accounts</h3>
          <small>{{ plural(accounts().length, 'account') }} in scope · {{ rows().length }} basket(s) held</small>
          <span class="sp"></span>
          <button class="btn sm" (click)="s.addSub()" [disabled]="s.busy() || !accounts().length">＋ Assign basket</button>
          <button class="btn sm" (click)="s.refresh()"><app-icon name="refresh" [size]="12" /> Refresh</button>
        </header>
        @if (!accounts().length) {
          <div class="empty">This user has no broker accounts, so no basket can be assigned.</div>
        } @else {
          <div class="table-wrap"><table>
            <thead><tr>
              <th>Basket</th>
              @for (a of accounts(); track a.clientId) { <th class="acc"><small>{{ a.broker }}</small><span class="mono">{{ a.accountId }}</span></th> }
            </tr></thead>
            <tbody>
              @for (r of rows(); track r.basket) {
                <tr class="brow" (click)="toggle(r.basket)">
                  <td>
                    <span class="chev" [class.open]="expanded().has(r.basket)">▸</span>
                    <b>{{ r.basket }}</b>
                    <small class="sub">{{ r.held }}/{{ accounts().length }} account(s) · {{ plural(r.strategies.length, 'strategy') }}</small>
                  </td>
                  @for (a of accounts(); track a.clientId) {
                    @let c = cell(r.basket, a.clientId);
                    <td class="cell" [class.on]="c?.status === 'Active'" (click)="$event.stopPropagation()">
                      @if (c) {
                        <span class="pill" [class.green]="c.status === 'Active'" [class.amber]="c.status === 'Paused'" [class.gray]="c.status === 'Inactive'" [class.red]="c.status === 'Expired'">{{ c.status }}</span>
                        <div class="mrow"><b>{{ c.multiplier }}×</b><button class="ib" title="Edit multiplier" (click)="s.editSub(c)" [disabled]="s.busy()">✎</button></div>
                        <div class="mrow">
                          @if (c.status === 'Active') { <button class="ib" title="Pause" (click)="s.subAction(c, 'pause')" [disabled]="s.busy()">⏸</button> }
                          @else { <button class="ib ok" title="Activate" (click)="s.subAction(c, 'activate')" [disabled]="s.busy()">▶</button> }
                          <button class="ib bad" title="Cancel" (click)="s.subAction(c, 'cancel')" [disabled]="s.busy()">✕</button>
                        </div>
                      } @else {
                        <span class="muted">Not assigned</span>
                        <button class="ib assign" (click)="s.addSub(r.basket, a.clientId)" [disabled]="s.busy()">＋ Assign</button>
                      }
                    </td>
                  }
                </tr>
                @if (expanded().has(r.basket)) {
                  <tr class="xrow"><td [attr.colspan]="accounts().length + 1">
                    @if (r.strategies.length) {
                      <table class="inner">
                        <thead><tr><th>Strategy</th><th class="n">Lot</th><th class="n">Mult</th><th class="n">CE</th><th class="n">PE</th><th>Product</th>
                          @for (a of accounts(); track a.clientId) { <th class="n">Dispatch · {{ a.accountId }}</th> }</tr></thead>
                        <tbody>
                          @for (st of r.strategies; track st.strategy) {
                            <tr>
                              <td>{{ st.strategy }} <small class="code">{{ st.code }}</small></td>
                              <td class="n mono">{{ st.lotSize }}</td><td class="n mono">{{ st.mult }}</td><td class="n mono">{{ st.ce }}</td><td class="n mono">{{ st.pe }}</td><td>{{ st.product }}</td>
                              @for (a of accounts(); track a.clientId) {
                                @let c = cell(r.basket, a.clientId);
                                <td class="n mono">@if (c) { CE {{ q(c.multiplier, st.lotSize, st.mult, st.ce) }} · PE {{ q(c.multiplier, st.lotSize, st.mult, st.pe) }} } @else { — }</td>
                              }
                            </tr>
                          }
                        </tbody>
                      </table>
                      <p class="note">Lot size and multipliers are basket-wide (read-only here). Dispatch = account multiplier × lot × strategy mult × leg, truncated.</p>
                    } @else { <div class="muted">No strategies in this basket.</div> }
                  </td></tr>
                }
              } @empty { <tr><td [attr.colspan]="accounts().length + 1" class="empty">No baskets assigned. Use ＋ Assign basket.</td></tr> }
            </tbody>
          </table></div>
        }
      </section>
    }
  `,
  styles: [`
    :host { display: block; }
    .muted { color: var(--muted); } .mono { font-family: var(--mono); } .sp { flex: 1; }
    .empty { padding: 20px; color: var(--muted); text-align: center; }
    .banner { display: flex; align-items: center; gap: 14px; padding: 12px 16px; margin-bottom: 12px; border: 1px solid #a5f3fc; border-radius: 10px;
      background: #ecfeff; color: #0e7490; font-size: 12.5px; p { margin: 0; flex: 1; line-height: 1.5; } .btn { flex: none; } }
    :host-context([data-theme='dark']) .banner { background: rgba(8, 145, 178, .12); border-color: rgba(8, 145, 178, .35); color: #67e8f9; }
    .mx { overflow: hidden;
      > header { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 14px 18px; border-bottom: 1px solid var(--border);
        h3 { margin: 0; font-size: 15px; font-weight: 600; } small { color: var(--text-2); font-size: 12px; } } }
    .table-wrap { overflow-x: auto; margin: 14px 18px 18px; border: 1px solid var(--border); border-radius: 10px; }
    table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
    th { text-align: left; padding: 10px 14px; background: var(--surface-2); color: var(--text-2); font-size: 11px; font-weight: 600; white-space: nowrap; }
    th.acc { min-width: 150px; small { display: block; font-size: 10px; letter-spacing: .06em; color: var(--muted); } }
    td { padding: 12px 14px; border-top: 1px solid var(--border); vertical-align: top; }
    th.n, td.n { text-align: right; }
    .brow { cursor: pointer; &:hover > td:first-child { background: var(--surface-2); } }
    .chev { display: inline-block; width: 14px; color: var(--muted); transition: transform .15s; &.open { transform: rotate(90deg); } }
    .sub { display: block; margin: 2px 0 0 14px; color: var(--muted); font-size: 11.5px; }
    .cell { background: var(--surface); &.on { background: color-mix(in srgb, var(--green-bg) 55%, transparent); }
      .mrow { display: flex; align-items: center; gap: 6px; margin-top: 6px; } }
    .ib { height: 24px; min-width: 24px; padding: 0 6px; border: 1px solid var(--border); border-radius: 6px; background: var(--surface); color: var(--text-2); font-size: 11.5px; cursor: pointer;
      &:hover:not(:disabled) { border-color: var(--teal); color: var(--teal-dark); } &:disabled { opacity: .45; cursor: default; }
      &.ok { color: var(--green); } &.bad { color: var(--red); } &.assign { display: block; margin-top: 6px; } }
    .xrow > td { background: var(--surface-2); }
    .inner { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; overflow: hidden; td { vertical-align: middle; padding: 8px 12px; } }
    .code { color: var(--muted); font-family: var(--mono); font-size: 10.5px; margin-left: 4px; }
    .note { margin: 8px 0 0; font-size: 11.5px; color: var(--text-2); }
  `],
})
export class UserBasketsComponent {
  readonly s = inject(UserWorkspaceService);
  plural = plural;
  q = dispatchQty;
  expanded = signal<Set<string>>(new Set());

  accounts = computed(() => this.s.brokerRows());
  /** live (non-cancelled) subscription per basket + account; the newest wins */
  private cells = computed(() => {
    const m = new Map<string, UserSubscriptionRow>();
    for (const x of this.s.subRows()) if (x.status !== 'Cancelled') m.set(`${x.basket}|${x.clientId}`, x);
    return m;
  });
  rows = computed(() => {
    const w = this.s.ws(); if (!w) return [];
    const names = [...new Set(this.s.subRows().filter(x => x.status !== 'Cancelled').map(x => x.basket))].sort();
    return names.map(b => ({
      basket: b,
      held: this.accounts().filter(a => this.cells().has(`${b}|${a.clientId}`)).length,
      strategies: w.baskets.find(x => x.basket === b)?.strategies ?? [],
    }));
  });
  cell(basket: string, clientId: string) { return this.cells().get(`${basket}|${clientId}`) ?? null; }
  toggle(b: string) { this.expanded.update(s => { const n = new Set(s); n.has(b) ? n.delete(b) : n.add(b); return n; }); }
}
