using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Dev CORS so `ng serve` (4200) can hit the API directly; in the docker
// stack nginx proxies /api same-origin and this never comes into play.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));

var connectionString =
    builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Port=5432;Database=rozenet;Username=rozenet;Password=rozenet";

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

var app = builder.Build();
app.UseCors();

await Db.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Logger);

// ---- health + links directory ----

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/links", async (NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand(
        """
        SELECT c.title, c.glyph, l.label, l.url, l.glyph, l.description, l.copy_text
        FROM link_categories c
        JOIN links l ON l.category_id = c.id
        ORDER BY c.sort_order, l.sort_order
        """);

    var categories = new List<LinkCategory>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var title = reader.GetString(0);
        var category = categories.LastOrDefault(c => c.Title == title);
        if (category is null)
        {
            category = new LinkCategory(title, reader.GetString(1), []);
            categories.Add(category);
        }

        category.Links.Add(new LinkItem(
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6)));
    }

    return Results.Json(categories, Json.Options);
});

// ---- auth ----

app.MapPost("/api/auth/redeem", async (RedeemRequest req, NpgsqlDataSource db) =>
{
    var username = (req.Username ?? "").Trim();
    if (!Auth.IsValidUsername(username))
        return Results.BadRequest(new { error = "username must be 3–20 chars: letters, numbers, or underscore." });
    if ((req.Password ?? "").Length < 8)
        return Results.BadRequest(new { error = "password must be at least 8 characters." });
    var code = (req.Code ?? "").Trim().ToUpperInvariant();
    if (code.Length == 0)
        return Results.BadRequest(new { error = "an invite code is required. this den is invite-only." });

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    await using (var invite = new NpgsqlCommand(
        "SELECT redeemed_by FROM invites WHERE code = $1 FOR UPDATE", conn, tx))
    {
        invite.Parameters.AddWithValue(code);
        await using var r = await invite.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return Results.BadRequest(new { error = "that invite code isn't real." });
        if (!r.IsDBNull(0))
            return Results.BadRequest(new { error = "that invite has already been used." });
    }

    await using (var taken = new NpgsqlCommand(
        "SELECT 1 FROM users WHERE lower(username) = lower($1)", conn, tx))
    {
        taken.Parameters.AddWithValue(username);
        if (await taken.ExecuteScalarAsync() is not null)
            return Results.BadRequest(new { error = "that name is taken." });
    }

    var (hash, salt) = Auth.HashPassword(req.Password!);
    int userId;
    await using (var insert = new NpgsqlCommand(
        "INSERT INTO users(username, password_hash, password_salt, is_admin) VALUES($1,$2,$3,false) RETURNING id",
        conn, tx))
    {
        insert.Parameters.AddWithValue(username);
        insert.Parameters.AddWithValue(hash);
        insert.Parameters.AddWithValue(salt);
        userId = (int)(await insert.ExecuteScalarAsync())!;
    }

    await using (var mark = new NpgsqlCommand(
        "UPDATE invites SET redeemed_by = $1, redeemed_at = now() WHERE code = $2", conn, tx))
    {
        mark.Parameters.AddWithValue(userId);
        mark.Parameters.AddWithValue(code);
        await mark.ExecuteNonQueryAsync();
    }

    var token = await Auth.CreateSessionAsync(conn, tx, userId);
    await tx.CommitAsync();

    return Results.Json(new AuthResponse(token, new UserDto(userId, username, false)), Json.Options);
});

app.MapPost("/api/auth/login", async (LoginRequest req, NpgsqlDataSource db) =>
{
    var username = (req.Username ?? "").Trim();
    await using var conn = await db.OpenConnectionAsync();

    int id; string hash, salt; bool isAdmin;
    await using (var cmd = new NpgsqlCommand(
        "SELECT id, password_hash, password_salt, is_admin FROM users WHERE lower(username) = lower($1)", conn))
    {
        cmd.Parameters.AddWithValue(username);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return Results.Json(new { error = "wrong name or password." }, Json.Options, statusCode: 401);
        id = r.GetInt32(0); hash = r.GetString(1); salt = r.GetString(2); isAdmin = r.GetBoolean(3);
    }

    if (!Auth.VerifyPassword(req.Password ?? "", hash, salt))
        return Results.Json(new { error = "wrong name or password." }, Json.Options, statusCode: 401);

    var token = await Auth.CreateSessionAsync(conn, null, id);
    return Results.Json(new AuthResponse(token, new UserDto(id, username, isAdmin)), Json.Options);
});

app.MapPost("/api/auth/logout", async (HttpContext ctx, NpgsqlDataSource db) =>
{
    var token = Auth.BearerToken(ctx);
    if (token is not null)
    {
        await using var cmd = db.CreateCommand("DELETE FROM sessions WHERE token = $1");
        cmd.Parameters.AddWithValue(token);
        await cmd.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/auth/me", async (HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    return user is null
        ? Results.Json(new { error = "not signed in." }, Json.Options, statusCode: 401)
        : Results.Json(new UserDto(user.Id, user.Username, user.IsAdmin), Json.Options);
});

// ---- forum: boards ----

app.MapGet("/api/boards", async (HttpContext ctx, NpgsqlDataSource db) =>
{
    if (await Auth.ResolveAsync(ctx, db) is null)
        return Results.Json(new { error = "members only." }, Json.Options, statusCode: 401);

    await using var cmd = db.CreateCommand(
        """
        SELECT b.slug, b.title, b.description, b.glyph,
               COUNT(t.id) AS threads,
               COALESCE(SUM((SELECT COUNT(*) FROM posts p WHERE p.thread_id = t.id)), 0) AS posts
        FROM boards b
        LEFT JOIN threads t ON t.board_id = b.id
        GROUP BY b.id
        ORDER BY b.sort_order
        """);

    var boards = new List<BoardDto>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        boards.Add(new BoardDto(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            (int)r.GetInt64(4), (int)r.GetInt64(5)));

    return Results.Json(boards, Json.Options);
});

app.MapGet("/api/boards/{slug}/threads", async (string slug, HttpContext ctx, NpgsqlDataSource db) =>
{
    if (await Auth.ResolveAsync(ctx, db) is null)
        return Results.Json(new { error = "members only." }, Json.Options, statusCode: 401);

    await using var cmd = db.CreateCommand(
        """
        SELECT t.id, t.title, u.username, t.created_at, t.last_post_at, t.locked, t.sticky,
               (SELECT COUNT(*) FROM posts p WHERE p.thread_id = t.id) - 1 AS replies
        FROM threads t
        JOIN users u ON u.id = t.author_id
        JOIN boards b ON b.id = t.board_id
        WHERE b.slug = $1
        ORDER BY t.sticky DESC, t.last_post_at DESC
        """);
    cmd.Parameters.AddWithValue(slug);

    var threads = new List<ThreadRowDto>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        threads.Add(new ThreadRowDto(r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.GetDateTime(3), r.GetDateTime(4), r.GetBoolean(5), r.GetBoolean(6), (int)r.GetInt64(7)));

    return Results.Json(threads, Json.Options);
});

app.MapPost("/api/boards/{slug}/threads", async (string slug, NewThreadRequest req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null) return Results.Json(new { error = "sign in first." }, Json.Options, statusCode: 401);

    var title = (req.Title ?? "").Trim();
    var body = (req.Body ?? "").Trim();
    if (title.Length is < 3 or > 140) return Results.BadRequest(new { error = "title must be 3–140 chars." });
    if (body.Length < 1) return Results.BadRequest(new { error = "say something in the first post." });

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    int boardId;
    await using (var b = new NpgsqlCommand("SELECT id FROM boards WHERE slug = $1", conn, tx))
    {
        b.Parameters.AddWithValue(slug);
        if (await b.ExecuteScalarAsync() is not int id)
            return Results.NotFound(new { error = "no such board." });
        boardId = id;
    }

    int threadId;
    await using (var t = new NpgsqlCommand(
        "INSERT INTO threads(board_id, title, author_id) VALUES($1,$2,$3) RETURNING id", conn, tx))
    {
        t.Parameters.AddWithValue(boardId);
        t.Parameters.AddWithValue(title);
        t.Parameters.AddWithValue(user.Id);
        threadId = (int)(await t.ExecuteScalarAsync())!;
    }

    await using (var p = new NpgsqlCommand(
        "INSERT INTO posts(thread_id, author_id, body) VALUES($1,$2,$3)", conn, tx))
    {
        p.Parameters.AddWithValue(threadId);
        p.Parameters.AddWithValue(user.Id);
        p.Parameters.AddWithValue(body);
        await p.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
    return Results.Json(new { id = threadId }, Json.Options);
});

// ---- forum: threads + posts ----

app.MapGet("/api/threads/{id:int}", async (int id, HttpContext ctx, NpgsqlDataSource db) =>
{
    if (await Auth.ResolveAsync(ctx, db) is null)
        return Results.Json(new { error = "members only." }, Json.Options, statusCode: 401);

    ThreadHeadDto head;
    await using (var cmd = db.CreateCommand(
        """
        SELECT t.id, t.title, b.slug, b.title, t.locked, t.sticky
        FROM threads t JOIN boards b ON b.id = t.board_id
        WHERE t.id = $1
        """))
    {
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return Results.NotFound(new { error = "thread not found." });
        head = new ThreadHeadDto(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.GetBoolean(4), r.GetBoolean(5));
    }

    var posts = new List<PostDto>();
    await using (var cmd = db.CreateCommand(
        """
        SELECT p.id, u.username, p.body, p.created_at
        FROM posts p JOIN users u ON u.id = p.author_id
        WHERE p.thread_id = $1
        ORDER BY p.created_at
        """))
    {
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            posts.Add(new PostDto(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDateTime(3)));
    }

    return Results.Json(new ThreadViewDto(head, posts), Json.Options);
});

app.MapPost("/api/threads/{id:int}/posts", async (int id, NewPostRequest req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null) return Results.Json(new { error = "sign in first." }, Json.Options, statusCode: 401);

    var body = (req.Body ?? "").Trim();
    if (body.Length < 1) return Results.BadRequest(new { error = "empty reply." });

    await using var conn = await db.OpenConnectionAsync();

    bool locked;
    await using (var t = new NpgsqlCommand("SELECT locked FROM threads WHERE id = $1", conn))
    {
        t.Parameters.AddWithValue(id);
        if (await t.ExecuteScalarAsync() is not bool l)
            return Results.NotFound(new { error = "thread not found." });
        locked = l;
    }
    if (locked) return Results.Json(new { error = "this thread is locked." }, Json.Options, statusCode: 403);

    await using (var p = new NpgsqlCommand(
        "INSERT INTO posts(thread_id, author_id, body) VALUES($1,$2,$3)", conn))
    {
        p.Parameters.AddWithValue(id);
        p.Parameters.AddWithValue(user.Id);
        p.Parameters.AddWithValue(body);
        await p.ExecuteNonQueryAsync();
    }
    await using (var u = new NpgsqlCommand("UPDATE threads SET last_post_at = now() WHERE id = $1", conn))
    {
        u.Parameters.AddWithValue(id);
        await u.ExecuteNonQueryAsync();
    }

    return Results.Json(new { ok = true }, Json.Options);
});

// ---- admin: mint invites ----

app.MapPost("/api/admin/invites", async (HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null || !user.IsAdmin)
        return Results.Json(new { error = "the leash is not yours to hold." }, Json.Options, statusCode: 403);

    var code = Auth.NewInviteCode();
    await using var cmd = db.CreateCommand("INSERT INTO invites(code, created_by) VALUES($1,$2)");
    cmd.Parameters.AddWithValue(code);
    cmd.Parameters.AddWithValue(user.Id);
    await cmd.ExecuteNonQueryAsync();

    return Results.Json(new { code }, Json.Options);
});

app.MapGet("/api/admin/invites", async (HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null || !user.IsAdmin)
        return Results.Json(new { error = "the leash is not yours to hold." }, Json.Options, statusCode: 403);

    await using var cmd = db.CreateCommand(
        """
        SELECT i.code, u.username, i.created_at, i.redeemed_at
        FROM invites i LEFT JOIN users u ON u.id = i.redeemed_by
        ORDER BY i.created_at DESC
        """);
    var invites = new List<InviteDto>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        invites.Add(new InviteDto(r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.GetDateTime(2),
            r.IsDBNull(3) ? null : r.GetDateTime(3)));

    return Results.Json(invites, Json.Options);
});

app.Run();

// ============================================================
// helpers
// ============================================================

static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

static class Auth
{
    public static bool IsValidUsername(string u) =>
        u.Length is >= 3 and <= 20 && u.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static (string hash, string salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        var expected = Convert.FromBase64String(hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, Convert.FromBase64String(salt), 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string NewInviteCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars
        var chars = RandomNumberGenerator.GetItems<char>(alphabet, 12);
        return $"{new string(chars[..4])}-{new string(chars[4..8])}-{new string(chars[8..])}";
    }

    public static string? BearerToken(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header["Bearer ".Length..].Trim() is { Length: > 0 } t ? t : null
            : null;
    }

    public static async Task<string> CreateSessionAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, int userId)
    {
        var token = NewToken();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sessions(token, user_id, expires_at) VALUES($1,$2, now() + interval '30 days')",
            conn, tx);
        cmd.Parameters.AddWithValue(token);
        cmd.Parameters.AddWithValue(userId);
        await cmd.ExecuteNonQueryAsync();
        return token;
    }

    public static async Task<CurrentUser?> ResolveAsync(HttpContext ctx, NpgsqlDataSource db)
    {
        var token = BearerToken(ctx);
        if (token is null) return null;

        await using var cmd = db.CreateCommand(
            """
            SELECT u.id, u.username, u.is_admin
            FROM sessions s JOIN users u ON u.id = s.user_id
            WHERE s.token = $1 AND s.expires_at > now()
            """);
        cmd.Parameters.AddWithValue(token);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new CurrentUser(r.GetInt32(0), r.GetString(1), r.GetBoolean(2))
            : null;
    }
}

static class Db
{
    // Bootstrap admin — LOCAL DEV ONLY (this is a public repo). Change before
    // any real deployment; a deployed instance should seed from a secret.
    private const string BootstrapAdmin = "roze";
    private const string BootstrapPassword = "roze-local-dev";
    private const string BootstrapInvite = "WELCOME-TO-THE-DEN";

    public static async Task InitializeAsync(NpgsqlDataSource db, ILogger logger)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CreateSchemaAsync(db);
                await SeedLinksAsync(db, logger);
                await SeedForumAsync(db, logger);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static async Task CreateSchemaAsync(NpgsqlDataSource db)
    {
        await using var cmd = db.CreateCommand(
            """
            CREATE TABLE IF NOT EXISTS link_categories(
                id SERIAL PRIMARY KEY, title TEXT NOT NULL UNIQUE, glyph TEXT NOT NULL, sort_order INT NOT NULL);
            CREATE TABLE IF NOT EXISTS links(
                id SERIAL PRIMARY KEY,
                category_id INT NOT NULL REFERENCES link_categories(id),
                label TEXT NOT NULL, url TEXT NOT NULL, glyph TEXT NOT NULL,
                description TEXT NOT NULL, copy_text TEXT NULL, sort_order INT NOT NULL);

            CREATE TABLE IF NOT EXISTS users(
                id SERIAL PRIMARY KEY,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                is_admin BOOLEAN NOT NULL DEFAULT false,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now());
            CREATE TABLE IF NOT EXISTS invites(
                code TEXT PRIMARY KEY,
                created_by INT NULL REFERENCES users(id),
                redeemed_by INT NULL REFERENCES users(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                redeemed_at TIMESTAMPTZ NULL);
            CREATE TABLE IF NOT EXISTS sessions(
                token TEXT PRIMARY KEY,
                user_id INT NOT NULL REFERENCES users(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                expires_at TIMESTAMPTZ NOT NULL);
            CREATE TABLE IF NOT EXISTS boards(
                id SERIAL PRIMARY KEY,
                slug TEXT NOT NULL UNIQUE, title TEXT NOT NULL,
                description TEXT NOT NULL, glyph TEXT NOT NULL, sort_order INT NOT NULL);
            CREATE TABLE IF NOT EXISTS threads(
                id SERIAL PRIMARY KEY,
                board_id INT NOT NULL REFERENCES boards(id),
                title TEXT NOT NULL,
                author_id INT NOT NULL REFERENCES users(id),
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                last_post_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                locked BOOLEAN NOT NULL DEFAULT false,
                sticky BOOLEAN NOT NULL DEFAULT false);
            CREATE TABLE IF NOT EXISTS posts(
                id SERIAL PRIMARY KEY,
                thread_id INT NOT NULL REFERENCES threads(id),
                author_id INT NOT NULL REFERENCES users(id),
                body TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now());
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedLinksAsync(NpgsqlDataSource db, ILogger logger)
    {
        await using var count = db.CreateCommand("SELECT COUNT(*) FROM links");
        if ((long)(await count.ExecuteScalarAsync())! > 0) return;

        await using var seed = db.CreateCommand(
            """
            INSERT INTO link_categories(title, glyph, sort_order) VALUES
                ('watching & playing', '►', 1),
                ('code & chat', '✎', 2);
            INSERT INTO links(category_id, label, url, glyph, description, copy_text, sort_order) VALUES
                (1, 'AniList', 'https://anilist.co/user/RozeAngel', '★', 'anime & manga i''m watching/reading', NULL, 1),
                (1, 'MyFigureCollection', 'https://myfigurecollection.net/profile/RozeAngel', '♥', 'figures i own + the wishlist that haunts me', NULL, 2),
                (1, 'Backloggd', 'https://backloggd.com/u/RozeAngel', '●', 'games i''m playing + the eternal backlog', NULL, 3),
                (2, 'GitHub', 'https://github.com/hazeliscoding', '⌂', 'code, projects, and cute web experiments', NULL, 1),
                (2, 'Discord', 'https://discord.com/', '✉', 'tap to copy my handle', 'roze.angel', 2);
            """);
        await seed.ExecuteNonQueryAsync();
        logger.LogInformation("Seeded link directory");
    }

    private static async Task SeedForumAsync(NpgsqlDataSource db, ILogger logger)
    {
        await using var count = db.CreateCommand("SELECT COUNT(*) FROM users");
        if ((long)(await count.ExecuteScalarAsync())! > 0) return;

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var (hash, salt) = Auth.HashPassword(BootstrapPassword);
        int adminId;
        await using (var u = new NpgsqlCommand(
            "INSERT INTO users(username, password_hash, password_salt, is_admin) VALUES($1,$2,$3,true) RETURNING id",
            conn, tx))
        {
            u.Parameters.AddWithValue(BootstrapAdmin);
            u.Parameters.AddWithValue(hash);
            u.Parameters.AddWithValue(salt);
            adminId = (int)(await u.ExecuteScalarAsync())!;
        }

        await using (var inv = new NpgsqlCommand(
            "INSERT INTO invites(code, created_by) VALUES($1,$2)", conn, tx))
        {
            inv.Parameters.AddWithValue(BootstrapInvite);
            inv.Parameters.AddWithValue(adminId);
            await inv.ExecuteNonQueryAsync();
        }

        await using (var b = new NpgsqlCommand(
            """
            INSERT INTO boards(slug, title, description, glyph, sort_order) VALUES
                ('the-den', 'the den', 'general chatter, introductions, whatever', '⌂', 1),
                ('screening-room', 'screening room', 'anime & manga — currently watching, recs, spoilers (tag them)', '►', 2),
                ('arcade', 'the arcade', 'games we''re playing and the backlog we''re avoiding', '●', 3)
            RETURNING id
            """, conn, tx))
        {
            await using var r = await b.ExecuteReaderAsync();
            await r.ReadAsync(); // first board id = the-den
        }

        int denId;
        await using (var find = new NpgsqlCommand("SELECT id FROM boards WHERE slug = 'the-den'", conn, tx))
            denId = (int)(await find.ExecuteScalarAsync())!;

        int threadId;
        await using (var t = new NpgsqlCommand(
            "INSERT INTO threads(board_id, title, author_id, sticky) VALUES($1,$2,$3,true) RETURNING id", conn, tx))
        {
            t.Parameters.AddWithValue(denId);
            t.Parameters.AddWithValue("welcome to the den ♥");
            t.Parameters.AddWithValue(adminId);
            threadId = (int)(await t.ExecuteScalarAsync())!;
        }

        await using (var p = new NpgsqlCommand(
            "INSERT INTO posts(thread_id, author_id, body) VALUES($1,$2,$3)", conn, tx))
        {
            p.Parameters.AddWithValue(threadId);
            p.Parameters.AddWithValue(adminId);
            p.Parameters.AddWithValue(
                "you made it in. be kind, tag your spoilers, and don't make me use the leash.\n\n— roze");
            await p.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        logger.LogInformation("Seeded forum (admin '{Admin}', bootstrap invite '{Invite}')",
            BootstrapAdmin, BootstrapInvite);
    }
}

// ============================================================
// DTOs
// ============================================================

record LinkCategory(string Title, string Glyph, List<LinkItem> Links);
record LinkItem(string Label, string Url, string Glyph, string Description, string? CopyText);

record RedeemRequest(string? Code, string? Username, string? Password);
record LoginRequest(string? Username, string? Password);
record NewThreadRequest(string? Title, string? Body);
record NewPostRequest(string? Body);

record CurrentUser(int Id, string Username, bool IsAdmin);
record UserDto(int Id, string Username, bool IsAdmin);
record AuthResponse(string Token, UserDto User);

record BoardDto(string Slug, string Title, string Description, string Glyph, int Threads, int Posts);
record ThreadRowDto(int Id, string Title, string Author, DateTime CreatedAt, DateTime LastPostAt,
    bool Locked, bool Sticky, int Replies);
record ThreadHeadDto(int Id, string Title, string BoardSlug, string BoardTitle, bool Locked, bool Sticky);
record PostDto(int Id, string Author, string Body, DateTime CreatedAt);
record ThreadViewDto(ThreadHeadDto Thread, List<PostDto> Posts);
record InviteDto(string Code, string? RedeemedBy, DateTime CreatedAt, DateTime? RedeemedAt);
