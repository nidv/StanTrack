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
            var celebrities = await _uow.Celebrities.SearchAsync(query, category);
            var categories = await _uow.Celebrities.GetDistinctCategoriesAsync();

            ViewBag.Query = query;
            ViewBag.Category = category;
            ViewBag.Categories = new SelectList(categories, category);

            return View(celebrities);
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

            ViewBag.UpcomingEvents = upcomingEvents;
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
                CreatedByUserId = userId
            };

            await _uow.Celebrities.AddAsync(celebrity);
            await _uow.SaveChangesAsync();

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
                PhotoUrl = celebrity.PhotoUrl
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
