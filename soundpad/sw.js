"use strict";
const CACHE_NAME = "luma-soundpad-v6";
const ASSETS = [
  "./",
  "./index.html",
  "./manifest.webmanifest",
  "./sw.js",
  "./README.md",
  "./icons/mark.svg",
  "./icons/icon-192.png",
  "./icons/icon-512.png",
  "./icons/maskable-512.png",
  "./icons/apple-touch-icon.png"
];
self.addEventListener("install", event => {
  event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(ASSETS)));
});
self.addEventListener("activate", event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith("luma-soundpad-") && key !== CACHE_NAME).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});
self.addEventListener("message", event => {
  if (event.data && event.data.type === "SKIP_WAITING") self.skipWaiting();
});
self.addEventListener("fetch", event => {
  const request = event.request;
  if (request.method !== "GET" || new URL(request.url).origin !== self.location.origin) return;
  event.respondWith(caches.match(request).then(cached => {
    if (cached) return cached;
    return fetch(request).then(response => {
      if (response.ok && new URL(request.url).pathname.endsWith("/index.html")) {
        const copy = response.clone();
        caches.open(CACHE_NAME).then(cache => cache.put(request, copy));
      }
      return response;
    }).catch(() => {
      if (request.mode === "navigate") return caches.match("./index.html");
      return Response.error();
    });
  }));
});
