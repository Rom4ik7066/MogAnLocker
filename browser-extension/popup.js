"use strict";

const enabled = document.getElementById("enabled");
const enabledLabel = document.getElementById("enabled-label");
const count = document.getElementById("blocked-count");
const connection = document.getElementById("connection");
const siteHost = document.getElementById("site-host");
const allowButton = document.getElementById("allow-site");
const error = document.getElementById("error");
let currentDomain = "";
let state;

function send(action, fields = {}) {
  return new Promise((resolve, reject) => {
    chrome.runtime.sendMessage({ action, ...fields }, response => {
      if (chrome.runtime.lastError) {
        reject(new Error(chrome.runtime.lastError.message));
      } else if (!response || !response.ok) {
        reject(new Error(response ? response.error : "Нет ответа от фонового процесса."));
      } else {
        resolve(response.data);
      }
    });
  });
}

function render(nextState) {
  state = nextState;
  enabled.checked = Boolean(state.enabled);
  enabledLabel.textContent = state.enabled ? "Защита включена" : "Защита выключена";
  count.textContent = Number(state.blockedCount || 0).toLocaleString("ru-RU");
  connection.textContent = "Связь с приложением MogLocker установлена";
  connection.classList.remove("disconnected");
  connection.classList.add("connected");
  const allowed = (state.allowlist || []).some(domain =>
    currentDomain === domain || currentDomain.endsWith("." + domain));
  allowButton.textContent = allowed ? "Убрать из Allowlist" : "Добавить в Allowlist";
  allowButton.disabled = !currentDomain;
  error.textContent = "";
}

async function initialize() {
  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    if (tabs[0] && tabs[0].url) {
      try {
        currentDomain = new URL(tabs[0].url).hostname;
      } catch (_) {
        currentDomain = "";
      }
    }
    siteHost.textContent = currentDomain || "Для этой страницы недоступно";
    render(await send("getState"));
  } catch (exception) {
    connection.textContent = "Приложение не подключено";
    connection.classList.remove("connected");
    connection.classList.add("disconnected");
    error.textContent = exception.message;
  }
}

enabled.addEventListener("change", async () => {
  enabled.disabled = true;
  try {
    render(await send("setEnabled", { enabled: enabled.checked }));
  } catch (exception) {
    error.textContent = exception.message;
    enabled.checked = Boolean(state && state.enabled);
  } finally {
    enabled.disabled = false;
  }
});

allowButton.addEventListener("click", async () => {
  allowButton.disabled = true;
  const allowed = (state.allowlist || []).some(domain =>
    currentDomain === domain || currentDomain.endsWith("." + domain));
  try {
    render(await send("toggleAllowlist", {
      domain: currentDomain,
      allow: !allowed
    }));
  } catch (exception) {
    error.textContent = exception.message;
  } finally {
    allowButton.disabled = !currentDomain;
  }
});

initialize();
