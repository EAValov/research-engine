// wwwroot/js/localStorage.js
(function () {
  const NS = (window.researchEngine = window.researchEngine || {});
  NS.storage = {
    getItem: function (key) {
      try {
        return localStorage.getItem(key);
      } catch {
        return null;
      }
    },
    setItem: function (key, value) {
      try {
        localStorage.setItem(key, value);
      } catch {
        // ignore
      }
    },
    removeItem: function (key) {
      try {
        localStorage.removeItem(key);
      } catch {
        // ignore
      }
    },
  };
})();