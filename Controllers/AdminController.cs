using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using StanTrack.BackgroundJobs;
using StanTrack.Interfaces;

namespace StanTrack.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        // POST: /Admin/Sync
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Sync(CancellationToken ct)
        {
            var syncService = HttpContext.RequestServices.GetRequiredService<EventSyncService>();
            var summary = await syncService.RunAsync(ct);
            return View("SyncResult", summary);
        }

        // POST: /Admin/SyncForCelebrity/5
        // "Sync this celebrity" button on /Celebrities/Details/{id}. Returns to the details page
        // with a TempData banner summarizing the run.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncForCelebrity(int id, CancellationToken ct)
        {
            var syncService = HttpContext.RequestServices.GetRequiredService<EventSyncService>();
            var summary = await syncService.RunForCelebrityAsync(id, ct);
            if (summary is null)
            {
                return NotFound();
            }

            var perSource = summary.CountsBySource.Count == 0
                ? "no applicable fetchers for this category"
                : string.Join("; ", summary.CountsBySource.Select(kvp =>
                    $"{kvp.Key}: {kvp.Value.Inserted} new, {kvp.Value.ExistingInDb} existing, {kvp.Value.DuplicateInRun} dup"));

            var result = $"Sync complete for this celebrity — {perSource}.";
            if (summary.FailuresBySource.Count > 0)
            {
                var failures = string.Join(", ", summary.FailuresBySource.Select(kvp => $"{kvp.Key} ({kvp.Value})"));
                result += $" Failures: {failures}.";
            }

            TempData["AdminSyncResult"] = result;
            return RedirectToAction("Details", "Celebrities", new { id });
        }
    }
}
