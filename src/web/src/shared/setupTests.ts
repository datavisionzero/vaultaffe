import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

afterEach(cleanup);

// jsdom lays nothing out and implements neither of these. Base UI's popups and
// the sidebar's mobile switch reach for them, and neither is a thing to assert
// about here.
Element.prototype.scrollIntoView ??= () => undefined;
Element.prototype.scrollTo ??= () => undefined;
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.setPointerCapture ??= () => undefined;
Element.prototype.releasePointerCapture ??= () => undefined;

// And it has no pointer events at all, which is what a menu opens on. The
// events a test dispatches are mouse events with a pointer's name, which is
// enough for the handlers to run — nothing here asserts about a pointer.
globalThis.PointerEvent ??= MouseEvent as typeof PointerEvent;
window.matchMedia ??= (query: string) =>
  ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }) as MediaQueryList;

// The jsdom vitest ships gives the page no storage. The theme lives in one and
// the session token in the other (ADR 0014), so the tests get both.
for (const name of ["localStorage", "sessionStorage"] as const) {
  if (typeof window[name] === "undefined" || window[name] === null) {
    Object.defineProperty(window, name, { value: aStorage(), configurable: true });
  }
}

function aStorage(): Storage {
  const store = new Map<string, string>();

  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key) => store.get(key) ?? null,
    key: (index) => [...store.keys()][index] ?? null,
    removeItem: (key) => {
      store.delete(key);
    },
    setItem: (key, value) => {
      store.set(key, String(value));
    },
  };
}
