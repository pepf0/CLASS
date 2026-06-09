window.themeManager = {
    storageKey: "site-theme",
    getStoredTheme() {
        try {
            return localStorage.getItem(this.storageKey);
        } catch {
            return null;
        }
    },
    setStoredTheme(theme) {
        try {
            localStorage.setItem(this.storageKey, theme);
        } catch {
            // The visible theme should still change when storage is unavailable.
        }
    },
    getCurrentTheme() {
        const savedTheme = this.getStoredTheme();
        return savedTheme === "light" ? "light" : "dark";
    },
    applyTheme(theme) {
        const normalizedTheme = theme === "light" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", normalizedTheme);
        return normalizedTheme;
    },
    initialize() {
        return this.applyTheme(this.getCurrentTheme()) === "dark";
    },
    isDarkMode() {
        const currentTheme = document.documentElement.getAttribute("data-theme");

        if (currentTheme === "light" || currentTheme === "dark") {
            return currentTheme === "dark";
        }

        return this.initialize();
    },
    toggleTheme() {
        const isDark = this.isDarkMode();
        const nextTheme = isDark ? "light" : "dark";
        this.applyTheme(nextTheme);
        this.setStoredTheme(nextTheme);
        return nextTheme === "dark";
    }
};

window.themeManager.initialize();
