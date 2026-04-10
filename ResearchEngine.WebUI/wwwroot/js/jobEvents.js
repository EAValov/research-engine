// wwwroot/js/jobEvents.js
// ES module for Blazor dynamic import.
// Exports: connect/close + UI helpers.
// Also attaches helpers to window.researchEngine.jobEvents for any existing global calls.

const sources = new Map();

function logSuppressed(action, error, level = "debug") {
  const logger = console?.[level] ?? console?.debug ?? console?.log;
  if (!logger) return;
  logger.call(console, `[jobEvents] ${action}`, error);
}

function invokeDotNetSafe(dotNetRef, method, action, ...args) {
  Promise.resolve(dotNetRef.invokeMethodAsync(method, ...args)).catch((error) => {
    logSuppressed(action, error, "warn");
  });
}

function closeEventSourceSafe(es, action) {
  try {
    es.close();
  } catch (error) {
    logSuppressed(action, error);
  }
}

export function connect(url, dotNetRef) {
  // url MUST be absolute, JobEventsClient ensures this.
  const es = new EventSource(url);

  const id = (crypto?.randomUUID?.() ?? ("es_" + Math.random().toString(16).slice(2)));
  sources.set(id, { es, dotNetRef });

  const onAnyEvent = (e) => {
    if (!e || !e.data) return;
    invokeDotNetSafe(dotNetRef, "OnSseEvent", "dispatching SSE event to .NET", e.data);
  };

  // Default SSE messages (no explicit `event:` field)
  es.onmessage = onAnyEvent;

  // Many servers send named events for "normal" items.
  // Listen to a few common names so we don't miss updates.
  const possibleNames = [
    "event",
    "job",
    "job-event",
    "jobEvent",
    "progress",
    "update"
  ];
  for (const name of possibleNames) {
    try {
      es.addEventListener(name, onAnyEvent);
    } catch (error) {
      logSuppressed(`subscribing to named SSE event '${name}'`, error);
    }
  }

  es.addEventListener("done", (e) => {
    if (!e || !e.data) return;
    invokeDotNetSafe(dotNetRef, "OnSseDone", "dispatching SSE completion to .NET", e.data);
    closeEventSourceSafe(es, "closing EventSource after done event");
  });

  es.onerror = () => {
    invokeDotNetSafe(dotNetRef, "OnSseError", "dispatching SSE error to .NET");
    closeEventSourceSafe(es, "closing EventSource after error event");
  };

  return id;
}

export function close(id) {
  const entry = sources.get(id);
  if (!entry) return;
  closeEventSourceSafe(entry.es, "closing EventSource connection");
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
  } catch (error) {
    logSuppressed("copying text to clipboard", error);
    return false;
  }
}

// Keep global helpers for any code that calls window.researchEngine.jobEvents.*
window.researchEngine = window.researchEngine || {};
window.researchEngine.jobEvents = window.researchEngine.jobEvents || {};
window.researchEngine.jobEvents.scrollIfNearBottom = scrollIfNearBottom;
window.researchEngine.jobEvents.scrollToBottom = scrollToBottom;
window.researchEngine.jobEvents.copyText = copyText;
