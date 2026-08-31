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
    }
}
