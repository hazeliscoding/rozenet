# rozenet ✦

Roze's den — a personal, non-work website with a GlitterNet-style design and a Makima motif (black, glitter red, gold). Home of the link directory (replacing [roze-tree](https://hazeliscoding.github.io/roze-tree/)) and a private, invite-only forum.

> 🖤 Platform: Angular 21 SPA · .NET 10 minimal API · PostgreSQL · Docker

## Pages

- `/` — the den (member-profile home)
- `/links` — the directory (categories + copy-to-clipboard rows, served from the API when up, bundled data otherwise)
- `/forum` — the control room (invite-only): sign in / redeem an invite, then browse boards
- `/forum/b/:slug` — a board's threads (+ start a new thread)
- `/forum/t/:id` — a thread with replies (+ post a reply)

## The forum

Invite-only. There are no open registrations — you need a code. Accounts, sessions,
boards, threads, and posts are all Postgres-backed via the .NET API.

- **Auth:** PBKDF2 password hashing, 30-day bearer-token sessions. The Angular
  interceptor attaches the token; board/thread reads and all writes require a valid session.
- **Invites:** single-use codes. Admins mint them (`POST /api/admin/invites`); a
  member redeems one to create their account.
- **Bootstrap (local dev only):** first API start seeds an admin `roze` /
  `roze-local-dev`, three boards, a sticky welcome thread, and one open invite
  code **`WELCOME-TO-THE-DEN`**. Change these before any real deployment.

### API endpoints

| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/api/links` | — | public link directory |
| POST | `/api/auth/redeem` | — | create account from an invite code |
| POST | `/api/auth/login` | — | sign in |
| POST | `/api/auth/logout` | token | end session |
| GET | `/api/auth/me` | token | current member |
| GET | `/api/boards` | member | boards + counts |
| GET | `/api/boards/{slug}/threads` | member | threads in a board |
| POST | `/api/boards/{slug}/threads` | member | start a thread |
| GET | `/api/threads/{id}` | member | thread + posts |
| POST | `/api/threads/{id}/posts` | member | reply |
| GET / POST | `/api/admin/invites` | admin | list / mint invite codes |

## Running

**Full stack (docker):**

```bash
docker compose up --build
```

Open `http://localhost:8090`. nginx serves the SPA and proxies `/api` to the .NET
service; the API creates and seeds the Postgres schema on first start. Sign in at
`/forum` as `roze` / `roze-local-dev`, or redeem `WELCOME-TO-THE-DEN` to make your own account.

**Frontend only:**

```bash
npm install
npm start          # ng serve on http://localhost:4200, /api proxied to :8080
```

Without the API, the links page falls back to `src/app/data/links.data.ts`; the forum needs the API.

**API only:**

```bash
cd api
dotnet run         # http://localhost:8080 (expects local Postgres, see Program.cs)
```

## Editing content

- Links: edit the seed in `api/Program.cs` (system of record) and mirror in `src/app/data/links.data.ts` (static fallback).
- Profile/home copy: `src/app/pages/home/home.html`.
- Boards: the seed in `api/Program.cs` (`SeedForumAsync`).
- Theme tokens: `src/styles.scss` (`--red-*`, `--gold-*`, surfaces).

## Roadmap

- [x] Forum phase 1 — accounts & invite codes
- [x] Forum phase 2 — boards, threads, posts
- [ ] Forum phase 3 — kaomoji reactions, edit/delete, mod tools (lock/sticky UI)
- [ ] Email-backed invite delivery
- [ ] Deploy
