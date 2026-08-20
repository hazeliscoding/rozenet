export interface LinkItem {
  label: string;
  url: string;
  glyph: string;
  description: string;
  copyText?: string;
}

export interface LinkCategory {
  title: string;
  glyph: string;
  links: LinkItem[];
}

// Fallback data — the API (when running) serves the same shape from Postgres.
export const LINK_CATEGORIES: LinkCategory[] = [
  {
    title: 'watching & playing',
    glyph: '►',
    links: [
      {
        label: 'AniList',
        url: 'https://anilist.co/user/RozeAngel',
        glyph: '★',
        description: 'anime & manga i\'m watching/reading',
      },
      {
        label: 'MyFigureCollection',
        url: 'https://myfigurecollection.net/profile/RozeAngel',
        glyph: '♥',
        description: 'figures i own + the wishlist that haunts me',
      },
      {
        label: 'Backloggd',
        url: 'https://backloggd.com/u/RozeAngel',
        glyph: '●',
        description: 'games i\'m playing + the eternal backlog',
      },
    ],
  },
  {
    title: 'code & chat',
    glyph: '✎',
    links: [
      {
        label: 'GitHub',
        url: 'https://github.com/hazeliscoding',
        glyph: '⌂',
        description: 'code, projects, and cute web experiments',
      },
      {
        label: 'Discord',
        url: 'https://discord.com/',
        glyph: '✉',
        description: 'tap to copy my handle',
        copyText: 'roze.angel',
      },
    ],
  },
];
