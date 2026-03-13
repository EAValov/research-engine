window.researchEngine = window.researchEngine || {};
window.researchEngine.theme = (function () {
  function getSystemTheme() {
    return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  function apply(theme) {

    document.documentElement.dataset.theme = theme;
    return theme;
  }

  function init() {
    const theme = getSystemTheme();
    return apply(theme);
  }

  function set(pref) {
    if (pref === "system") return init();
    return apply(pref === "dark" ? "dark" : "light");
  }

  return { init, set };
})();