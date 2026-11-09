import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { IconComponent } from '../shared/icon.component';

interface NavItem { label: string; icon: string; path: string; children?: boolean; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
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
    { label: 'Basket Management', icon: 'basket', path: '/admin/baskets', children: true },
    { label: 'Algo Management', icon: 'trending-up', path: '/admin/algos', children: true },
    { label: 'Holding', icon: 'briefcase', path: '/admin/holding' },
    { label: 'Copy Trade', icon: 'copy', path: '/admin/copy-trade' },
    { label: 'Algo Trade', icon: 'plug', path: '/admin/algo-trade' },
    { label: 'Strategy Performance', icon: 'trending-up', path: '/admin/strategy-performance' },
    { label: 'Strategy Performance (New)', icon: 'trending-up', path: '/admin/strategy-performance-console-v2' },
    { label: 'Broker A/c', icon: 'users', path: '/admin/broker-accounts' },
    { label: 'Broker Live (Fyers)', icon: 'radio', path: '/admin/broker-live' },
    { label: 'Crypto Live (Delta)', icon: 'coin', path: '/admin/crypto-live' },
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

  toggleTheme() {
    this.dark.update(v => !v);
    document.documentElement.setAttribute('data-theme', this.dark() ? 'dark' : 'light');
  }
}
