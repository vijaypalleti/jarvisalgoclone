import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../../shared/icon.component';
import { OpsService } from '../../core/ops.service';
import { SystemUserListItem } from '../../core/ops.models';
import { UserWorkspaceService, initials } from './user-workspace.service';

const PAGE = 60;

/** "Pick a user to manage": shown on every User Performance sub-tab until a user is selected. */
@Component({
  selector: 'app-user-picker',
  standalone: true,
  imports: [IconComponent, FormsModule],
  template: `
  <!-- ================= picker ================= -->
  <section class="card picker">
    <div class="pk-empty">
      <span class="gear"><app-icon name="wrench" [size]="26" /></span>
      <h3>{{ title() }}</h3>
      <p>Or go to <b>Trading</b> and use ⋮ → <b>Manage User</b> on any broker-account row. That finds the system user who owns it.</p>
    </div>
    <div class="pk-tools">
      <input class="input" placeholder="Search by name, email or mobile..." [ngModel]="search()" (ngModelChange)="onSearch($event)" aria-label="Search users" />
      <span class="muted">{{ search() ? list().length + ' of ' : '' }}{{ total() }} users</span>
      <button class="btn primary" (click)="s.createUser()">＋ Create user</button>
    </div>
    <div class="pk-grid" [class.loading]="loadingList()">
      @for (u of list().slice(0, shown()); track u.id) {
        <button class="ucard" (click)="choose(u)">
          <span class="av">{{ initials(u.name) }}</span>
          <span class="ui">
            <b>{{ u.name }}</b>
            <small>{{ u.email }}</small>
            <span class="tags">
              <span class="pill" [class.green]="u.status === 'Active'" [class.gray]="u.status !== 'Active'">{{ u.status }}</span>
              @if (!u.hasStaticIp) { <span class="warn">No static IP</span> }
            </span>
          </span>
        </button>
      } @empty { <div class="empty">No users match “{{ search() }}”.</div> }
    </div>
    @if (list().length > shown()) {
      <div class="more"><button class="btn" (click)="shown.set(shown() + 60)">Show more ({{ list().length - shown() }} left)</button></div>
    }
  </section>
  `,
  styles: [`
    .muted { color: var(--muted); }
    .empty { padding: 24px; text-align: center; color: var(--muted); grid-column: 1 / -1; }
/* ---------------- picker ---------------- */
.picker { padding: 28px 20px 20px; }
.pk-empty { text-align: center; margin-bottom: 24px;
  .gear { display: inline-grid; place-items: center; width: 52px; height: 52px; border-radius: 50%; background: var(--surface-2); color: var(--muted); margin-bottom: 10px; }
  h3 { margin: 0 0 6px; font-size: 19px; font-weight: 700; }
  p { margin: 0 auto; max-width: 560px; color: var(--text-2); font-size: 13px; line-height: 1.55; } }
.pk-tools { display: flex; align-items: center; gap: 12px; margin-bottom: 14px;
  .input { flex: 1; height: 42px; font-size: 13px; }
  .muted { font-size: 12px; white-space: nowrap; }
  .btn { height: 42px; } }
.pk-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 12px; transition: opacity .15s;
  &.loading { opacity: .6; } }
.ucard { display: flex; gap: 12px; align-items: flex-start; padding: 14px; text-align: left; border: 1px solid var(--border); border-radius: 12px;
  background: var(--surface); color: var(--text); font: inherit; cursor: pointer; min-width: 0; transition: border-color .15s, box-shadow .15s;
  &:hover { border-color: var(--teal); box-shadow: 0 4px 14px rgba(15, 23, 42, .07); }
  .ui { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  b { font-size: 13.5px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  small { font-size: 11.5px; color: var(--text-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .tags { display: flex; align-items: center; gap: 8px; margin-top: 6px; }
  .warn { color: var(--orange); font-size: 12px; } }
.av { flex: none; display: grid; place-items: center; width: 38px; height: 38px; border-radius: 10px; background: var(--surface-2);
  border: 1px solid var(--border); color: var(--text-2); font-weight: 700; font-size: 12.5px;
  &.big { width: 56px; height: 56px; border-radius: 12px; border: 0; background: linear-gradient(135deg, var(--teal), var(--teal-dark)); color: #fff; font-size: 20px; } }
.more { text-align: center; margin-top: 14px; }

  `],
})
export class UserPickerComponent {
  private ops = inject(OpsService);
  readonly s = inject(UserWorkspaceService);
  title = input('Pick a user to manage');
  search = signal('');
  list = signal<SystemUserListItem[]>([]);
  total = signal(0);
  shown = signal(PAGE);
  loadingList = signal(false);
  initials = initials;
  private timer?: ReturnType<typeof setTimeout>;

  constructor() { effect(() => { this.s.listVersion(); this.loadList(); }); }

  onSearch(v: string) { this.search.set(v); clearTimeout(this.timer); this.timer = setTimeout(() => this.loadList(), 200); }
  loadList() {
    this.loadingList.set(true);
    this.ops.users(this.search()).subscribe({
      next: r => { this.list.set(r.items); this.total.set(r.total); this.shown.set(PAGE); this.loadingList.set(false); },
      error: () => this.loadingList.set(false),
    });
  }
  choose(u: SystemUserListItem) { this.s.open(u.id); }
}
