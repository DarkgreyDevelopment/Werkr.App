// ── Werkr Theme Toggle ──────────────────────────────────────
// Dark theme is the default. Persists to localStorage.
// The checkbox lives in the statically-rendered NavMenu, so it is
// already in the DOM by the time this script runs (loaded at end of body).

(function () {
    const b = document.body;
    if (!b) return;

    function applyTheme(dark) {
        b.classList.toggle("dark-theme", dark);
        b.classList.toggle("light-theme", !dark);
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

    // Initial page load
    applyTheme(currentIsDark());
    bindToggle();

    // Re-apply after Blazor enhanced navigation replaces the DOM
    document.addEventListener("blazor:enhancedload", function () {
        applyTheme(currentIsDark());
        bindToggle();
    });

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
