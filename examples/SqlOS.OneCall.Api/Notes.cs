using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using SqlOS.Extensions;
using SqlOS.Fga.Interfaces;
using SqlOS.Mcp;

namespace SqlOS.OneCall.Api;

/// <summary>FGA vocabulary seeded through <c>app.Authorization(...)</c> and checked by <see cref="NotesService"/>.</summary>
public static class NotesAuthorization
{
    public const string NotebookType = "notebook";
    public const string OwnerRole = "notebook_owner";
    public const string ReadPermission = "NOTES_READ";
    public const string WritePermission = "NOTES_WRITE";

    public static string NotebookId(string userId) => $"notebook::{userId}";
}

public sealed class Note
{
    public Guid Id { get; set; }
    public string NotebookId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed record NoteRequest(string Text);

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options)
    : SqlOSDbContext<NotesDbContext>(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnApplicationModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NotebookId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            entity.HasIndex(x => x.NotebookId);
        });
    }
}

/// <summary>
/// The single domain service behind both surfaces. The API handlers and the MCP tools call it with
/// the user SqlOS already authenticated, and it enforces FGA on the user's notebook.
/// </summary>
public sealed class NotesService(NotesDbContext db, ISqlOSFgaAuthService fga)
{
    public async Task<IReadOnlyList<Note>> ListAsync(string userId, CancellationToken ct)
    {
        var notebookId = await EnsureNotebookAsync(userId, NotesAuthorization.ReadPermission, ct);
        return await db.Notes.Where(n => n.NotebookId == notebookId).OrderBy(n => n.CreatedAt).ToListAsync(ct);
    }

    public async Task<Note> AddAsync(string userId, string text, CancellationToken ct)
    {
        var notebookId = await EnsureNotebookAsync(userId, NotesAuthorization.WritePermission, ct);
        var note = new Note { Id = Guid.NewGuid(), NotebookId = notebookId, Text = text.Trim(), CreatedAt = DateTime.UtcNow };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return note;
    }

    private async Task<string> EnsureNotebookAsync(string userId, string permission, CancellationToken ct)
    {
        var notebookId = NotesAuthorization.NotebookId(userId);

        // First use: provision the user's notebook and make them its owner.
        await db.ProvisionUserSubjectAsync(userId, userId, cancellationToken: ct);
        await db.ProvisionResourceWithIdAsync(notebookId, NotesAuthorization.NotebookType, $"Notebook of {userId}", cancellationToken: ct);
        await db.GrantRoleAsync(userId, notebookId, NotesAuthorization.OwnerRole, ct);
        await db.SaveChangesAsync(ct);

        var access = await fga.CheckAccessAsync(userId, permission, notebookId);
        return access.Allowed
            ? notebookId
            : throw new UnauthorizedAccessException($"{userId} lacks {permission} on {notebookId}.");
    }
}

/// <summary>
/// MCP tools exposed on /mcp. SqlOS already validated the token for the MCP audience; the tools
/// act as the connecting user through <see cref="ISqlOSMcpUserContext"/>.
/// </summary>
public sealed class NotesMcpTools
{
    [McpServerTool(Name = "list_notes"), Description("Lists the connecting user's notes.")]
    public static async Task<IReadOnlyList<string>> ListNotes(ISqlOSMcpUserContext user, NotesService notes, CancellationToken ct)
        => (await notes.ListAsync(RequireUser(user), ct)).Select(n => n.Text).ToArray();

    [McpServerTool(Name = "add_note"), Description("Adds a note to the connecting user's notebook.")]
    public static async Task<string> AddNote(
        ISqlOSMcpUserContext user,
        NotesService notes,
        [Description("The note text.")] string text,
        CancellationToken ct)
        => (await notes.AddAsync(RequireUser(user), text, ct)).Id.ToString();

    private static string RequireUser(ISqlOSMcpUserContext user)
        => user.UserId ?? throw new InvalidOperationException("This tool requires a user token.");
}
