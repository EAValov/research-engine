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

window.researchEngine.downloadTextFile = function (fileName, content) {
  const trimmed = typeof fileName === "string" ? fileName.trim() : "";
  const safeName = trimmed.length > 0 ? trimmed : "synthesis.md";
  const finalName = safeName.toLowerCase().endsWith(".md") ? safeName : `${safeName}.md`;
  const text = typeof content === "string" ? content : "";

  const blob = new Blob([text], { type: "text/markdown;charset=utf-8" });
  const url = URL.createObjectURL(blob);

  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = finalName;
  anchor.rel = "noopener noreferrer";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();

  URL.revokeObjectURL(url);
};

window.researchEngine.mermaid = (function () {
  let mermaidLoadPromise = null;
  let initialized = false;
  let renderCounter = 0;

  function ensureMermaidLoaded() {
    if (window.mermaid) return Promise.resolve(window.mermaid);
    if (mermaidLoadPromise) return mermaidLoadPromise;

    mermaidLoadPromise = new Promise((resolve, reject) => {
      const existing = document.querySelector("script[data-research-mermaid='1']");
      if (existing) {
        existing.addEventListener("load", () => resolve(window.mermaid), { once: true });
        existing.addEventListener("error", () => reject(new Error("Failed to load mermaid script")), { once: true });
        return;
      }

      const script = document.createElement("script");
      script.src = "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js";
      script.async = true;
      script.defer = true;
      script.setAttribute("data-research-mermaid", "1");
      script.onload = () => resolve(window.mermaid);
      script.onerror = () => reject(new Error("Failed to load mermaid script"));
      document.head.appendChild(script);
    });

    return mermaidLoadPromise;
  }

  function transformFencedBlocks(container) {
    const blocks = container.querySelectorAll("pre > code.language-mermaid, pre > code.lang-mermaid");
    if (!blocks.length) return 0;

    let converted = 0;
    for (const code of blocks) {
      const pre = code.parentElement;
      if (!pre) continue;

      const graph = document.createElement("div");
      graph.className = "mermaid";
      graph.textContent = code.textContent || "";
      pre.replaceWith(graph);
      converted += 1;
    }
    return converted;
  }

  async function render(container) {
    if (!container) return false;

    transformFencedBlocks(container);

    const nodes = Array.from(container.querySelectorAll(".mermaid"));
    if (nodes.length === 0) return false;

    const mermaid = await ensureMermaidLoaded().catch(() => null);
    if (!mermaid) return false;

    if (!initialized) {
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: "strict",
      });
      initialized = true;
    }

    const targets = nodes.filter((n) => !n.dataset.researchMermaidRendered);
    if (targets.length === 0) return true;

    for (const node of targets)
      node.dataset.researchMermaidRendered = "1";

    try {
      if (typeof mermaid.run === "function") {
        await mermaid.run({ nodes: targets });
        return true;
      }
    } catch {
      // Fallback to legacy API below.
    }

    if (!mermaid.mermaidAPI || typeof mermaid.mermaidAPI.render !== "function")
      return false;

    for (const node of targets) {
      const id = `re-mermaid-${++renderCounter}`;
      const source = node.textContent || "";
      try {
        const output = await mermaid.mermaidAPI.render(id, source);
        const svg = typeof output === "string" ? output : output?.svg;
        if (svg)
          node.innerHTML = svg;
      } catch {
        // Leave raw text if rendering fails.
      }
    }

    return true;
  }

  return { render };
})();
