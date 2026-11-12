import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AssistantChatComponent } from '../shared/assistant-chat.component';
import { IconComponent } from '../shared/icon.component';

interface NavChild { label: string; icon: string; path: string; }
/** children: true = has a sub-menu in production that isn't cloned yet (caret only); an array = a real sub-menu */
interface NavItem { label: string; icon: string; path: string; children?: boolean | NavChild[]; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent, AssistantChatComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  pinned = signal(false);
  hovered = signal(false);
  dark = signal(false);
  userMenu = signal(false);

  nav: NavItem[] = [
    { label: 'Dashboard', icon: 'bar-chart', path: '/admin/dashboard' },
    { label: 'Observability', icon: 'activity', path: '/admin/observability' },
    { label: 'Basket Management', icon: 'basket', path: '/admin/baskets', children: [
      { label: 'Basket Subscriptions', icon: 'basket', path: '/admin/basket-subscriptions' },
      { label: 'Basket Configuration', icon: 'basket', path: '/admin/basket-configuration' },
      { label: 'Basket Console (New)', icon: 'grid', path: '/admin/basket-console' },
      { label: 'Split / Freeze Limits', icon: 'layers', path: '/admin/split-freeze-limits' },
    ] },
    { label: 'Algo Management', icon: 'trending-up', path: '/admin/algos', children: true },
    { label: 'Holding', icon: 'briefcase', path: '/admin/holding' },
    { label: 'Copy Trade', icon: 'copy', path: '/admin/copy-trade' },
    { label: 'Algo Trade', icon: 'plug', path: '/admin/algo-trade' },
    { label: 'Strategy Performance', icon: 'trending-up', path: '/admin/strategy-performance' },
    { label: 'Strategy Performance (New)', icon: 'trending-up', path: '/admin/strategy-performance-console-v2' },
    { label: 'Broker A/c', icon: 'users', path: '/admin/broker-accounts' },
    { label: 'Broker Live (Fyers)', icon: 'radio', path: '/admin/broker-live' },
    { label: 'Crypto Live (Delta)', icon: 'coin', path: '/admin/crypto-live' },
    { label: 'Realtime Data', icon: 'database', path: '/admin/realtime' },
    { label: 'System Users', icon: 'users-round', path: '/admin/system-users' },
    { label: 'Subscription', icon: 'gem', path: '/admin/subscription' },
    { label: 'Announcement', icon: 'megaphone', path: '/admin/announcement' },
    { label: 'Logs', icon: 'logs', path: '/admin/logs' },
    { label: 'Coupons', icon: 'ticket', path: '/admin/coupons' },
    { label: 'Platform Access', icon: 'grid', path: '/admin/platform-access', children: true },
    { label: 'Security', icon: 'shield', path: '/admin/security' },
    { label: 'Data Store', icon: 'database', path: '/admin/data-store' },
    { label: 'API Keys', icon: 'key', path: '/admin/api-keys' },
    { label: 'Referrals', icon: 'share', path: '/admin/referrals' },
  ];

  get expanded() { return this.pinned() || this.hovered(); }

  // sub-menus: a group opens on click, and opens by itself when one of its pages is showing
  private router = inject(Router);
  url = signal(this.router.url);
  openGroup = signal<string | null>(null);
  constructor() {
    this.syncGroup();
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => { this.url.set(this.router.url); this.syncGroup(); });
  }
  subItems(item: NavItem): NavChild[] { return Array.isArray(item.children) ? item.children : []; }
  groupActive(item: NavItem) { return this.subItems(item).some(c => this.url().startsWith(c.path)); }
  toggleGroup(item: NavItem) { this.openGroup.update(g => g === item.path ? null : item.path); }
  private syncGroup() { const g = this.nav.find(i => this.groupActive(i)); if (g) this.openGroup.set(g.path); }

  toggleTheme() {
    this.dark.update(v => !v);
    document.documentElement.setAttribute('data-theme', this.dark() ? 'dark' : 'light');
  }
}
