using Microsoft.AspNetCore.Mvc;
using StanTrack.Interfaces;
using StanTrack.Models;
using System.Diagnostics;

namespace StanTrack.Controllers
{
    public class HomeController : Controller
    {
        private readonly IUnitOfWork _uow;

        public HomeController(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<IActionResult> Index()
        {
            var upcomingEvents = await _uow.Events.GetUpcomingAsync(DateTime.UtcNow, 30);
            var totalUpcoming = await _uow.Events.GetUpcomingCountAsync(DateTime.UtcNow);
            var featured = await _uow.Celebrities.GetRandomAsync(6);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var followedIds = userId is null
                ? new HashSet<int>()
                : new HashSet<int>(await _uow.Follows.GetFollowedCelebrityIdsAsync(userId));

            ViewBag.EventCount = Math.Max(totalUpcoming - totalUpcoming % 100, 100);
            ViewBag.FeaturedCelebrities = featured;
            ViewData["FollowedCelebrityIds"] = followedIds;

            return View(upcomingEvents);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
