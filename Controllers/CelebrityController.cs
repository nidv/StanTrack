using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StanTrack.Filters;
using StanTrack.Models;
using StanTrack.Models.Dtos;
using StanTrack.Data;

[Route("api/[controller]")]
[ApiController]
public class CelebrityController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public CelebrityController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: api/celebrity/by-name/bts
    // Case-insensitive exact match. Partial/fuzzy search would live behind a
    // separate ?q= endpoint if needed.
    [HttpGet("by-name/{name}")]
    [AllowAnonymous]
    public async Task<ActionResult<CelebrityResponse>> GetByName(string name)
    {
        var normalized = name.Trim().ToLower();
        var celebrity = await _context.Celebrities.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name.ToLower() == normalized);

        if (celebrity == null) return NotFound();
        return CelebrityResponse.FromEntity(celebrity);
    }

    // POST: api/celebrity
    // Gated by Key header against ApiKeys:CelebrityWrite in config; signed-in
    // Identity alone is not sufficient.
    [HttpPost]
    [RequireApiKey("CelebrityWrite")]
    public async Task<ActionResult<CelebrityResponse>> Create(CelebrityCreateRequest request)
    {
        var celebrity = new Celebrity
        {
            Name = request.Name.Trim(),
            Category = request.Category.Trim(),
            Bio = request.Bio,
            PhotoUrl = request.PhotoUrl,
            DateOfBirth = request.DateOfBirth,
            // Null when no user is signed in; the API key allows anonymous-creator writes.
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        };

        _context.Celebrities.Add(celebrity);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetByName), new { name = celebrity.Name }, CelebrityResponse.FromEntity(celebrity));
    }
}
