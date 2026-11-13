import { Component, ElementRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { IconComponent } from '../../shared/icon.component';
import { UserWorkspaceService, initials, plural } from './user-workspace.service';

type Section = 'details' | 'brokers' | 'subs' | 'baskets';

/**
 * Performance → User Management: what the selected user OWNS — details, broker accounts, basket subscriptions and
 * baskets (with the qty the OMS would dispatch). The user, actions and dialogs live in UserWorkspaceService.
 */
@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [IconComponent, DatePipe],
  templateUrl: './user-management.component.html',
  styleUrl: './user-management.component.scss',
})
export class UserManagementComponent {
  readonly svc = inject(UserWorkspaceService);
  private host: ElementRef<HTMLElement> = inject(ElementRef);

  open = signal<Record<Section, boolean>>({ details: true, brokers: true, subs: true, baskets: true });
  allOpen = computed(() => Object.values(this.open()).every(Boolean));
  loggedIn = computed(() => this.svc.brokerRows().filter(a => a.session === 'Logged in').length);
  algoOn = computed(() => this.svc.brokerRows().filter(a => a.algoOn).length);
  activeSubs = computed(() => this.svc.subRows().filter(s => s.status === 'Active').length);
  multipliers = computed(() => [...new Set(this.svc.basketRows().map(b => b.multiplier))]);
  /** the OMS client id(s) this user owns, e.g. "1360" or "1023, 117" ('' = none trading in the OMS) */
  omsIds = computed(() => (this.svc.ws()?.brokerAccounts ?? []).filter(a => a.inOms).map(a => a.clientId).join(', '));
  initials = initials;
  plural = plural;

  toggle(s: Section) { this.open.update(o => ({ ...o, [s]: !o[s] })); }
  toggleAll() { const v = !this.allOpen(); this.open.set({ details: v, brokers: v, subs: v, baskets: v }); }
  jump(s: Section) {
    this.open.update(o => ({ ...o, [s]: true }));
    setTimeout(() => this.host.nativeElement.querySelector(`#um-${s}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 30);
  }
}
