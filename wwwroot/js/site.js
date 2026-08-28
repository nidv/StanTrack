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
