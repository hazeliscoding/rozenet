export interface UserDto {
  id: number;
  username: string;
  isAdmin: boolean;
}

export interface AuthResponse {
  token: string;
  user: UserDto;
}

export interface Board {
  slug: string;
  title: string;
  description: string;
  glyph: string;
  threads: number;
  posts: number;
}

export interface ThreadRow {
  id: number;
  title: string;
  author: string;
  createdAt: string;
  lastPostAt: string;
  locked: boolean;
  sticky: boolean;
  replies: number;
}

export interface ThreadHead {
  id: number;
  title: string;
  boardSlug: string;
  boardTitle: string;
  locked: boolean;
  sticky: boolean;
}

export interface Reaction {
  kaomoji: string;
  count: number;
  mine: boolean;
}

export interface Post {
  id: number;
  author: string;
  authorId: number;
  body: string;
  createdAt: string;
  editedAt: string | null;
  reactions: Reaction[];
}

export interface ThreadView {
  thread: ThreadHead;
  posts: Post[];
  viewerId: number;
  viewerIsAdmin: boolean;
}

/** The reaction palette members can choose from (mirrors the API allow-list). */
export const REACTION_PALETTE = ['♥', '☆', '✧', '(＾▽＾)', '(=^･ω･^=)', 'orz'];
