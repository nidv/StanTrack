using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StanTrack.Interfaces;
using StanTrack.Models;
using StanTrack.ViewModels;

namespace StanTrack.Controllers
{
    public class CelebritiesController : Controller
    {
        private readonly IUnitOfWork _uow;
        private readonly UserManager<ApplicationUser> _userManager;

        public CelebritiesController(IUnitOfWork uow, UserManager<ApplicationUser> userManager)
        {
            _uow = uow;
            _userManager = userManager;
        }

        // GET: /Celebrities
        [AllowAnonymous]
        public async Task<IActionResult> Index(string? query, string? category)
        {
            const int pageSize = 9;

            var total = await _uow.Celebrities.CountAsync(query, category);
            var celebrities = await _uow.Celebrities.SearchPaginatedAsync(query, category, 1, pageSize);
            var categories = await _uow.Celebrities.GetDistinctCategoriesAsync();

            var userId = _userManager.GetUserId(User);
            var followedIds = userId is null
                ? new HashSet<int>()
                : new HashSet<int>(await _uow.Follows.GetFollowedCelebrityIdsAsync(userId));

            ViewBag.Query = query;
            ViewBag.Category = category;
            ViewBag.Categories = new SelectList(categories, category);
            ViewBag.Total = total;
            ViewBag.HasMore = pageSize < total;
            ViewData["FollowedCelebrityIds"] = followedIds;

            return View(celebrities);
        }

        // GET: /Celebrities/ListPartial?query=&category=&page=2
        // Returns just the card grid markup for one page slice (no layout) so the
        // "Load more" button can fetch + append without re-fetching prior pages.
        [AllowAnonymous]
        public async Task<IActionResult> ListPartial(string? query, string? category, int page = 1)
        {
            const int pageSize = 9;
            if (page < 1) page = 1;

            var total = await _uow.Celebrities.CountAsync(query, category);
            var celebrities = await _uow.Celebrities.SearchPaginatedAsync(query, category, page, pageSize);

            var userId = _userManager.GetUserId(User);
            var followedIds = userId is null
                ? new HashSet<int>()
                : new HashSet<int>(await _uow.Follows.GetFollowedCelebrityIdsAsync(userId));

            ViewData["FollowedCelebrityIds"] = followedIds;
            // Offset for the CSS stagger-delay custom property so appended cards
            // continue the cascade from where the previous batch left off visually.
            ViewBag.RevealOffset = (page - 1) * pageSize;
            // POSTs from inside these cards (Follow toggle) should land back on
            // the real Index page, not this bare-partial endpoint.
            ViewData["ReturnUrlOverride"] = Url.Action(nameof(Index), new { query, category });

            // Pagination state goes to the JS handler via headers, not markup,
            // so the grid stays a pure list of cards.
            Response.Headers["X-HasMore"] = ((long)page * pageSize < total).ToString();
            Response.Headers["X-NextPage"] = (page + 1).ToString();

            return PartialView("_CelebrityCardList", celebrities);
        }

        // GET: /Celebrities/SearchPartial?query=taylor
        // Live-search endpoint. Returns the N closest name matches across all
        // categories (typing auto-clears the category pill client-side, so this
        // is intentionally category-blind). No pagination — results replace the
        // grid, the Load-more button hides while a search is active.
        [AllowAnonymous]
        public async Task<IActionResult> SearchPartial(string? query)
        {
            const int maxResults = 20;

            var celebrities = string.IsNullOrWhiteSpace(query)
                ? Array.Empty<Models.Celebrity>()
                : await _uow.Celebrities.SearchPaginatedAsync(query, category: null, page: 1, pageSize: maxResults);

            var userId = _userManager.GetUserId(User);
            ViewData["FollowedCelebrityIds"] = userId is null
                ? new HashSet<int>()
                : new HashSet<int>(await _uow.Follows.GetFollowedCelebrityIdsAsync(userId));

            // Same trap as ListPartial — without this, Follow redirects back here
            // and the browser gets bare partial HTML (white page, no layout).
            ViewData["ReturnUrlOverride"] = Url.Action(nameof(Index), new { query });

            Response.Headers["X-SearchCount"] = celebrities.Count().ToString();
            return PartialView("_CelebrityCardList", celebrities);
        }

        // GET: /Celebrities/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var celebrity = await _uow.Celebrities.GetByIdAsync(id);
            if (celebrity is null)
            {
                return NotFound();
            }

            var upcomingEvents = await _uow.Events.GetUpcomingForCelebritiesAsync(
                new[] { celebrity.Id }, DateTime.UtcNow);

            var userId = _userManager.GetUserId(User);
            var isFollowing = userId is not null
                && await _uow.Follows.IsFollowingAsync(userId, celebrity.Id);

            ViewBag.UpcomingEvents = upcomingEvents;
            ViewBag.IsFollowing = isFollowing;
            return View(celebrity);
        }

        // GET: /Celebrities/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View(new CelebrityFormViewModel());
        }

        // POST: /Celebrities/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(CelebrityFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var userId = _userManager.GetUserId(User);
            if (userId is null)
            {
                return Challenge();
            }

            var celebrity = new Celebrity
            {
                Name = vm.Name,
                Category = vm.Category,
                Bio = vm.Bio,
                PhotoUrl = vm.PhotoUrl,
                DateOfBirth = vm.DateOfBirth,
                CreatedByUserId = userId
            };

            await _uow.Celebrities.AddAsync(celebrity);
            await _uow.SaveChangesAsync(); // celebrity.Id now assigned

            if (celebrity.DateOfBirth.HasValue)
            {
                var today = DateTime.UtcNow.Date;
                var dob = celebrity.DateOfBirth.Value;
                var nextBirthday = new DateTime(today.Year, dob.Month, dob.Day);
                if (nextBirthday < today)
                {
                    nextBirthday = nextBirthday.AddYears(1);
                }

                var birthdayEvent = new Event
                {
                    CelebrityId = celebrity.Id,
                    Title = $"{celebrity.Name}'s birthday",
                    EventType = Models.Enums.EventType.Birthday,
                    EventDate = nextBirthday,
                    Source = "Manual"
                };

                await _uow.Events.AddAsync(birthdayEvent);
                await _uow.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id = celebrity.Id });
        }

        // GET: /Celebrities/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var celebrity = await _uow.Celebrities.GetByIdAsync(id);
            if (celebrity is null)
            {
                return NotFound();
            }

            var vm = new CelebrityFormViewModel
            {
                Id = celebrity.Id,
                Name = celebrity.Name,
                Category = celebrity.Category,
                Bio = celebrity.Bio,
                PhotoUrl = celebrity.PhotoUrl,
                DateOfBirth = celebrity.DateOfBirth
            };

            return View(vm);
        }

        // POST: /Celebrities/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, CelebrityFormViewModel vm)
        {
            if (id != vm.Id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var celebrity = await _uow.Celebrities.GetByIdAsync(id);
            if (celebrity is null)
            {
                return NotFound();
            }

            celebrity.Name = vm.Name;
            celebrity.Category = vm.Category;
            celebrity.Bio = vm.Bio;
            celebrity.PhotoUrl = vm.PhotoUrl;
            celebrity.DateOfBirth = vm.DateOfBirth;

            _uow.Celebrities.Update(celebrity);
            await _uow.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = celebrity.Id });
        }

        // GET: /Celebrities/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var celebrity = await _uow.Celebrities.GetByIdAsync(id);
            if (celebrity is null)
            {
                return NotFound();
            }

            return View(celebrity);
        }

        // POST: /Celebrities/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var celebrity = await _uow.Celebrities.GetByIdAsync(id);
            if (celebrity is null)
            {
                return NotFound();
            }

            _uow.Celebrities.Delete(celebrity);
            await _uow.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
