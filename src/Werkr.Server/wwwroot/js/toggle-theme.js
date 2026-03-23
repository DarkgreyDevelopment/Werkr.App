// ── Werkr Theme Toggle ──────────────────────────────────────
// Dark theme is the default. Persists to localStorage.
// The checkbox lives in the statically-rendered NavMenu, so it is
// already in the DOM by the time this script runs (loaded at end of body).
//
// Blazor enhanced navigation morphs the <body> from the server
// response which has no class attribute, stripping theme classes.
// A MutationObserver detects this and restores the correct theme
// immediately, independent of any particular event name.

(function () {
    if (!document.body) return;

    function applyTheme(dark) {
        document.body.classList.toggle("dark-theme", dark);
        document.body.classList.toggle("light-theme", !dark);
    }

    function currentIsDark() {
        const stored = window.localStorage?.getItem("theme");
        return !stored || stored === "dark-theme";
    }

    // Bind the toggle checkbox — re-entrant safe
    function bindToggle() {
        const sw = document.getElementById("theme-toggle");
        if (!sw) return;
        sw.checked = currentIsDark();
        // Clone-replace to remove any previous listener
        const fresh = sw.cloneNode(true);
        sw.parentNode.replaceChild(fresh, sw);
        fresh.addEventListener("change", function () {
            const dark = this.checked;
            applyTheme(dark);
            localStorage?.setItem("theme", dark ? "dark-theme" : "light-theme");
        });
    }

    function restore() {
        applyTheme(currentIsDark());
        bindToggle();
    }

    // Initial page load
    restore();

    // Blazor enhanced navigation — DOM event
    document.addEventListener("enhancedload", restore);

    // Blazor event API (available after blazor.web.js loads)
    if (typeof Blazor !== "undefined" && Blazor.addEventListener) {
        Blazor.addEventListener("enhancedload", restore);
    }

    // MutationObserver — catches any body class stripping regardless of
    // event names or timing. When both theme classes are missing we
    // restore from localStorage. The guard prevents infinite recursion:
    // restore() adds a class → observer fires → guard stops re-entry.
    new MutationObserver(function () {
        if (!document.body.classList.contains("dark-theme") &&
            !document.body.classList.contains("light-theme")) {
            restore();
        }
    }).observe(document.body, { attributes: true, attributeFilter: ["class"] });

    // Sync across tabs
    window.addEventListener("storage", function (e) {
        if (e.key === "theme") {
            const dark = e.newValue === "dark-theme";
            applyTheme(dark);
            const sw = document.getElementById("theme-toggle");
            if (sw) sw.checked = dark;
        }
    });
})();
