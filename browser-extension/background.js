"use strict";

const HOST_NAME = "com.moglocker.adblock";
const RULE_PAGE_SIZE = 300;
const COSMETIC_PAGE_SIZE = 150;
let nativePort = null;
let nextRequestId = 1;
let pending = new Map();
let blockedRuleIds = new Set();
let syncInProgress = false;
let lastAppliedFilterRevision = -1;
let lastAppliedEnabled = null;
let queuedHits = [];
let hitFlushTimer = null;
let reconnectTimer = null;
const cosmeticCssByTab = new Map();

function connectNativeHost() {
  if (nativePort) return Promise.resolve(nativePort);
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  try {
    nativePort = chrome.runtime.connectNative(HOST_NAME);
  } catch (error) {
    return Promise.reject(new Error("Не удалось подключить MogLocker: " + error.message));
  }

  nativePort.onMessage.addListener(message => {
    const waiter = pending.get(message.id);
    if (!waiter) return;
    pending.delete(message.id);
    clearTimeout(waiter.timeout);
    if (message.ok) waiter.resolve(message.data);
    else waiter.reject(new Error(message.error || "Ошибка Native Messaging host."));
  });

  nativePort.onDisconnect.addListener(() => {
    const error = chrome.runtime.lastError;
    nativePort = null;
    for (const waiter of pending.values()) {
      clearTimeout(waiter.timeout);
      waiter.reject(new Error(error ? error.message : "Соединение с MogLocker прервано."));
    }
    pending.clear();
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      ensureFiltering().catch(reportError);
    }, 1500);
  });
  return Promise.resolve(nativePort);
}

async function hostRequest(action, data = {}) {
  const port = await connectNativeHost();
  const id = String(nextRequestId++);
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pending.delete(id);
      reject(new Error("Тайм-аут связи с MogLocker: " + action));
    }, 20000);
    pending.set(id, { resolve, reject, timeout });
    try {
      port.postMessage({ id, action, data });
    } catch (error) {
      clearTimeout(timeout);
      pending.delete(id);
      reject(error);
    }
  });
}

async function loadAllRules() {
  const rules = [];
  blockedRuleIds = new Set();
  let offset = 0;
  let total = 0;
  do {
    const page = await hostRequest("getRulesPage", {
      offset,
      limit: RULE_PAGE_SIZE
    });
    total = page.total;
    for (const rule of page.rules) {
      rules.push(rule);
      if (rule.action && rule.action.type === "block") blockedRuleIds.add(rule.id);
    }
    offset += page.rules.length;
    if (page.rules.length === 0 && offset < total) {
      throw new Error("Сервер правил вернул пустую страницу до конца списка.");
    }
  } while (offset < total);
  return rules;
}

async function ensureFiltering() {
  if (syncInProgress) return;
  syncInProgress = true;
  try {
    const state = await hostRequest("getState");
    if (!state.enabled) {
      if (lastAppliedEnabled === false) return;
      await chrome.declarativeNetRequest.updateDynamicRules({
        removeRuleIds: (await chrome.declarativeNetRequest.getDynamicRules()).map(rule => rule.id),
        addRules: []
      });
      blockedRuleIds.clear();
      await applyCosmeticsToOpenTabs(false);
      lastAppliedEnabled = false;
      lastAppliedFilterRevision = state.filterRevision;
      return;
    }
    if (lastAppliedEnabled === true &&
        lastAppliedFilterRevision === state.filterRevision) return;
    const rules = await loadAllRules();
    if (rules.length > 4500) throw new Error("Превышен лимит динамических правил браузера.");
    await chrome.declarativeNetRequest.updateDynamicRules({
      removeRuleIds: (await chrome.declarativeNetRequest.getDynamicRules()).map(rule => rule.id),
      addRules: rules
    });
    await applyCosmeticsToOpenTabs(true);
    lastAppliedEnabled = true;
    lastAppliedFilterRevision = state.filterRevision;
  } catch (error) {
    reportError(error);
  } finally {
    syncInProgress = false;
  }
}

async function cosmeticRulesForHost(host) {
  const rules = [];
  let offset = 0;
  let total = 0;
  do {
    const page = await hostRequest("getCosmeticsForHost", {
      host,
      offset,
      limit: COSMETIC_PAGE_SIZE
    });
    total = page.total;
    rules.push(...page.rules);
    offset += page.rules.length;
    if (page.rules.length === 0 && offset < total) {
      throw new Error("Сервер косметических правил вернул пустую страницу.");
    }
  } while (offset < total);
  return rules;
}

async function injectCosmetics(tabId, url, enabled) {
  const cssKey = "cosmeticCss_" + tabId;
  const savedCss = await chrome.storage.session.get(cssKey);
  const oldCss = cosmeticCssByTab.get(tabId) || savedCss[cssKey];
  if (oldCss) {
    try {
      await chrome.scripting.removeCSS({
        target: { tabId },
        css: oldCss,
        origin: "USER"
      });
    } catch (error) {
      if (!/No tab with id|Cannot access|cannot be scripted/i.test(error.message)) {
        reportError(error);
      }
    }
    cosmeticCssByTab.delete(tabId);
    await chrome.storage.session.remove(cssKey);
  }
  let parsed;
  try {
    parsed = new URL(url);
  } catch (_) {
    return;
  }
  if (!enabled || !["http:", "https:"].includes(parsed.protocol)) return;
  const rules = await cosmeticRulesForHost(parsed.hostname);
  if (rules.length === 0) return;
  const css = rules.map(rule => rule.selector + "{display:none !important;}").join("\n");
  try {
    await chrome.scripting.insertCSS({
      target: { tabId },
      css,
      origin: "USER"
    });
    cosmeticCssByTab.set(tabId, css);
    await chrome.storage.session.set({ [cssKey]: css });
  } catch (error) {
    if (!/No tab with id|Cannot access|cannot be scripted/i.test(error.message)) {
      reportError(error);
    }
  }
}

chrome.tabs.onRemoved.addListener(tabId => {
  cosmeticCssByTab.delete(tabId);
  chrome.storage.session.remove("cosmeticCss_" + tabId);
});

async function applyCosmeticsToOpenTabs(enabled) {
  const tabs = await chrome.tabs.query({});
  for (const tab of tabs) {
    if (tab.id !== undefined && tab.url) {
      injectCosmetics(tab.id, tab.url, enabled).catch(reportError);
    }
  }
}

function queueBlockedRequest(info) {
  const request = info && info.request;
  const ruleId = info && info.rule && info.rule.ruleId;
  if (!request || !blockedRuleIds.has(ruleId) || !/^https?:\/\//i.test(request.url)) return;
  queuedHits.push({
    url: request.url,
    resourceType: request.resourceType || request.type || "other"
  });
  if (!hitFlushTimer) {
    hitFlushTimer = setTimeout(flushBlockedRequests, 1500);
  }
  if (queuedHits.length >= 100) flushBlockedRequests();
}

async function flushBlockedRequests() {
  if (hitFlushTimer) {
    clearTimeout(hitFlushTimer);
    hitFlushTimer = null;
  }
  if (queuedHits.length === 0) return;
  const batch = queuedHits.splice(0, 100);
  try {
    await hostRequest("recordBlocked", { records: batch });
  } catch (error) {
    queuedHits = batch.concat(queuedHits).slice(-500);
    reportError(error);
  }
  if (queuedHits.length && !hitFlushTimer) {
    hitFlushTimer = setTimeout(flushBlockedRequests, 1500);
  }
}

function reportError(error) {
  console.error("[MogLocker AdBlock]", error && error.message ? error.message : error);
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create("moglocker-sync", { periodInMinutes: 1 });
  ensureFiltering().catch(reportError);
});

chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create("moglocker-sync", { periodInMinutes: 1 });
  ensureFiltering().catch(reportError);
});

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "moglocker-sync") ensureFiltering();
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" && tab.url) {
    hostRequest("getState")
      .then(state => injectCosmetics(tabId, tab.url, state.enabled))
      .catch(reportError);
  }
});

chrome.tabs.onActivated.addListener(async activeInfo => {
  try {
    const tab = await chrome.tabs.get(activeInfo.tabId);
    const state = await hostRequest("getState");
    if (tab.url) await injectCosmetics(tab.id, tab.url, state.enabled);
  } catch (error) {
    reportError(error);
  }
});

if (chrome.declarativeNetRequest.onRuleMatchedDebug) {
  chrome.declarativeNetRequest.onRuleMatchedDebug.addListener(queueBlockedRequest);
} else {
  console.error("[MogLocker AdBlock] Браузер не поддерживает журнал совпадений DNR.");
}

chrome.declarativeNetRequest.getDynamicRules()
  .then(rules => {
    blockedRuleIds = new Set(rules
      .filter(rule => rule.action && rule.action.type === "block")
      .map(rule => rule.id));
  })
  .catch(reportError);

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  (async () => {
    switch (message.action) {
      case "getState":
        return await hostRequest("getState");
      case "setEnabled":
        await hostRequest("setEnabled", { enabled: Boolean(message.enabled) });
        await ensureFiltering();
        return await hostRequest("getState");
      case "toggleAllowlist":
        if (!message.domain) throw new Error("Текущую страницу нельзя добавить в исключения.");
        await hostRequest(message.allow ? "addAllowlist" : "removeAllowlist", {
          domain: message.domain
        });
        await ensureFiltering();
        return await hostRequest("getState");
      case "sync":
        await ensureFiltering();
        return await hostRequest("getState");
      default:
        throw new Error("Неизвестная команда расширения.");
    }
  })().then(
    data => sendResponse({ ok: true, data }),
    error => sendResponse({ ok: false, error: error.message })
  );
  return true;
});
