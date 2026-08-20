# rozenet ✦

Roze's den — a personal, non-work website with a GlitterNet-style design and a Makima motif (black, glitter red, gold). Home of the link directory (replacing [roze-tree](https://hazeliscoding.github.io/roze-tree/)) and, eventually, a private invite-only forum.

> 🖤 Platform: Angular 21 SPA · .NET 10 minimal API · PostgreSQL · Docker

## Pages

- `/` — the den (member-profile home)
- `/links` — the directory (categories + copy-to-clipboard rows, served from the API when up, bundled data otherwise)
- `/forum` — the control room (invite-only gate; real forum is roadmap)

## Running

**Full stack (docker):**

```bash
docker compose up --build
```

Open `http://localhost:8090`. nginx serves the SPA and proxies `/api` to the .NET service; the API creates and seeds the Postgres `links` tables on first start.

**Frontend only:**

```bash
npm install
npm start          # ng serve on http://localhost:4200, /api proxied to :8080
```

Without the API running, the links page falls back to `src/app/data/links.data.ts`.

**API only:**

```bash
cd api
dotnet run         # http://localhost:8080 (expects local Postgres, see Program.cs)
```

## Editing content

- Links: edit the seed in `api/Program.cs` (system of record) and mirror in `src/app/data/links.data.ts` (static fallback).
- Profile/home copy: `src/app/pages/home/home.html`.
- Theme tokens: `src/styles.scss` (`--red-*`, `--gold-*`, surfaces).

## Roadmap

- [ ] Forum phase 1 — accounts & invite codes (.NET + Postgres)
- [ ] Forum phase 2 — boards, threads, posts
- [ ] Forum phase 3 — kaomoji reactions, mod tools
- [ ] Deploy
