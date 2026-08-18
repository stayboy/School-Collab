// dialogDrawer.js -- focus capture / restore helpers for DialogDrawer.
//
// Loaded lazily by DialogDrawer.razor via the standard Blazor "import"
// pattern (JS.InvokeAsync<IJSObjectReference>("import",
// "./_content/SchoolCollab.Admin.Shared/js/dialogDrawer.js")). The module
// is served as a static asset of the SchoolCollab.Admin.Shared RCL; there
// is no <script> tag in App.razor for it. C# calls the named exports
// through the IJSObjectReference returned by the import -- it never calls
// a global, so none is registered.
//
// Why a JS module instead of a server-side callback?
//   ElementReference ids are scoped to the capturing component and cannot
//   be focused from another instance. The drawer needs to remember what
//   was focused when it opened (typically the trigger button rendered by
//   a child component) and restore focus to that element on close. Storing
//   a CSS selector at open time and using it at close time avoids the
//   ElementReference scoping problem entirely.
//
// Best-effort: every helper returns null / no-ops on failure so the
// drawer's try/catch wrappers keep the user-visible behaviour intact if
// the JS module isn't loaded yet (e.g. during pre-rendering or tests).

/**
 * Captures a CSS selector that uniquely identifies the currently focused
 * element, or null when it cannot be uniquely identified.
 *
 * Strategy: walk up from document.activeElement to the first ancestor
 * (inclusive) that has an id and still resolves to itself; emit "#id".
 * When no ancestor has an id, return null rather than a tag selector --
 * querySelector(tagName) would return the FIRST element of that tag on
 * the page, which is almost never the trigger. Returning null makes the
 * restore step a clean no-op instead of focusing an unrelated element.
 *
 * @returns {string|null} CSS selector ("#id") or null.
 */
export function captureActiveElementSelector() {
    try {
        const active = document.activeElement;
        if (!active || active === document.body || active === document.documentElement) {
            return null;
        }
        // Walk to the closest ancestor (inclusive) with an id that still
        // resolves to itself. Gives a stable selector that survives
        // re-renders of the leaf (e.g. a focusable child of a row that is
        // about to be torn down).
        let node = active;
        while (node && node !== document.body) {
            if (node.id) {
                if (document.getElementById(node.id) === node) {
                    return '#' + CSS.escape(node.id);
                }
            }
            node = node.parentElement;
        }
        // No uniquely-identifiable ancestor -- leave focus where it is
        // on close rather than guessing.
        return null;
    } catch {
        return null;
    }
}

/**
 * Focuses the element matching the given CSS selector. Silently no-ops
 * when no element matches (the captured element may have been re-rendered
 * or removed between capture and restore) or when the selector is empty.
 *
 * @param {string} selector CSS selector returned from
 *   captureActiveElementSelector().
 */
export function focusBySelector(selector) {
    try {
        if (!selector) return;
        const el = document.querySelector(selector);
        if (el && typeof el.focus === 'function') {
            el.focus();
        }
    } catch {
        // Element gone -- leave focus where it is.
    }
}
