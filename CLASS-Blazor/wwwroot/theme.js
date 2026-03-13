window.themeManager = {
    isDarkMode() {
        return document.documentElement.getAttribute("data-theme") === "dark";
    },
    toggleTheme() {
        const isDark = this.isDarkMode();
        const nextTheme = isDark ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", nextTheme);
        localStorage.setItem("site-theme", nextTheme);
        return nextTheme === "dark";
    }
};
