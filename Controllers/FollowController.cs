using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.Controllers
{
    [Authorize(Roles = "Fan,Admin")]
    public class FollowController : Controller
    {
        private readonly IUnitOfWork _uow;
        private readonly UserManager<ApplicationUser> _userManager;

        public FollowController(IUnitOfWork uow, UserManager<ApplicationUser> userManager)
        {
            _uow = uow;
            _userManager = userManager;
        }

        // POST: /Follow/Toggle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int celebrityId, string? returnUrl)
        {
            var userId = _userManager.GetUserId(User);
            if (userId is null)
            {
                return Challenge();
            }

            var celebrity = await _uow.Celebrities.GetByIdAsync(celebrityId);
            if (celebrity is null)
            {
                return NotFound();
            }

            var isFollowing = await _uow.Follows.IsFollowingAsync(userId, celebrityId);
            if (isFollowing)
            {
                await _uow.Follows.UnfollowAsync(userId, celebrityId);
            }
            else
            {
                await _uow.Follows.FollowAsync(userId, celebrityId);
            }
            await _uow.SaveChangesAsync();

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            var referer = Request.Headers.Referer.ToString();
            if (!string.IsNullOrEmpty(referer) && Url.IsLocalUrl(referer))
            {
                return LocalRedirect(referer);
            }

            return RedirectToAction("Index", "Celebrities");
        }
    }
}
