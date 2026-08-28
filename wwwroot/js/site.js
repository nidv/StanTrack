// StanTrack site JS.
//
// "Load more" pagination: the Index pages render page 1 server-side and emit a
// button[data-load-more] with the next page number. Clicking fetches the next
// slice from the matching *Controller.ListPartial endpoint and appends the
// returned markup to the target container. The partial returns pagination
// state via X-HasMore / X-NextPage response headers, so no JSON envelope is
// needed and the markup stays a pure list of items.

(function () {
    function updateShownCounter(counterSelector, delta) {
        if (!counterSelector) return;
        document.querySelectorAll(counterSelector).forEach(function (el) {
            var current = parseInt(el.textContent, 10);
            if (!isNaN(current)) {
                el.textContent = String(current + delta);
            }
        });
    }

    // Count nodes in an HTML fragment without touching innerHTML.
    // We insert the fragment into a live (but detached) container, count its
    // children, then move them into the real target.
    function appendFragment(target, html) {
        var range = document.createRange();
        range.selectNodeContents(target);
        var fragment = range.createContextualFragment(html);
        var count = fragment.childElementCount;
        target.appendChild(fragment);
        return count;
    }

    async function onLoadMoreClick(btn) {
        var targetSelector = btn.getAttribute('data-target');
        var container = targetSelector && document.querySelector(targetSelector);
        if (!container) return;

        var partialUrl = container.getAttribute('data-partial-url');
        if (!partialUrl) return;

        var nextPage = parseInt(btn.getAttribute('data-next-page') || '2', 10);

        // Build the query string from data-param-* attributes on the container
        // (e.g. data-param-query="taylor" -> query=taylor).
        var params = new URLSearchParams();
        params.set('page', String(nextPage));
        for (var i = 0; i < container.attributes.length; i++) {
            var attr = container.attributes[i];
            if (attr.name.startsWith('data-param-') && attr.value) {
                params.set(attr.name.substring('data-param-'.length), attr.value);
            }
        }

        btn.disabled = true;
        var originalLabel = btn.textContent;
        btn.textContent = 'Loading…';

        try {
            var response = await fetch(partialUrl + '?' + params.toString(), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin'
            });
            if (!response.ok) {
                throw new Error('Partial request failed: ' + response.status);
            }

            var html = await response.text();
            var appendedCount = appendFragment(container, html);

            var hasMoreHeader = response.headers.get('X-HasMore');
            var hasMore = hasMoreHeader === 'True' || hasMoreHeader === 'true';
            var serverNextPage = parseInt(response.headers.get('X-NextPage') || '', 10);

            updateShownCounter('[data-celebrity-shown]', appendedCount);
            updateShownCounter('[data-event-shown]', appendedCount);

            if (hasMore && !isNaN(serverNextPage)) {
                btn.setAttribute('data-next-page', String(serverNextPage));
                btn.disabled = false;
                btn.textContent = originalLabel;
            } else {
                var wrapper = btn.closest('#grid-end');
                if (wrapper) wrapper.remove();
                else btn.remove();
            }
        } catch (err) {
            console.error(err);
            btn.disabled = false;
            btn.textContent = originalLabel + ' (failed, retry)';
        }
    }

    document.addEventListener('click', function (event) {
        var btn = event.target.closest('[data-load-more]');
        if (btn) {
            event.preventDefault();
            onLoadMoreClick(btn);
        }
    });
})();

// Live search on the celebrities index.
//
// Typing in #celebrity-search-input debounces 300ms, hits /Celebrities/SearchPartial,
// and replaces the grid contents. While a search is active: category pills visually
// reset to "All" (search is category-blind), the Load-more wrapper is hidden.
// Clearing the input restores the initial page-1 grid (cached on page load) and
// the original category pill state.
//
// Latest-request-wins via AbortController so fast typing doesn't show stale hits.
(function () {
    var input = document.getElementById('celebrity-search-input');
    var grid = document.getElementById('celebrity-grid');
    if (!input || !grid) return;

    var searchUrl = input.getAttribute('data-search-url');
    var loadMoreWrap = document.getElementById('grid-end');
    var noResults = document.getElementById('celebrity-no-results');
    var initialEmpty = document.getElementById('celebrity-initial-empty');
    var pills = document.querySelectorAll('[data-category-pill]');
    var pillsWrap = document.getElementById('category-pills');

    // Cache the initial page-1 child nodes so clearing the search restores
    // instantly without another server round-trip. Cloning (not innerHTML)
    // avoids the HTML-injection lint rule.
    var initialGridNodes = Array.prototype.map.call(grid.childNodes, function (n) {
        return n.cloneNode(true);
    });
    var initialNextPage = loadMoreWrap
        ? loadMoreWrap.querySelector('[data-load-more]').getAttribute('data-next-page')
        : null;

    var aborter = null;
    var debounceTimer = null;

    function setPillsAllActive() {
        pills.forEach(function (p) {
            var isAll = p.getAttribute('data-category') === '';
            p.classList.toggle('btn-primary', isAll);
            p.classList.toggle('btn-outline-primary', !isAll);
        });
    }

    function restorePillsFromServer() {
        var active = pillsWrap ? pillsWrap.getAttribute('data-active-category') || '' : '';
        pills.forEach(function (p) {
            var isActive = p.getAttribute('data-category') === active;
            p.classList.toggle('btn-primary', isActive);
            p.classList.toggle('btn-outline-primary', !isActive);
        });
    }

    function hideLoadMore() {
        if (loadMoreWrap) loadMoreWrap.style.display = 'none';
    }
    function showLoadMore() {
        if (loadMoreWrap) loadMoreWrap.style.display = '';
    }

    function showNoResults() {
        if (noResults) noResults.style.display = '';
        if (grid) grid.style.display = 'none';
    }
    function showGrid() {
        if (noResults) noResults.style.display = 'none';
        if (grid) grid.style.display = '';
    }

    function restoreInitial() {
        // Replace grid children with the cached initial nodes (re-cloned so the
        // originals remain cached for the next clear-search cycle).
        while (grid.firstChild) grid.removeChild(grid.firstChild);
        initialGridNodes.forEach(function (n) {
            grid.appendChild(n.cloneNode(true));
        });

        if (initialEmpty) initialEmpty.style.display = '';
        if (initialNextPage && loadMoreWrap) {
            var btn = loadMoreWrap.querySelector('[data-load-more]');
            if (btn) btn.setAttribute('data-next-page', initialNextPage);
            showLoadMore();
        }
        restorePillsFromServer();
        showGrid();

        // Reset the "X of Y shown" counter to the page-1 count.
        var pageSize = initialGridNodes.length;
        document.querySelectorAll('[data-celebrity-shown]').forEach(function (el) {
            el.textContent = String(pageSize);
        });
    }

    async function runSearch(query) {
        if (aborter) aborter.abort();
        aborter = new AbortController();

        hideLoadMore();
        setPillsAllActive();
        if (initialEmpty) initialEmpty.style.display = 'none';

        try {
            var response = await fetch(searchUrl + '?query=' + encodeURIComponent(query), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                credentials: 'same-origin',
                signal: aborter.signal
            });
            if (!response.ok) throw new Error('Search failed: ' + response.status);

            var html = await response.text();
            var count = parseInt(response.headers.get('X-SearchCount') || '0', 10);

            // Replace grid contents.
            while (grid.firstChild) grid.removeChild(grid.firstChild);
            var range = document.createRange();
            range.selectNodeContents(grid);
            grid.appendChild(range.createContextualFragment(html));

            if (count === 0) showNoResults();
            else showGrid();

            // Show the search count, not the page-1 count, in the "X of Y" line.
            document.querySelectorAll('[data-celebrity-shown]').forEach(function (el) {
                el.textContent = String(count);
            });
        } catch (err) {
            if (err && err.name === 'AbortError') return; // newer keystroke already running
            console.error(err);
        }
    }

    input.addEventListener('input', function () {
        var q = input.value.trim();
        if (debounceTimer) clearTimeout(debounceTimer);

        if (q === '') {
            if (aborter) aborter.abort();
            restoreInitial();
            return;
        }

        debounceTimer = setTimeout(function () { runSearch(q); }, 300);
    });

    // Progressive enhancement: with JS live-search active, the submit button
    // is redundant. Intercept form submit so Enter doesn't trigger a full reload.
    var form = document.getElementById('celebrity-search-form');
    if (form) {
        form.addEventListener('submit', function (event) {
            event.preventDefault();
            var q = input.value.trim();
            if (debounceTimer) clearTimeout(debounceTimer);
            if (q === '') restoreInitial();
            else runSearch(q);
        });
    }
})();
