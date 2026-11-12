import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UserModal, UserWorkspaceService } from './user-workspace.service';

/** The edit / confirm dialog and the toast used by every User Performance sub-tab (state lives in UserWorkspaceService). */
@Component({
  selector: 'app-user-dialogs',
  standalone: true,
  imports: [FormsModule],
  template: `
    @if (s.modal(); as m) {
      <div class="scrim" (click)="s.modal.set(null)"></div>
      <form class="modal card" role="dialog" aria-modal="true" (submit)="$event.preventDefault(); s.submitModal()" (keydown.escape)="s.modal.set(null)">
        @switch (m.kind) {
          @case ('user') {
            @let f = usr(m);
            <h3>{{ f.mode === 'edit' ? 'Edit user' : 'Create user' }}</h3>
            <label>Full name<input class="input" name="n" [(ngModel)]="f.name" required /></label>
            <label>Email<input class="input" name="e" type="email" [(ngModel)]="f.email" required /></label>
            <label>Mobile<input class="input" name="m" [(ngModel)]="f.mobile" /></label>
          }
          @case ('number') {
            @let f = num(m);
            <h3>{{ f.title }}</h3>
            <label>{{ f.label }}<input class="input" name="v" type="number" [min]="f.min" [max]="f.max" step="1" [(ngModel)]="f.value" /></label>
          }
          @case ('subscribe') {
            @let f = sub(m);
            <h3>Assign basket</h3>
            <label>Basket<select class="input" name="b" [(ngModel)]="f.basket">@for (b of s.basketNames(); track b) { <option [value]="b">{{ b }}</option> }</select></label>
            <label>Broker account<select class="input" name="c" [(ngModel)]="f.clientId">@for (a of s.ws()?.brokerAccounts ?? []; track a.clientId) { <option [value]="a.clientId">{{ a.broker }} · {{ a.accountId }}</option> }</select></label>
            <div class="two">
              <label>Plan length<select class="input" name="mo" [(ngModel)]="f.months"><option [ngValue]="1">1 month</option><option [ngValue]="3">3 months</option><option [ngValue]="6">6 months</option><option [ngValue]="12">12 months</option></select></label>
              <label>Multiplier<input class="input" name="mu" type="number" min="1" max="50" [(ngModel)]="f.multiplier" /></label>
            </div>
            <p class="small">A pending payment is created for the plan; mark it paid under Subscriptions &amp; Billing.</p>
          }
          @case ('confirm') {
            @let f = cnf(m);
            <h3>{{ f.title }}</h3>
            <p>{{ f.text }}</p>
          }
        }
        <p class="demo">Demo: this changes the in-memory demo data only.</p>
        <div class="mact">
          <button type="button" class="btn" (click)="s.modal.set(null)">Cancel</button>
          <button type="submit" class="btn" [class.primary]="m.kind !== 'confirm' || !cnf(m).danger" [class.dangerfill]="m.kind === 'confirm' && cnf(m).danger" [disabled]="s.busy()">
            {{ m.kind === 'confirm' ? 'Confirm' : m.kind === 'user' && usr(m).mode === 'create' ? 'Create' : m.kind === 'subscribe' ? 'Assign' : 'Save' }}
          </button>
        </div>
      </form>
    }
    @if (s.toast(); as t) { <div class="toast" [class.bad]="t.bad" role="status">{{ t.text }}</div> }
  `,
  styles: [`
    .scrim { position: fixed; inset: 0; background: rgba(15, 23, 42, .45); z-index: 950; }
    .modal { position: fixed; z-index: 951; left: 50%; top: 18vh; transform: translateX(-50%); width: min(440px, calc(100vw - 32px)); padding: 20px;
      display: flex; flex-direction: column; gap: 12px; box-shadow: 0 24px 60px rgba(15, 23, 42, .3);
      h3 { margin: 0; font-size: 16px; } p { margin: 0; color: var(--text-2); } .small { font-size: 11.5px; }
      label { display: flex; flex-direction: column; gap: 6px; font-size: 12px; font-weight: 600; color: var(--text-2); }
      .two { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
      .demo { font-size: 11px; color: var(--muted); }
      .mact { display: flex; justify-content: flex-end; gap: 8px; } }
    .btn.dangerfill { background: var(--red); border-color: var(--red); color: #fff; font-weight: 600; }
    .toast { position: fixed; z-index: 960; left: 50%; bottom: 28px; transform: translateX(-50%); max-width: calc(100vw - 32px);
      padding: 10px 16px; border-radius: 10px; background: #0f172a; color: #fff; font-size: 12.5px; box-shadow: 0 10px 30px rgba(0, 0, 0, .25);
      &.bad { background: #7c2d12; } }
  `],
})
export class UserDialogsComponent {
  readonly s = inject(UserWorkspaceService);
  num(m: UserModal) { return m as Extract<UserModal, { kind: 'number' }>; }
  usr(m: UserModal) { return m as Extract<UserModal, { kind: 'user' }>; }
  sub(m: UserModal) { return m as Extract<UserModal, { kind: 'subscribe' }>; }
  cnf(m: UserModal) { return m as Extract<UserModal, { kind: 'confirm' }>; }
}
