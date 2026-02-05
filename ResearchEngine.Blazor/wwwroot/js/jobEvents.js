// wwwroot/js/jobEvents.js
// ES module for Blazor dynamic import.
// Exports: connect/close + UI helpers.
// Also attaches helpers to window.researchEngine.jobEvents for any existing global calls.

const sources = new Map();

export function connect(url, dotNetRef) {
  // url MUST be absolute (http://localhost:8090/...), JobEventsClient ensures this.
  const es = new EventSource(url);

  const id = (crypto?.randomUUID?.() ?? ("es_" + Math.random().toString(16).slice(2)));
  sources.set(id, { es, dotNetRef });

  es.onmessage = (e) => {
    if (!e || !e.data) return;
    dotNetRef.invokeMethodAsync("OnSseEvent", e.data);
  };

  es.addEventListener("done", (e) => {
    if (!e || !e.data) return;
    dotNetRef.invokeMethodAsync("OnSseDone", e.data);
    try { es.close(); } catch { }
  });

  es.onerror = () => {
    dotNetRef.invokeMethodAsync("OnSseError");
    try { es.close(); } catch { }
  };

  return id;
}

export function close(id) {
  const entry = sources.get(id);
  if (!entry) return;
  try { entry.es.close(); } catch { }
  sources.delete(id);
}

export function scrollIfNearBottom(el) {
  if (!el) return;
  const threshold = 120;
  const distanceFromBottom = (el.scrollHeight - el.scrollTop - el.clientHeight);
  if (distanceFromBottom <= threshold) el.scrollTop = el.scrollHeight;
}

export function scrollToBottom(el) {
  if (!el) return;
  el.scrollTop = el.scrollHeight;
}

export async function copyText(text) {
  if (!text) return false;
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

// Keep global helpers for any code that calls window.researchEngine.jobEvents.*
window.researchEngine = window.researchEngine || {};
window.researchEngine.jobEvents = window.researchEngine.jobEvents || {};
window.researchEngine.jobEvents.scrollIfNearBottom = scrollIfNearBottom;
window.researchEngine.jobEvents.scrollToBottom = scrollToBottom;
window.researchEngine.jobEvents.copyText = copyText;
