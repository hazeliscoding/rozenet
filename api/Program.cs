using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Railway (and most PaaS) inject a PORT to listen on. Locally this is unset
// and the Dockerfile's ASPNETCORE_URLS (8080) applies.
var port = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Dev CORS so `ng serve` (4200) can hit the API directly; in the docker
// stack nginx proxies /api same-origin and this never comes into play.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));

var connectionString = ResolveConnectionString(builder.Configuration);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

// Email sender: real SMTP when Smtp:Host is set, else a dev logger.
var smtpHost = builder.Configuration["Smtp:Host"];
if (!string.IsNullOrWhiteSpace(smtpHost))
{
    var opts = new SmtpOptions(
        smtpHost,
        int.TryParse(builder.Configuration["Smtp:Port"], out var p) ? p : 587,
        builder.Configuration["Smtp:User"],
        builder.Configuration["Smtp:Pass"],
        builder.Configuration["Smtp:From"] ?? "roze@rozenet.local",
        builder.Configuration["Smtp:FromName"] ?? "roze");
    builder.Services.AddSingleton(opts);
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
}

// Base URL used to build invite links (nginx origin in the docker stack).
var appBaseUrl = (builder.Configuration["App:BaseUrl"] ?? "http://localhost:8090").TrimEnd('/');

var app = builder.Build();

// Canonical host: 301 www.* → the bare apex (keeps one canonical URL).
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host;
    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Redirect(
            $"https://{host[4..]}{ctx.Request.Path}{ctx.Request.QueryString}", permanent: true);
        return;
    }
    await next();
});

app.UseCors();

// Serve the built Angular SPA when it's bundled in (the combined Railway image
// copies it to wwwroot). Harmless locally where wwwroot is empty and the SPA is
// served by nginx / ng serve instead.
app.UseDefaultFiles();
app.UseStaticFiles();

// Bootstrap admin credentials. Defaults are LOCAL DEV ONLY (visible in this
// public repo) — a real deployment MUST override Bootstrap__Password (and
// ideally Bootstrap__Admin / Bootstrap__Invite) via env vars.
var bootstrap = new BootstrapConfig(
    builder.Configuration["Bootstrap:Admin"] ?? "roze",
    builder.Configuration["Bootstrap:Password"] ?? "roze-local-dev",
    builder.Configuration["Bootstrap:Invite"] ?? "WELCOME-TO-THE-DEN");

await Db.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Logger, bootstrap);

// ---- health + links directory ----

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/version", (IConfiguration cfg) =>
{
    // Railway injects git metadata at runtime for GitHub-sourced deploys.
    var sha = cfg["RAILWAY_GIT_COMMIT_SHA"];
    return Results.Ok(new
    {
        app = "rozenet",
        commit = string.IsNullOrEmpty(sha) ? "dev" : sha[..Math.Min(7, sha.Length)],
        branch = cfg["RAILWAY_GIT_BRANCH"] ?? "local",
    });
});

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
    var viewer = await Auth.ResolveAsync(ctx, db);
    if (viewer is null) return Results.Json(new { error = "members only." }, Json.Options, statusCode: 401);

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

    // Gather reactions for the thread's posts, grouped per post with a "mine" flag.
    var byPost = new Dictionary<int, Dictionary<string, (int Count, bool Mine)>>();
    await using (var cmd = db.CreateCommand(
        """
        SELECT r.post_id, r.kaomoji, r.user_id
        FROM reactions r JOIN posts p ON p.id = r.post_id
        WHERE p.thread_id = $1
        """))
    {
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var postId = r.GetInt32(0);
            var kao = r.GetString(1);
            var mine = r.GetInt32(2) == viewer.Id;
            var group = byPost.TryGetValue(postId, out var g) ? g : byPost[postId] = new();
            var (count, wasMine) = group.TryGetValue(kao, out var v) ? v : (0, false);
            group[kao] = (count + 1, wasMine || mine);
        }
    }

    var posts = new List<PostDto>();
    await using (var cmd = db.CreateCommand(
        """
        SELECT p.id, u.username, p.author_id, p.body, p.created_at, p.edited_at
        FROM posts p JOIN users u ON u.id = p.author_id
        WHERE p.thread_id = $1
        ORDER BY p.created_at, p.id
        """))
    {
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var postId = r.GetInt32(0);
            var reactions = byPost.TryGetValue(postId, out var g)
                ? g.Select(kv => new ReactionDto(kv.Key, kv.Value.Count, kv.Value.Mine)).ToList()
                : [];
            posts.Add(new PostDto(postId, r.GetString(1), r.GetInt32(2), r.GetString(3),
                r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetDateTime(5), reactions));
        }
    }

    return Results.Json(new ThreadViewDto(head, posts, viewer.Id, viewer.IsAdmin), Json.Options);
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

// ---- reactions ----

app.MapPost("/api/posts/{id:int}/reactions", async (int id, ReactionRequest req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null) return Results.Json(new { error = "sign in first." }, Json.Options, statusCode: 401);

    var kao = (req.Kaomoji ?? "").Trim();
    if (!Reactions.Allowed.Contains(kao))
        return Results.BadRequest(new { error = "that's not a reaction you can use." });

    await using var conn = await db.OpenConnectionAsync();

    // Toggle: remove if the member already reacted with this kaomoji, else add.
    await using (var del = new NpgsqlCommand(
        "DELETE FROM reactions WHERE post_id = $1 AND user_id = $2 AND kaomoji = $3", conn))
    {
        del.Parameters.AddWithValue(id);
        del.Parameters.AddWithValue(user.Id);
        del.Parameters.AddWithValue(kao);
        if (await del.ExecuteNonQueryAsync() == 0)
        {
            await using var ins = new NpgsqlCommand(
                "INSERT INTO reactions(post_id, user_id, kaomoji) VALUES($1,$2,$3)", conn);
            ins.Parameters.AddWithValue(id);
            ins.Parameters.AddWithValue(user.Id);
            ins.Parameters.AddWithValue(kao);
            await ins.ExecuteNonQueryAsync();
        }
    }

    // Return the fresh summary for this post.
    var summary = new List<ReactionDto>();
    await using (var cmd = new NpgsqlCommand(
        """
        SELECT kaomoji, COUNT(*), bool_or(user_id = $2)
        FROM reactions WHERE post_id = $1
        GROUP BY kaomoji
        """, conn))
    {
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(user.Id);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            summary.Add(new ReactionDto(r.GetString(0), (int)r.GetInt64(1), r.GetBoolean(2)));
    }

    return Results.Json(summary, Json.Options);
});

// ---- edit / delete posts ----

app.MapPatch("/api/posts/{id:int}", async (int id, NewPostRequest req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null) return Results.Json(new { error = "sign in first." }, Json.Options, statusCode: 401);

    var body = (req.Body ?? "").Trim();
    if (body.Length < 1) return Results.BadRequest(new { error = "empty post." });

    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        "UPDATE posts SET body = $1, edited_at = now() WHERE id = $2 AND author_id = $3", conn);
    cmd.Parameters.AddWithValue(body);
    cmd.Parameters.AddWithValue(id);
    cmd.Parameters.AddWithValue(user.Id);
    // Only the author may edit — a 0-row update means not theirs (or gone).
    if (await cmd.ExecuteNonQueryAsync() == 0)
        return Results.Json(new { error = "you can only edit your own posts." }, Json.Options, statusCode: 403);

    return Results.Json(new { ok = true }, Json.Options);
});

app.MapDelete("/api/posts/{id:int}", async (int id, HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null) return Results.Json(new { error = "sign in first." }, Json.Options, statusCode: 401);

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    int authorId, threadId;
    await using (var find = new NpgsqlCommand("SELECT author_id, thread_id FROM posts WHERE id = $1", conn, tx))
    {
        find.Parameters.AddWithValue(id);
        await using var r = await find.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return Results.NotFound(new { error = "post not found." });
        authorId = r.GetInt32(0);
        threadId = r.GetInt32(1);
    }

    if (authorId != user.Id && !user.IsAdmin)
        return Results.Json(new { error = "not yours to delete." }, Json.Options, statusCode: 403);

    // Is this the opening post? If so, deleting it removes the whole thread.
    int firstPostId;
    await using (var first = new NpgsqlCommand(
        "SELECT id FROM posts WHERE thread_id = $1 ORDER BY created_at, id LIMIT 1", conn, tx))
    {
        first.Parameters.AddWithValue(threadId);
        firstPostId = (int)(await first.ExecuteScalarAsync())!;
    }

    var threadDeleted = firstPostId == id;
    if (threadDeleted)
    {
        // reactions cascade from posts; remove posts then the thread.
        await Exec(conn, tx, "DELETE FROM posts WHERE thread_id = $1", threadId);
        await Exec(conn, tx, "DELETE FROM threads WHERE id = $1", threadId);
    }
    else
    {
        await Exec(conn, tx, "DELETE FROM posts WHERE id = $1", id);
        await using var bump = new NpgsqlCommand(
            "UPDATE threads SET last_post_at = (SELECT MAX(created_at) FROM posts WHERE thread_id = $1) WHERE id = $1",
            conn, tx);
        bump.Parameters.AddWithValue(threadId);
        await bump.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
    return Results.Json(new { threadDeleted, threadId }, Json.Options);

    static async Task Exec(NpgsqlConnection c, NpgsqlTransaction t, string sql, int arg)
    {
        await using var cmd = new NpgsqlCommand(sql, c, t);
        cmd.Parameters.AddWithValue(arg);
        await cmd.ExecuteNonQueryAsync();
    }
});

// ---- mod tools: lock / sticky ----

app.MapPost("/api/threads/{id:int}/moderate", async (int id, ModerateRequest req, HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null || !user.IsAdmin)
        return Results.Json(new { error = "the leash is not yours to hold." }, Json.Options, statusCode: 403);

    var sets = new List<string>();
    if (req.Locked is not null) sets.Add($"locked = {(req.Locked.Value ? "true" : "false")}");
    if (req.Sticky is not null) sets.Add($"sticky = {(req.Sticky.Value ? "true" : "false")}");
    if (sets.Count == 0) return Results.BadRequest(new { error = "nothing to change." });

    await using var cmd = db.CreateCommand($"UPDATE threads SET {string.Join(", ", sets)} WHERE id = $1");
    cmd.Parameters.AddWithValue(id);
    if (await cmd.ExecuteNonQueryAsync() == 0)
        return Results.NotFound(new { error = "thread not found." });

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

    return Results.Json(new { code, link = $"{appBaseUrl}/forum?invite={code}" }, Json.Options);
});

app.MapPost("/api/admin/invites/email", async (EmailInviteRequest req, HttpContext ctx, NpgsqlDataSource db, IEmailSender email) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null || !user.IsAdmin)
        return Results.Json(new { error = "the leash is not yours to hold." }, Json.Options, statusCode: 403);

    var address = (req.Email ?? "").Trim();
    if (!address.Contains('@') || address.Length < 5)
        return Results.BadRequest(new { error = "that doesn't look like an email." });

    var code = Auth.NewInviteCode();
    await using (var cmd = db.CreateCommand(
        "INSERT INTO invites(code, created_by, invited_email) VALUES($1,$2,$3)"))
    {
        cmd.Parameters.AddWithValue(code);
        cmd.Parameters.AddWithValue(user.Id);
        cmd.Parameters.AddWithValue(address);
        await cmd.ExecuteNonQueryAsync();
    }

    var link = $"{appBaseUrl}/forum?invite={code}";
    var emailed = await email.SendInviteAsync(address, link);

    // The link is returned so the admin can copy it (and always, in dev, where
    // no mail is actually sent). code is included for convenience.
    return Results.Json(new { code, link, emailed }, Json.Options);
});

app.MapGet("/api/admin/invites", async (HttpContext ctx, NpgsqlDataSource db) =>
{
    var user = await Auth.ResolveAsync(ctx, db);
    if (user is null || !user.IsAdmin)
        return Results.Json(new { error = "the leash is not yours to hold." }, Json.Options, statusCode: 403);

    await using var cmd = db.CreateCommand(
        """
        SELECT i.code, i.invited_email, u.username, i.created_at, i.redeemed_at
        FROM invites i LEFT JOIN users u ON u.id = i.redeemed_by
        ORDER BY i.created_at DESC
        """);
    var invites = new List<InviteDto>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        invites.Add(new InviteDto(r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.GetDateTime(3),
            r.IsDBNull(4) ? null : r.GetDateTime(4)));

    return Results.Json(invites, Json.Options);
});

// SPA fallback: any non-API route serves index.html so client-side routing works
// (only matters when wwwroot is populated, i.e. the combined image).
app.MapFallbackToFile("index.html");

app.Run();

// ============================================================
// helpers
// ============================================================

// Prefer an explicit Npgsql connection string (ConnectionStrings__Db); otherwise
// accept a postgres:// URL (Railway's DATABASE_URL) and convert it.
static string ResolveConnectionString(IConfiguration cfg)
{
    var explicitCs = cfg.GetConnectionString("Db");
    if (!string.IsNullOrWhiteSpace(explicitCs)) return explicitCs;

    var url = cfg["DATABASE_URL"];
    if (!string.IsNullOrWhiteSpace(url))
    {
        var uri = new Uri(url);
        var creds = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(creds[0]),
            Password = creds.Length > 1 ? Uri.UnescapeDataString(creds[1]) : "",
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Prefer,
            TrustServerCertificate = true,
        }.ConnectionString;
    }

    return "Host=localhost;Port=5432;Database=rozenet;Username=rozenet;Password=rozenet";
}

static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

static class Reactions
{
    // The only reactions members can use — kaomoji + dingbats, on brand.
    public static readonly HashSet<string> Allowed =
        ["♥", "☆", "✧", "(＾▽＾)", "(=^･ω･^=)", "orz"];
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
    public static async Task InitializeAsync(NpgsqlDataSource db, ILogger logger, BootstrapConfig bootstrap)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CreateSchemaAsync(db);
                await SeedLinksAsync(db, logger);
                await SeedForumAsync(db, logger, bootstrap);
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
            ALTER TABLE invites ADD COLUMN IF NOT EXISTS invited_email TEXT NULL;
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
            ALTER TABLE posts ADD COLUMN IF NOT EXISTS edited_at TIMESTAMPTZ NULL;
            CREATE TABLE IF NOT EXISTS reactions(
                id SERIAL PRIMARY KEY,
                post_id INT NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
                user_id INT NOT NULL REFERENCES users(id),
                kaomoji TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                UNIQUE(post_id, user_id, kaomoji));
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

    private static async Task SeedForumAsync(NpgsqlDataSource db, ILogger logger, BootstrapConfig bootstrap)
    {
        await using var count = db.CreateCommand("SELECT COUNT(*) FROM users");
        if ((long)(await count.ExecuteScalarAsync())! > 0) return;

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var (hash, salt) = Auth.HashPassword(bootstrap.Password);
        int adminId;
        await using (var u = new NpgsqlCommand(
            "INSERT INTO users(username, password_hash, password_salt, is_admin) VALUES($1,$2,$3,true) RETURNING id",
            conn, tx))
        {
            u.Parameters.AddWithValue(bootstrap.Admin);
            u.Parameters.AddWithValue(hash);
            u.Parameters.AddWithValue(salt);
            adminId = (int)(await u.ExecuteScalarAsync())!;
        }

        await using (var inv = new NpgsqlCommand(
            "INSERT INTO invites(code, created_by) VALUES($1,$2)", conn, tx))
        {
            inv.Parameters.AddWithValue(bootstrap.Invite);
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
            bootstrap.Admin, bootstrap.Invite);
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
record ReactionRequest(string? Kaomoji);
record ModerateRequest(bool? Locked, bool? Sticky);
record EmailInviteRequest(string? Email);
record BootstrapConfig(string Admin, string Password, string Invite);

record CurrentUser(int Id, string Username, bool IsAdmin);
record UserDto(int Id, string Username, bool IsAdmin);
record AuthResponse(string Token, UserDto User);

record BoardDto(string Slug, string Title, string Description, string Glyph, int Threads, int Posts);
record ThreadRowDto(int Id, string Title, string Author, DateTime CreatedAt, DateTime LastPostAt,
    bool Locked, bool Sticky, int Replies);
record ThreadHeadDto(int Id, string Title, string BoardSlug, string BoardTitle, bool Locked, bool Sticky);
record ReactionDto(string Kaomoji, int Count, bool Mine);
record PostDto(int Id, string Author, int AuthorId, string Body, DateTime CreatedAt,
    DateTime? EditedAt, List<ReactionDto> Reactions);
record ThreadViewDto(ThreadHeadDto Thread, List<PostDto> Posts, int ViewerId, bool ViewerIsAdmin);
record InviteDto(string Code, string? InvitedEmail, string? RedeemedBy, DateTime CreatedAt, DateTime? RedeemedAt);
