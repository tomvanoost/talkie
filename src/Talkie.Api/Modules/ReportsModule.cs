using Microsoft.EntityFrameworkCore;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;
using static Talkie.Api.Infrastructure.EndpointExtensions;

namespace Talkie.Api.Modules;

/// <summary>Everyone can report a user; only admin@talkie.com sees all reports and can remove them.</summary>
public sealed class ReportsModule : IModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reports").WithTags("Reports").RequireTalkieUser();

        g.MapGet("/", async (CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
        {
            var me = await current.GetAsync(ct);
            var query = db.Reports.AsNoTracking()
                .Include(r => r.Reporter).ThenInclude(u => u!.Company)
                .Include(r => r.ReportedUser).ThenInclude(u => u!.Company)
                .AsQueryable();
            if (!CurrentUser.IsAdmin(me)) query = query.Where(r => r.ReporterId == me.Id);
            var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
            return reports.Select(r => new ReportDto(r.Id, r.Reporter!.ToSummary(), r.ReportedUser!.ToSummary(), r.Reason, r.CreatedAt)).ToList();
        });

        g.MapPost("/", async (CreateReportRequest request, CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
        {
            var me = await current.GetAsync(ct);
            var reason = request.Reason?.Trim() ?? "";
            if (reason.Length is < 3 or > 1000) return BadRequest("Please describe the reason (3 to 1000 characters).");
            var email = Validation.NormalizeEmail(request.ReportedEmail ?? "");
            var reported = await db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Email == email, ct);
            if (reported is null) return NotFound("No user with this e-mail exists.");
            if (reported.Id == me.Id) return BadRequest("You cannot report yourself.");

            var report = new Report { ReporterId = me.Id, ReportedUserId = reported.Id, Reason = reason };
            db.Reports.Add(report);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ReportDto(report.Id, me.ToSummary(), reported.ToSummary(), report.Reason, report.CreatedAt));
        });

        g.MapDelete("/{id:guid}", async (Guid id, CurrentUser current, TalkieDbContext db, CancellationToken ct) =>
        {
            var me = await current.GetAsync(ct);
            if (!CurrentUser.IsAdmin(me)) return Forbidden("Only admin@talkie.com can remove reports.");
            var deleted = await db.Reports.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? NotFound("Report not found.") : Results.NoContent();
        });
    }
}
