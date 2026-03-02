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

    // Read stored preference (default: dark)
    const stored = window.localStorage?.getItem("theme");
    const isDark = !stored || stored === "dark-theme";
    applyTheme(isDark);

    // Bind the toggle checkbox
    function bindToggle(sw) {
        if (!sw) return;
        sw.checked = isDark;
        sw.addEventListener("change", function () {
            const dark = this.checked;
            applyTheme(dark);
            localStorage?.setItem("theme", dark ? "dark-theme" : "light-theme");
        });
    }

    // The checkbox should already exist (static SSR)
    bindToggle(document.getElementById("theme-toggle"));

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
