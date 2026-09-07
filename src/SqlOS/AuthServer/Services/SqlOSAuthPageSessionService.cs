using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SqlOS.AuthServer.Interfaces;
using SqlOS.AuthServer.Models;

namespace SqlOS.AuthServer.Services;

public sealed class SqlOSAuthPageSessionService
{
    private const string CookieName = "sqlos_auth_page";
    private const string FamilyItemKey = "SqlOS.AuthPageSessionFamilyId";

    public const string SessionNoLongerActiveMessage = "Authentication session is no longer active.";

    internal const string LogoutReason = "logout";
    internal const string LegacyUnlinkedReason = "legacy_unlinked";

    private readonly ISqlOSAuthServerDbContext _context;
    private readonly SqlOSCryptoService _cryptoService;
    private readonly SqlOSSettingsService _settingsService;

    public SqlOSAuthPageSessionService(
        ISqlOSAuthServerDbContext context,
        SqlOSCryptoService cryptoService,
        SqlOSSettingsService settingsService)
    {
        _context = context;
        _cryptoService = cryptoService;
        _settingsService = settingsService;
    }

    public async Task<SqlOSAuthPageSession?> TryGetSessionAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var rawToken = httpContext.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var token = await _cryptoService.FindTemporaryTokenAsync(SqlOSAuthLifecyclePolicy.AuthPageSessionPurpose, rawToken, cancellationToken);
        if (token?.UserId == null)
        {
            return null;
        }

        if (!await EnsureFamilyIsReusableAsync(token, cancellationToken))
        {
            return null;
        }

        var lifecycle = await SqlOSAuthLifecyclePolicy.EvaluateAsync(
            _context,
            token.UserId,
            token.OrganizationId,
            cancellationToken);
        if (!lifecycle.IsActive)
        {
            var now = DateTime.UtcNow;
            await SqlOSAuthLifecyclePolicy.RevokeForDenialAsync(
                _context,
                token.UserId,
                token.OrganizationId,
                lifecycle,
                now,
                cancellationToken);
            token.ConsumedAt = now;
            SqlOSAuthLifecyclePolicy.AddDeniedAudit(
                _context,
                _cryptoService.GenerateId("aud"),
                "auth_page_session_reuse",
                lifecycle,
                token.UserId,
                token.OrganizationId);
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var user = await _context.Set<SqlOSUser>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == token.UserId && x.IsActive, cancellationToken);
        if (user == null)
        {
            return null;
        }

        var payload = _cryptoService.DeserializePayload<AuthPageSessionPayload>(token);
        var authenticatedAt = payload is { AuthenticatedAt: var stamped } && stamped != default
            ? stamped
            : token.CreatedAt;
        return new SqlOSAuthPageSession(
            rawToken,
            user,
            token.OrganizationId,
            payload?.AuthenticationMethod ?? "password",
            authenticatedAt);
    }

    public Task SignInAsync(
        HttpContext httpContext,
        SqlOSUser user,
        string? organizationId,
        string authenticationMethod,
        CancellationToken cancellationToken = default)
        => SignInAsync(httpContext, user, organizationId, authenticationMethod, authenticatedAt: null, cancellationToken);

    public Task SignInAsync(
        HttpContext httpContext,
        SqlOSUser user,
        string? organizationId,
        string authenticationMethod,
        DateTime? authenticatedAt,
        CancellationToken cancellationToken = default)
        => SignInAsync(httpContext, user, organizationId, authenticationMethod, authenticatedAt, continueExistingSession: false, cancellationToken);

    public async Task SignInAsync(
        HttpContext httpContext,
        SqlOSUser user,
        string? organizationId,
        string authenticationMethod,
        DateTime? authenticatedAt,
        bool continueExistingSession,
        CancellationToken cancellationToken = default)
    {
        var lifecycle = await SqlOSAuthLifecyclePolicy.EvaluateAsync(
            _context,
            user.Id,
            organizationId,
            cancellationToken);
        if (!lifecycle.IsActive)
        {
            await SqlOSAuthLifecyclePolicy.RevokeForDenialAsync(
                _context,
                user.Id,
                organizationId,
                lifecycle,
                DateTime.UtcNow,
                cancellationToken);
            SqlOSAuthLifecyclePolicy.AddDeniedAudit(
                _context,
                _cryptoService.GenerateId("aud"),
                "auth_page_session_issue",
                lifecycle,
                user.Id,
                organizationId);
            await _context.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(SessionNoLongerActiveMessage);
        }

        var family = await ResolveFamilyForSignInAsync(httpContext, user.Id, organizationId, continueExistingSession, cancellationToken);
        var securitySettings = await _settingsService.GetResolvedSecuritySettingsAsync(cancellationToken);
        var rawToken = await _cryptoService.CreateTemporaryTokenAsync(
            SqlOSAuthLifecyclePolicy.AuthPageSessionPurpose,
            user.Id,
            null,
            organizationId,
            new AuthPageSessionPayload(authenticationMethod, authenticatedAt ?? DateTime.UtcNow),
            securitySettings.SessionIdleTimeout,
            cancellationToken,
            family.Id);

        var revokedAt = await _context.Set<SqlOSAuthPageSessionFamily>()
            .AsNoTracking()
            .Where(x => x.Id == family.Id)
            .Select(x => x.RevokedAt)
            .FirstAsync(cancellationToken);
        if (revokedAt != null)
        {
            await _cryptoService.ConsumeTemporaryTokenAsync(
                SqlOSAuthLifecyclePolicy.AuthPageSessionPurpose,
                rawToken,
                cancellationToken);
            throw new InvalidOperationException(SessionNoLongerActiveMessage);
        }

        httpContext.Items[FamilyItemKey] = family.Id;
        httpContext.Response.Cookies.Append(CookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.Add(securitySettings.SessionIdleTimeout),
            Path = "/"
        });
    }

    public async Task SignOutAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var rawToken = httpContext.Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var token = await FindAuthPageTokenByRawAsync(rawToken, unconsumedOnly: false, cancellationToken);
            if (token != null)
            {
                var now = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(token.AuthPageSessionFamilyId))
                {
                    await RevokeFamilyAsync(token.AuthPageSessionFamilyId, LogoutReason, now, cancellationToken);
                }
                else
                {
                    await ConsumeLegacyUnlinkedAsync(token, now, cancellationToken);
                }
            }
        }

        httpContext.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Path = "/"
        });
    }

    internal async Task<bool> CanContinuePresentingSessionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Items[FamilyItemKey] is string issuedFamilyId
            && !string.IsNullOrWhiteSpace(issuedFamilyId))
        {
            var issuedFamily = await _context.Set<SqlOSAuthPageSessionFamily>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == issuedFamilyId, cancellationToken);
            return issuedFamily is { RevokedAt: null };
        }

        var rawToken = httpContext.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return true;
        }

        var token = await FindAuthPageTokenByRawAsync(rawToken, unconsumedOnly: false, cancellationToken);
        if (token == null || string.IsNullOrWhiteSpace(token.AuthPageSessionFamilyId))
        {
            return false;
        }

        var family = await _context.Set<SqlOSAuthPageSessionFamily>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == token.AuthPageSessionFamilyId, cancellationToken);
        return family is { RevokedAt: null };
    }

    private async Task<SqlOSAuthPageSessionFamily> ResolveFamilyForSignInAsync(
        HttpContext httpContext,
        string userId,
        string? organizationId,
        bool continueExistingSession,
        CancellationToken cancellationToken)
    {
        if (httpContext.Items[FamilyItemKey] is string issuedFamilyId
            && !string.IsNullOrWhiteSpace(issuedFamilyId))
        {
            var issuedFamily = await _context.Set<SqlOSAuthPageSessionFamily>()
                .FirstOrDefaultAsync(x => x.Id == issuedFamilyId, cancellationToken);
            if (issuedFamily != null && issuedFamily.RevokedAt == null)
            {
                return issuedFamily;
            }

            if (continueExistingSession)
            {
                throw new InvalidOperationException(SessionNoLongerActiveMessage);
            }
        }

        var rawToken = httpContext.Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var presenting = await FindAuthPageTokenByRawAsync(rawToken, unconsumedOnly: true, cancellationToken);
            if (presenting != null)
            {
                if (string.IsNullOrWhiteSpace(presenting.AuthPageSessionFamilyId))
                {
                    await ConsumeLegacyUnlinkedAsync(presenting, DateTime.UtcNow, cancellationToken);
                    if (continueExistingSession)
                    {
                        throw new InvalidOperationException(SessionNoLongerActiveMessage);
                    }
                }
                else
                {
                    var existing = await _context.Set<SqlOSAuthPageSessionFamily>()
                        .FirstOrDefaultAsync(x => x.Id == presenting.AuthPageSessionFamilyId, cancellationToken);
                    if (existing == null || existing.RevokedAt != null)
                    {
                        if (continueExistingSession)
                        {
                            throw new InvalidOperationException(SessionNoLongerActiveMessage);
                        }
                    }
                    else
                    {
                        return existing;
                    }
                }
            }
            else if (continueExistingSession)
            {
                var consumed = await FindAuthPageTokenByRawAsync(rawToken, unconsumedOnly: false, cancellationToken);
                if (consumed != null)
                {
                    throw new InvalidOperationException(SessionNoLongerActiveMessage);
                }
            }
        }

        var family = new SqlOSAuthPageSessionFamily
        {
            Id = _cryptoService.GenerateId("aps"),
            UserId = userId,
            OrganizationId = organizationId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Set<SqlOSAuthPageSessionFamily>().Add(family);
        await _context.SaveChangesAsync(cancellationToken);
        return family;
    }

    private async Task<bool> EnsureFamilyIsReusableAsync(SqlOSTemporaryToken token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.AuthPageSessionFamilyId))
        {
            await ConsumePresentingTokenAsync(token, cancellationToken);
            return false;
        }

        var family = await _context.Set<SqlOSAuthPageSessionFamily>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == token.AuthPageSessionFamilyId, cancellationToken);
        if (family == null || family.RevokedAt != null)
        {
            await ConsumePresentingTokenAsync(token, cancellationToken);
            return false;
        }

        return true;
    }

    private async Task RevokeFamilyAsync(
        string familyId,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var family = await _context.Set<SqlOSAuthPageSessionFamily>()
            .FirstOrDefaultAsync(x => x.Id == familyId, cancellationToken);
        if (family != null && family.RevokedAt == null)
        {
            family.RevokedAt = now;
            family.RevocationReason = reason;
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        var tokens = await _context.Set<SqlOSTemporaryToken>()
            .Where(x => x.Purpose == SqlOSAuthLifecyclePolicy.AuthPageSessionPurpose
                && x.AuthPageSessionFamilyId == familyId
                && x.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
        {
            return;
        }

        foreach (var sibling in tokens)
        {
            sibling.ConsumedAt = now;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task ConsumeLegacyUnlinkedAsync(
        SqlOSTemporaryToken presenting,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var unlinked = await _context.Set<SqlOSTemporaryToken>()
            .Where(x => x.Purpose == SqlOSAuthLifecyclePolicy.AuthPageSessionPurpose
                && x.ConsumedAt == null
                && x.AuthPageSessionFamilyId == null
                && x.UserId == presenting.UserId)
            .ToListAsync(cancellationToken);
        foreach (var token in unlinked)
        {
            token.ConsumedAt = now;
        }

        if (unlinked.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ConsumePresentingTokenAsync(SqlOSTemporaryToken token, CancellationToken cancellationToken)
    {
        if (token.ConsumedAt != null)
        {
            return;
        }

        token.ConsumedAt = DateTime.UtcNow;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<SqlOSTemporaryToken?> FindAuthPageTokenByRawAsync(
        string rawToken,
        bool unconsumedOnly,
        CancellationToken cancellationToken)
    {
        var hash = _cryptoService.HashToken(rawToken);
        var query = _context.Set<SqlOSTemporaryToken>()
            .Where(x => x.Purpose == SqlOSAuthLifecyclePolicy.AuthPageSessionPurpose && x.TokenHash == hash);
        if (unconsumedOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(x => x.ConsumedAt == null && x.ExpiresAt >= now);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    // AuthenticatedAt defaults so cookies minted before the field existed still
    // deserialize; a default value means "unknown" and falls back to the
    // temporary token's CreatedAt.
    private sealed record AuthPageSessionPayload(string AuthenticationMethod, DateTime AuthenticatedAt = default);
}

public sealed record SqlOSAuthPageSession(
    string RawToken,
    SqlOSUser User,
    string? OrganizationId,
    string AuthenticationMethod,
    DateTime AuthenticatedAt);
