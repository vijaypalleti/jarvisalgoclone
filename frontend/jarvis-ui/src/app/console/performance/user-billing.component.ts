import { Component, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { IconComponent } from '../../shared/icon.component';
import { UserWorkspaceService, plural } from './user-workspace.service';

/**
 * Performance → Subscriptions & Billing (with a user selected): what this client pays — plans, renewals and payments.
 * Not cloned from a screenshot; built in the same style. Demo prices: ₹2,499 per month (10 % off 6 months, 20 % off 12).
 */
@Component({
  selector: 'app-user-billing',
  standalone: true,
  imports: [IconComponent, DatePipe, DecimalPipe],
  template: `
    @if (s.ws()) {
      <div class="tiles">
        <div class="card tile"><span class="label-caps">Lifetime value</span><b>₹{{ ltv() | number: '1.0-0' }}</b><small>{{ plural(paid().length, 'paid payment') }}</small></div>
        <div class="card tile" [class.alert]="due() > 0"><span class="label-caps">Outstanding</span><b>₹{{ due() | number: '1.0-0' }}</b><small>{{ plural(unpaid().length, 'payment') }} pending or failed</small></div>
        <div class="card tile"><span class="label-caps">Active plans</span><b>{{ active().length }}</b><small>₹{{ monthly() | number: '1.0-0' }} / month equivalent</small></div>
        <div class="card tile"><span class="label-caps">Next renewal</span><b>{{ next() ? (next()!.end | date: 'dd MMM') : '—' }}</b><small>{{ next() ? next()!.basket + ' · ' + next()!.daysLeft + ' day(s)' : 'no active plan' }}</small></div>
      </div>

      <section class="card blk">
        <header><app-icon name="package" [size]="14" /><h3>Subscriptions</h3><small>{{ plural(s.subRows().length, 'plan') }}</small><span class="sp"></span>
          <button class="btn sm primary" (click)="s.addSub()" [disabled]="s.busy()">＋ Add subscription</button></header>
        <div class="table-wrap"><table>
          <thead><tr><th>Basket</th><th>Broker account</th><th>Plan</th><th class="n">Price</th><th>Period</th><th class="n">Days left</th><th>Status</th><th>Actions</th></tr></thead>
          <tbody>
            @for (x of s.subRows(); track x.id) {
              <tr [class.dim]="x.status === 'Cancelled'">
                <td><b>{{ x.basket }}</b></td>
                <td class="mono">{{ s.accLabel(x.clientId) }}</td>
                <td>{{ x.plan }}<small class="sub">{{ x.months }} month(s)</small></td>
                <td class="n mono">₹{{ x.price | number: '1.0-0' }}</td>
                <td class="mono">{{ x.start | date: 'dd MMM yy' }} → {{ x.end | date: 'dd MMM yy' }}</td>
                <td class="n mono" [class.warn]="x.status === 'Active' && x.daysLeft <= 15">{{ x.daysLeft }}</td>
                <td><span class="pill" [class.green]="x.status === 'Active'" [class.amber]="x.status === 'Paused'" [class.gray]="x.status === 'Inactive'" [class.red]="x.status === 'Cancelled' || x.status === 'Expired'">{{ x.status }}</span></td>
                <td><div class="acts">
                  @if (x.status !== 'Cancelled') {
                    <button class="btn sm" (click)="s.subAction(x, 'extend')" [disabled]="s.busy()">📅 Renew +1 month</button>
                    @if (x.status === 'Active') { <button class="btn sm" (click)="s.subAction(x, 'pause')" [disabled]="s.busy()">⏸ Pause</button> }
                    @else { <button class="btn sm" (click)="s.subAction(x, 'activate')" [disabled]="s.busy()">✓ Activate</button> }
                  }
                </div></td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty">No subscriptions.</td></tr> }
          </tbody>
        </table></div>
      </section>

      <section class="card blk">
        <header><app-icon name="coin" [size]="14" /><h3>Payments</h3><small>newest first</small></header>
        <div class="table-wrap"><table>
          <thead><tr><th>Date</th><th>Invoice</th><th>Basket</th><th>Method</th><th class="n">Amount</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (p of s.payRows(); track p.id) {
              <tr>
                <td class="mono">{{ p.date | date: 'dd MMM yyyy, HH:mm' }}</td>
                <td class="mono">{{ p.invoice }}</td>
                <td>{{ p.basket }}</td>
                <td>{{ p.method }}</td>
                <td class="n mono">₹{{ p.amount | number: '1.0-0' }}</td>
                <td><span class="pill" [class.green]="p.status === 'Paid'" [class.amber]="p.status === 'Pending'" [class.red]="p.status === 'Failed'">{{ p.status }}</span></td>
                <td>@if (p.status !== 'Paid') { <button class="btn sm" (click)="s.markPaid(p)" [disabled]="s.busy()">✓ Mark paid</button> }</td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty">No payments yet (trial or unpaid plans).</td></tr> }
          </tbody>
        </table></div>
      </section>
    }
  `,
  styles: [`
    :host { display: block; }
    .mono { font-family: var(--mono); } .sp { flex: 1; } .warn { color: var(--orange); font-weight: 600; }
    .empty { padding: 20px; color: var(--muted); text-align: center; }
    .tiles { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 12px;
      .tile { display: flex; flex-direction: column; gap: 4px; padding: 14px 18px; b { font-size: 24px; font-weight: 700; } small { color: var(--text-2); font-size: 12px; }
        &.alert b { color: var(--red); } } }
    @media (max-width: 900px) { .tiles { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    .blk { margin-bottom: 12px; overflow: hidden;
      > header { display: flex; align-items: center; gap: 10px; padding: 14px 18px; border-bottom: 1px solid var(--border);
        h3 { margin: 0; font-size: 15px; font-weight: 600; } small { color: var(--text-2); font-size: 12px; } } }
    .table-wrap { overflow-x: auto; margin: 14px 18px 18px; border: 1px solid var(--border); border-radius: 10px; }
    table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
    th { text-align: left; padding: 10px 14px; background: var(--surface-2); color: var(--text-2); font-size: 10.5px; font-weight: 600; letter-spacing: .06em; text-transform: uppercase; white-space: nowrap; }
    td { padding: 11px 14px; border-top: 1px solid var(--border); vertical-align: middle; }
    th.n, td.n { text-align: right; }
    tr.dim td { opacity: .55; }
    .sub { display: block; color: var(--muted); font-size: 11px; }
    .acts { display: flex; gap: 6px; flex-wrap: wrap; }
  `],
})
export class UserBillingComponent {
  readonly s = inject(UserWorkspaceService);
  plural = plural;
  paid = computed(() => this.s.payRows().filter(p => p.status === 'Paid'));
  unpaid = computed(() => this.s.payRows().filter(p => p.status !== 'Paid'));
  ltv = computed(() => this.paid().reduce((t, p) => t + p.amount, 0));
  due = computed(() => this.unpaid().reduce((t, p) => t + p.amount, 0));
  active = computed(() => this.s.subRows().filter(x => x.status === 'Active'));
  monthly = computed(() => this.active().reduce((t, x) => t + x.price / x.months, 0));
  next = computed(() => [...this.active()].sort((a, b) => a.daysLeft - b.daysLeft)[0] ?? null);
}
