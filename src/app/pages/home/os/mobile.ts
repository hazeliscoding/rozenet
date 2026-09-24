import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconComponent, IconName } from '../../../components/icon';
import { RozeOsBase } from './os-base';

type AppId = 'directory' | 'control' | 'denlog';

interface AppDef {
  id: AppId;
  icon: IconName;
  label: string;
  title: string;
  tag?: string;
  /** Left and right cells of the app's status bar. */
  status: [string, string];
}

const APPS: AppDef[] = [
  {
    id: 'directory',
    icon: 'folder',
    label: 'directory',
    title: 'directory.exe — C:\\den\\links',
    status: ['DEAD LINKS PRUNED', 'roze-tree ♥'],
  },
  {
    id: 'control',
    icon: 'lock',
    label: 'control room',
    title: 'access — control_room.exe',
    tag: '[locked]',
    status: ['DOOR: LOCKED', '"be a good visitor ♥"'],
  },
  {
    id: 'denlog',
    icon: 'file',
    label: 'den.log',
    title: 'den.log',
    status: ['LOG', 'tail -f'],
  },
];

/**
 * ROZE-OS, mobile shell. Ported from ROZE-OS Mobile: a status bar, a home
 * screen with an app grid, full-screen app views, and a dock.
 *
 * Same apps as the desktop shell — this is a different presentation of the
 * same OS, not a reduced version of it.
 */
@Component({
  selector: 'roze-mobile',
  standalone: true,
  imports: [FormsModule, IconComponent],
  templateUrl: './mobile.html',
  styleUrl: './mobile.scss',
})
export class MobileComponent extends RozeOsBase {
  protected readonly apps = APPS;

  /** null = the home screen. */
  protected readonly app = signal<AppId | null>(null);

  protected readonly current = computed(() => APPS.find((a) => a.id === this.app()) ?? null);

  protected readonly dateLine = computed(() => {
    const d = new Date();
    return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(
      d.getDate(),
    ).padStart(2, '0')}`;
  });

  openApp(id: AppId) {
    if (this.app() !== id) this.addLog('opened ' + id);
    this.app.set(id);
  }

  goHome() {
    this.app.set(null);
  }

  isOpen(id: AppId): boolean {
    return this.app() === id;
  }
}
