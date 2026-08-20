using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StanTrack.Interfaces;
using StanTrack.Models;
using StanTrack.ViewModels;

namespace StanTrack.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IUnitOfWork _uow;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(IUnitOfWork uow, UserManager<ApplicationUser> userManager)
        {
            _uow = uow;
            _userManager = userManager;
        }

        // GET: /Dashboard
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            if (userId is null)
            {
                return Challenge();
            }

            var followedIds = await _uow.Follows.GetFollowedCelebrityIdsAsync(userId);

            var followedCelebrities = await _uow.Celebrities.GetByIdsAsync(followedIds);

            var upcomingEvents = followedIds.Count == 0
                ? Array.Empty<Event>()
                : await _uow.Events.GetUpcomingForCelebritiesAsync(followedIds, DateTime.UtcNow);

            var vm = new DashboardViewModel
            {
                FollowedCelebrities = followedCelebrities,
                UpcomingEvents = upcomingEvents
            };

            return View(vm);
        }
    }
}
