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
        private readonly IUnitOfWork _uow;

        public AdminController(IUnitOfWork uow)
        {
            _uow = uow;
        }

        // GET: /Admin/Events
        [HttpGet]
        public async Task<IActionResult> Events()
        {
            var events = await _uow.Events.GetAllForAdminReviewAsync();
            return View(events);
        }

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
