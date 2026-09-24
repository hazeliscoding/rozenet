import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Denwa's 16x16 icon sprite, inlined.
 *
 * The design system ships these as an external `assets/icons.svg` sprite used via
 * `<use href="assets/icons.svg#i-folder">`, but external `<use>` references have
 * patchy Safari support — so the paths live here instead, matching how the
 * under-construction page already inlines its warning glyph.
 *
 * 1px-grid geometric strokes, square joins, currentColor throughout.
 */
export type IconName =
  | 'folder'
  | 'file'
  | 'lock'
  | 'message'
  | 'user'
  | 'image'
  | 'warning'
  | 'audio'
  | 'archive'
  | 'eye'
  | 'search'
  | 'clock';

@Component({
  selector: 'dw-icon',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="dw-icon" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      @switch (name()) {
        @case ('folder') {
          <path d="M1.5 3.5h5l1.5 2h6.5v7h-13z" fill="none" stroke="currentColor" />
        }
        @case ('file') {
          <path d="M3.5 1.5h6l3 3v10h-9z M9.5 1.5v3h3" fill="none" stroke="currentColor" />
        }
        @case ('lock') {
          <rect x="3.5" y="7.5" width="9" height="7" fill="none" stroke="currentColor" />
          <path d="M5.5 7.5V5a2.5 2.5 0 015 0v2.5M8 10v2" fill="none" stroke="currentColor" />
        }
        @case ('message') {
          <path d="M1.5 2.5h13v8h-8l-3 3v-3h-2z" fill="none" stroke="currentColor" />
          <path d="M4 5.5h9M4 8h6" stroke="currentColor" />
        }
        @case ('user') {
          <circle cx="8" cy="5" r="2.5" fill="none" stroke="currentColor" />
          <path d="M2.5 14c0-3 2.5-4.5 5.5-4.5s5.5 1.5 5.5 4.5" fill="none" stroke="currentColor" />
        }
        @case ('image') {
          <rect x="1.5" y="2.5" width="13" height="11" fill="none" stroke="currentColor" />
          <circle cx="5" cy="6" r="1" fill="currentColor" />
          <path d="M2 12l4-4 3 3 3-4 2.5 3" fill="none" stroke="currentColor" />
        }
        @case ('warning') {
          <path d="M8 1.5L15 14H1z" fill="none" stroke="currentColor" />
          <path d="M8 6v4M8 11.5v1.5" stroke="currentColor" />
        }
        @case ('audio') {
          <path d="M2.5 6.5h2l4-3v9l-4-3h-2z" fill="none" stroke="currentColor" />
          <path d="M11 5.5v5M13.5 4v8" stroke="currentColor" />
        }
        @case ('archive') {
          <rect x="1.5" y="2.5" width="13" height="3" fill="none" stroke="currentColor" />
          <path d="M2.5 5.5v8h11v-8M6 8h4" fill="none" stroke="currentColor" />
        }
        @case ('eye') {
          <path
            d="M1 8c2-3.5 4.5-5 7-5s5 1.5 7 5c-2 3.5-4.5 5-7 5s-5-1.5-7-5z"
            fill="none"
            stroke="currentColor"
          />
          <circle cx="8" cy="8" r="2" fill="currentColor" />
        }
        @case ('search') {
          <circle cx="6.5" cy="6.5" r="4" fill="none" stroke="currentColor" />
          <path d="M9.5 9.5L14 14" stroke="currentColor" />
        }
        @case ('clock') {
          <circle cx="8" cy="8" r="6.5" fill="none" stroke="currentColor" />
          <path d="M8 4v4l3 2" fill="none" stroke="currentColor" />
        }
      }
    </svg>
  `,
})
export class IconComponent {
  readonly name = input.required<IconName>();
}
