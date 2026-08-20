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

// Schema + seed. The links table is the system of record once the stack is
// up; the SPA falls back to bundled data when the API is unreachable.
await InitializeDatabaseAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Logger);

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

    return Results.Json(categories, JsonOptions);
});

app.Run();

static async Task InitializeDatabaseAsync(NpgsqlDataSource db, ILogger logger)
{
    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await using var setup = db.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS link_categories(
                    id SERIAL PRIMARY KEY,
                    title TEXT NOT NULL UNIQUE,
                    glyph TEXT NOT NULL,
                    sort_order INT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS links(
                    id SERIAL PRIMARY KEY,
                    category_id INT NOT NULL REFERENCES link_categories(id),
                    label TEXT NOT NULL,
                    url TEXT NOT NULL,
                    glyph TEXT NOT NULL,
                    description TEXT NOT NULL,
                    copy_text TEXT NULL,
                    sort_order INT NOT NULL
                );
                """);
            await setup.ExecuteNonQueryAsync();

            await using var count = db.CreateCommand("SELECT COUNT(*) FROM links");
            if ((long)(await count.ExecuteScalarAsync())! == 0)
            {
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

partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
}

record LinkCategory(string Title, string Glyph, List<LinkItem> Links);

record LinkItem(string Label, string Url, string Glyph, string Description, string? CopyText);
