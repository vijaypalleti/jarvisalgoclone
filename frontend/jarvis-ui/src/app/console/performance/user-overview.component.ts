import { Component, computed, inject, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { IconComponent } from '../../shared/icon.component';
import { UserWorkspaceService, initials, plural } from './user-workspace.service';

export type UserTab = 'overview' | 'trading' | 'users' | 'basket' | 'billing';

/** Performance → Overview (with a user selected): who this client is, and what needs a human across every section. */
@Component({
  selector: 'app-user-overview',
  standalone: true,
  imports: [IconComponent, DecimalPipe],
  template: `
    @if (s.ws(); as w) {
      @let u = w.user;
      <section class="card who">
        <span class="av">{{ initials(u.name) }}</span>
        <div class="id">
          <h2>{{ u.name }} <span class="pill" [class.green]="u.status === 'Active'" [class.gray]="u.status !== 'Active'">{{ u.status }}</span></h2>
          <div class="mono meta">#{{ u.id }} · {{ u.email }} · {{ u.mobile }}</div>
          <div class="mono meta">@if (u.publicIp) { static IP {{ u.publicIp }} } @else { <span class="warn">No static IP</span> }</div>
        </div>
        <nav class="links">
          <button (click)="go.emit('trading')"><app-icon name="trending-up" [size]="13" /> Trading</button>
          <button (click)="go.emit('users')"><app-icon name="user" [size]="13" /> User</button>
          <button (click)="go.emit('basket')"><app-icon name="basket" [size]="13" /> Baskets</button>
          <button (click)="go.emit('billing')"><app-icon name="ticket" [size]="13" /> Billing</button>
        </nav>
      </section>

      <div class="tiles">
        <div class="card tile"><span class="label-caps">Broker accounts</span><b>{{ s.brokerRows().length }}</b><small>{{ ready() }} trade-ready today</small></div>
        <div class="card tile"><span class="label-caps">Baskets held</span><b>{{ s.basketRows().length }}</b><small>across {{ plural(liveSubs(), 'subscription') }}</small></div>
        <div class="card tile"><span class="label-caps">Active subscriptions</span><b>{{ activeSubs() }}</b><small>{{ paused() ? paused() + ' paused' : 'none paused' }}</small></div>
        <div class="card tile"><span class="label-caps">Lifetime value</span><b>₹{{ ltv() | number: '1.0-0' }}</b><small>{{ plural(paidCount(), 'payment') }}</small></div>
      </div>

      <section class="card att">
        <header>
          <span class="bell">🔔</span><h3>Needs attention</h3>
          <small>Across every section. This is the only view that shows them all at once.</small>
          <span class="sp"></span>
          <button class="link" (click)="s.refresh()"><app-icon name="refresh" [size]="12" /> Recheck</button>
        </header>
        @if (w.attention.length === 0) {
          <div class="ok">
            <span class="tick">✓</span>
            <div><b>Nothing needs attention.</b><p>Every broker account is trade-ready, no subscription expires within 15 days, and every payment is settled.</p></div>
          </div>
        } @else {
          <ul>
            @for (a of w.attention; track $index) {
              <li [class]="a.severity">
                <i></i><span class="txt">{{ a.text }}</span>
                <button class="sect" (click)="go.emit(tabFor(a.section))">{{ sectionLabel[a.section] }} →</button>
              </li>
            }
          </ul>
        }
      </section>

      <section class="card list">
        <header><app-icon name="plug" [size]="14" /><h3>Broker accounts</h3><span class="sp"></span><button class="link" (click)="go.emit('users')">Manage</button></header>
        <div class="grid">
          @for (a of s.brokerRows(); track a.clientId) {
            <div class="acc">
              <div><b>{{ a.broker }}</b> <span class="mono muted">{{ a.accountId }}</span></div>
              <div class="pills">
                <span class="pill" [class.green]="a.algoOn" [class.gray]="!a.algoOn">Algo {{ a.algoOn ? 'ON' : 'OFF' }}</span>
                <span class="pill" [class.green]="a.session === 'Logged in'" [class.orange]="a.session !== 'Logged in'">{{ a.session === 'Logged in' ? 'Session Today' : 'Logged out' }}</span>
                @if (!a.active) { <span class="pill red">Inactive</span> }
              </div>
              <small class="muted">Lot size {{ a.lot }}</small>
            </div>
          } @empty { <div class="empty">No broker accounts linked.</div> }
        </div>
      </section>

      <section class="card list">
        <header><app-icon name="package" [size]="14" /><h3>Subscriptions</h3><span class="sp"></span><button class="link" (click)="go.emit('billing')">Billing</button></header>
        <div class="grid">
          @for (x of s.subRows(); track x.id) {
            <div class="acc">
              <div><b>{{ x.basket }}</b> <span class="mono muted">{{ x.broker }}</span></div>
              <div class="pills">
                <span class="pill" [class.green]="x.status === 'Active'" [class.amber]="x.status === 'Paused'" [class.red]="x.status === 'Cancelled' || x.status === 'Expired'" [class.gray]="x.status === 'Inactive'">{{ x.status }}</span>
                <span class="pill gray">{{ x.multiplier }}×</span>
              </div>
              <small class="muted" [class.warn]="x.status === 'Active' && x.daysLeft <= 15">{{ x.daysLeft }} day(s) left · {{ x.months }} month plan</small>
            </div>
          } @empty { <div class="empty">No subscriptions.</div> }
        </div>
      </section>
    }
  `,
  styles: [`
    :host { display: block; }
    .muted { color: var(--muted); } .mono { font-family: var(--mono); } .sp { flex: 1; } .warn { color: var(--orange) !important; }
    .empty { padding: 16px; color: var(--muted); }
    .who { display: flex; align-items: center; gap: 16px; padding: 16px 20px; margin-bottom: 12px; border-left: 3px solid var(--teal);
      .av { flex: none; display: grid; place-items: center; width: 54px; height: 54px; border-radius: 50%; background: linear-gradient(135deg, var(--teal), var(--teal-dark)); color: #fff; font-weight: 700; font-size: 19px; }
      .id { flex: 1; min-width: 0; }
      h2 { margin: 0 0 4px; font-size: 19px; display: flex; align-items: center; gap: 10px; }
      .meta { font-size: 12px; color: var(--text-2); }
      .links { display: flex; gap: 4px; flex-wrap: wrap;
        button { display: inline-flex; align-items: center; gap: 5px; height: 30px; padding: 0 10px; border: 0; border-radius: 8px; background: none; color: var(--text); font: inherit; font-size: 12.5px; font-weight: 500; cursor: pointer;
          &:hover { background: var(--surface-2); } app-icon { color: var(--text-2); } } } }
    .tiles { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 12px;
      .tile { display: flex; flex-direction: column; gap: 4px; padding: 14px 18px; b { font-size: 24px; font-weight: 700; } small { color: var(--text-2); font-size: 12px; } } }
    @media (max-width: 900px) { .tiles { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    .att, .list { margin-bottom: 12px; overflow: hidden;
      header { display: flex; align-items: center; gap: 10px; padding: 14px 20px; border-bottom: 1px solid var(--border);
        h3 { margin: 0; font-size: 15px; font-weight: 600; } small { color: var(--text-2); font-size: 12px; } } }
    .link { display: inline-flex; align-items: center; gap: 4px; border: 0; background: none; color: var(--text); font: inherit; font-size: 12.5px; font-weight: 500; cursor: pointer; &:hover { color: var(--teal-dark); } }
    .ok { display: flex; gap: 14px; align-items: flex-start; padding: 18px 20px;
      .tick { display: grid; place-items: center; width: 32px; height: 32px; border-radius: 50%; background: var(--green-bg); color: var(--green); font-weight: 700; flex: none; }
      p { margin: 4px 0 0; color: var(--text-2); font-size: 12.5px; } }
    ul { list-style: none; margin: 0; padding: 6px 0; }
    li { display: flex; align-items: center; gap: 10px; padding: 9px 20px; font-size: 13px;
      i { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--muted); }
      &.high i { background: var(--red); } &.medium i { background: var(--orange); } &.low i { background: var(--amber); }
      .txt { flex: 1; }
      .sect { border: 1px solid var(--border); background: var(--surface); border-radius: 999px; padding: 2px 10px; font-size: 11px; color: var(--text-2); cursor: pointer; &:hover { border-color: var(--teal); color: var(--teal-dark); } } }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 10px; padding: 14px 20px 18px; }
    .acc { display: flex; flex-direction: column; gap: 6px; padding: 12px 14px; border: 1px solid var(--border); border-radius: 10px; font-size: 13px;
      .pills { display: flex; gap: 6px; flex-wrap: wrap; } small { font-size: 11.5px; } > div > .muted { margin-left: 6px; } }
  `],
})
export class UserOverviewComponent {
  readonly s = inject(UserWorkspaceService);
  go = output<UserTab>();
  initials = initials;
  plural = plural;
  readonly sectionLabel: Record<string, string> = { brokers: 'Broker accounts', subs: 'Subscriptions', billing: 'Billing', user: 'User details' };

  ready = computed(() => this.s.brokerRows().filter(a => a.active && a.algoOn && a.session === 'Logged in').length);
  liveSubs = computed(() => this.s.subRows().filter(x => x.status !== 'Cancelled').length);
  activeSubs = computed(() => this.s.subRows().filter(x => x.status === 'Active').length);
  paused = computed(() => this.s.subRows().filter(x => x.status === 'Paused').length);
  ltv = computed(() => this.s.payRows().filter(p => p.status === 'Paid').reduce((t, p) => t + p.amount, 0));
  paidCount = computed(() => this.s.payRows().filter(p => p.status === 'Paid').length);
  tabFor(section: string): UserTab { return section === 'billing' ? 'billing' : section === 'subs' ? 'basket' : 'users'; }
}
