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

export interface Post {
  id: number;
  author: string;
  body: string;
  createdAt: string;
}

export interface ThreadView {
  thread: ThreadHead;
  posts: Post[];
}
