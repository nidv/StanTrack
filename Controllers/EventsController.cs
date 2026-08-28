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
        public async Task<IActionResult> Index()
        {
            const int pageSize = 50;
            var now = DateTime.UtcNow;

            var total = await _uow.Events.GetUpcomingCountAsync(now);
            var events = await _uow.Events.GetUpcomingPaginatedAsync(now, 1, pageSize);

            ViewBag.Total = total;
            ViewBag.HasMore = pageSize < total;

            return View(events);
        }

        // GET: /Events/ListPartial?page=2
        // Returns just the <li> markup for one page slice (no layout) so the
        // "Load more" button can fetch + append without re-fetching prior pages.
        public async Task<IActionResult> ListPartial(int page = 1)
        {
            const int pageSize = 50;
            if (page < 1) page = 1;

            var now = DateTime.UtcNow;
            var total = await _uow.Events.GetUpcomingCountAsync(now);
            var events = await _uow.Events.GetUpcomingPaginatedAsync(now, page, pageSize);

            Response.Headers["X-HasMore"] = ((long)page * pageSize < total).ToString();
            Response.Headers["X-NextPage"] = (page + 1).ToString();

            return PartialView("_EventListItems", events);
        }
    }
}
