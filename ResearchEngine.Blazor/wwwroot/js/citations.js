window.researchEngine = window.researchEngine || {};
window.researchEngine.citations = (function () {
  const wired = new WeakMap();
  let globalHandlers = null;

  function wire(container, dotNetRef) {
    if (!container) return;
    if (wired.get(container)) return;

    const hoverTimers = new Map(); // key = element, value = timer id

    function getRect(el) {
      const r = el.getBoundingClientRect();
      return {
        left: r.left, top: r.top, right: r.right, bottom: r.bottom,
        width: r.width, height: r.height,
        vw: window.innerWidth, vh: window.innerHeight
      };
    }

    function schedule(el, delay, fn) {
      clearTimeout(hoverTimers.get(el));
      const t = setTimeout(fn, delay);
      hoverTimers.set(el, t);
    }

    function onEnter(e) {
      const el = e.target.closest(".lrn-cite");
      if (!el) return;
      const id = el.getAttribute("data-lrn");
      if (!id) return;

      schedule(el, 150, () => {
        dotNetRef.invokeMethodAsync("OnCitationActivate", id, "hover", getRect(el));
      });
    }

    function onLeave(e) {
      const el = e.target.closest(".lrn-cite");
      if (!el) return;
      schedule(el, 150, () => {
        dotNetRef.invokeMethodAsync("OnCitationDeactivate", "hover");
      });
    }

    function onFocusIn(e) {
      const el = e.target.closest(".lrn-cite");
      if (!el) return;
      const id = el.getAttribute("data-lrn");
      if (!id) return;

      dotNetRef.invokeMethodAsync("OnCitationActivate", id, "focus", getRect(el));
    }

    function onClick(e) {
      const el = e.target.closest(".lrn-cite");
      if (!el) return;
      e.preventDefault();
      const id = el.getAttribute("data-lrn");
      if (!id) return;

      dotNetRef.invokeMethodAsync("OnCitationToggle", id, getRect(el));
    }

    function onKeyDown(e) {
      const el = e.target.closest(".lrn-cite");
      if (!el) return;

      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        const id = el.getAttribute("data-lrn");
        if (!id) return;
        dotNetRef.invokeMethodAsync("OnCitationToggle", id, getRect(el));
      }
      if (e.key === "Escape") {
        dotNetRef.invokeMethodAsync("OnRequestClosePopover", "esc");
      }
    }

    container.addEventListener("mouseenter", onEnter, true);
    container.addEventListener("mouseleave", onLeave, true);
    container.addEventListener("focusin", onFocusIn, true);
    container.addEventListener("click", onClick, true);
    container.addEventListener("keydown", onKeyDown, true);

    wired.set(container, true);
  }

  function registerGlobalClose(dotNetRef, popoverElementId) {
    unregisterGlobalClose();

    function onDocDown(e) {
      const pop = document.getElementById(popoverElementId);
      if (!pop) return;

      const inPopover = pop.contains(e.target);
      const inCitation = e.target.closest && e.target.closest(".lrn-cite");

      if (!inPopover && !inCitation) {
        dotNetRef.invokeMethodAsync("OnRequestClosePopover", "outside");
      }
    }

    function onDocKey(e) {
      if (e.key === "Escape") {
        dotNetRef.invokeMethodAsync("OnRequestClosePopover", "esc");
      }
    }

    document.addEventListener("mousedown", onDocDown, true);
    document.addEventListener("touchstart", onDocDown, true);
    document.addEventListener("keydown", onDocKey, true);

    globalHandlers = { onDocDown, onDocKey };
  }

  function unregisterGlobalClose() {
    if (!globalHandlers) return;
    document.removeEventListener("mousedown", globalHandlers.onDocDown, true);
    document.removeEventListener("touchstart", globalHandlers.onDocDown, true);
    document.removeEventListener("keydown", globalHandlers.onDocKey, true);
    globalHandlers = null;
  }

  function scrollIntoViewById(id) {
    if (!id) return false;
    const el = document.getElementById(id);
    if (!el) return false;
    el.scrollIntoView({ block: "center", behavior: "smooth" });
    return true;
  }

  return { wire, registerGlobalClose, unregisterGlobalClose, scrollIntoViewById };
})();
