// Connects to the Automation Test program on 127.0.0.1 and executes its commands.
// Commands: { id, cmd, args }.  Replies: { id, result } or { id, error }.  Events: { event, tabId, method, params }.
const URL = "ws://127.0.0.1:47615/";
const attached = new Set();
let socket = null;
let pingTimer = null;

function connect() {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) return;
  try {
    socket = new WebSocket(URL);
  } catch (e) {
    return;
  }
  socket.onopen = () => {
    // A message every 20 s keeps the service worker alive while the program is connected.
    clearInterval(pingTimer);
    pingTimer = setInterval(() => send({ event: "ping" }), 20000);
  };
  socket.onmessage = async (ev) => {
    let msg;
    try { msg = JSON.parse(ev.data); } catch (e) { return; }
    try {
      send({ id: msg.id, result: await run(msg.cmd, msg.args || {}) });
    } catch (e) {
      send({ id: msg.id, error: String((e && e.message) || e) });
    }
  };
  socket.onclose = () => { clearInterval(pingTimer); socket = null; };
  socket.onerror = () => { try { socket.close(); } catch (e) { } };
}

function send(obj) {
  if (socket && socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(obj));
}

async function ensureAttached(tabId) {
  if (attached.has(tabId)) return;
  await chrome.debugger.attach({ tabId }, "1.3");
  attached.add(tabId);
}

async function run(cmd, args) {
  switch (cmd) {
    case "hello":
      return { browser: navigator.userAgent };
    case "tabs": {
      const tabs = await chrome.tabs.query({});
      return tabs.map(t => ({ id: t.id, url: t.url, title: t.title, active: t.active, windowId: t.windowId }));
    }
    case "openUrl": {
      // args: { url, newTab, tabId }
      let tab;
      if (args.newTab || args.tabId == null) tab = await chrome.tabs.create({ url: args.url, active: true });
      else tab = await chrome.tabs.update(args.tabId, { url: args.url, active: true });
      return { tabId: tab.id };
    }
    case "cdp": {
      // args: { tabId, method, params }
      await ensureAttached(args.tabId);
      return await chrome.debugger.sendCommand({ tabId: args.tabId }, args.method, args.params || {});
    }
    default:
      throw new Error("Unknown command: " + cmd);
  }
}

chrome.debugger.onEvent.addListener((source, method, params) => {
  send({ event: "cdp", tabId: source.tabId, method, params });
});
chrome.debugger.onDetach.addListener((source) => attached.delete(source.tabId));
chrome.tabs.onRemoved.addListener((tabId) => attached.delete(tabId));

// Try to connect now and then, so the extension finds the program whenever it starts.
chrome.alarms.create("reconnect", { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(connect);
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
setInterval(connect, 3000);
connect();
