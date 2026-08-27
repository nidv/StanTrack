using Microsoft.AspNetCore.Mvc;
using StanTrack.Interfaces;

namespace StanTrack.Controllers
{
    public class EventsController : Controller
    {
        private readonly IUnitOfWork _uow;

        public EventsController(IUnitOfWork uow)
        {
            _uow = uow;
        }

        // GET: /Events
        public async Task<IActionResult> Index(int page = 1)
        {
            const int pageSize = 200;
            if (page < 1) page = 1;

            var total = await _uow.Events.GetUpcomingCountAsync(DateTime.UtcNow);
            var events = await _uow.Events.GetUpcomingPaginatedAsync(DateTime.UtcNow, page, pageSize);

            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.HasMore = page * pageSize < total;
            ViewBag.NextPage = page + 1;
            ViewBag.Shown = Math.Min(page * pageSize, total);

            return View(events);
        }
    }
}
