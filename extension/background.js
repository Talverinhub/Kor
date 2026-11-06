// TalverinConnect köprüsü: sekmelerdeki bağlayıcılar ile bu bilgisayardaki uygulama arasında mesaj taşır.
// Hizmet çalışanı her an kapanabilir; sekmeler yeniden bağlanınca bağlantı kendiliğinden kurulur.
const ADDRESS = 'ws://127.0.0.1:8899/baglan';
const PROTOCOL = 1;
const KEEPALIVE_MS = 20000;
const RETRY_MS = 3000;

const ports = new Map();
const activeTabs = new Set();
let socket = null;
let keepAlive = null;
let retry = null;

function setStatus(status) {
  chrome.storage.local.set({ status }).catch(() => {});
}

function browserName() {
  if (navigator.brave) return 'brave';
  return navigator.userAgent.includes('Edg/') ? 'msedge' : 'chrome';
}

function send(message) {
  if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify(message));
}

function connect() {
  if (socket && socket.readyState <= WebSocket.OPEN) return;
  clearTimeout(retry);
  socket = new WebSocket(ADDRESS);
  socket.onopen = () => {
    send({ t: 'hello', protocol: PROTOCOL, version: chrome.runtime.getManifest().version, browser: browserName() });
    // Şarkı yokken canlı tutma gönderilmez, arka plan servisi uyuyabilir.
    keepAlive = setInterval(() => { if (activeTabs.size > 0) send({ t: 'ping' }); }, KEEPALIVE_MS);
    setStatus('allow');
  };
  socket.onmessage = (event) => {
    const message = JSON.parse(event.data);
    if (message.t === 'lang') {
      chrome.storage.local.set({ lang: message.lang }).catch(() => {});
    } else if (message.t === 'welcome') {
      if (message.update) reloadOnce(message.latest);
      setStatus(message.update ? 'outdated' : 'connected');
      for (const port of ports.values()) port.postMessage({ t: 'resend' });
    } else if (message.t === 'command') {
      ports.get(message.tab)?.postMessage(message);
    }
  };
  socket.onclose = () => {
    clearInterval(keepAlive);
    socket = null;
    setStatus('offline');
    if (ports.size > 0) retry = setTimeout(connect, RETRY_MS);
  };
}

// Geliştirici kurulumunda Kor dosyaları yenileyince eklenti kendini bir kez yeniden yükler.
async function reloadOnce(latest) {
  if (!latest || 'update_url' in chrome.runtime.getManifest()) return;
  const { reloadedFor } = await chrome.storage.local.get('reloadedFor');
  if (reloadedFor === latest) return;
  await chrome.storage.local.set({ reloadedFor: latest });
  chrome.runtime.reload();
}

function remember(message) {
  if (message.t === 'health') {
    chrome.storage.local.set({ [`problem-${message.source}`]: message.error }).catch(() => {});
    return;
  }
  if (message.t !== 'state') return;
  const now = message.active ? { title: message.title, artist: message.artist, source: message.source } : null;
  chrome.storage.session.set({ now }).catch(() => {});
}

chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== 'talverin' || !port.sender?.tab) return;
  const tab = port.sender.tab.id;
  ports.set(tab, port);
  connect();
  port.onMessage.addListener((message) => {
    connect();
    if (message.t === 'state') {
      if (message.active) activeTabs.add(tab); else activeTabs.delete(tab);
    }
    remember(message);
    send({ ...message, tab });
  });
  port.onDisconnect.addListener(() => {
    ports.delete(tab);
    activeTabs.delete(tab);
    send({ t: 'gone', tab });
    if (activeTabs.size === 0) chrome.storage.session.set({ now: null }).catch(() => {});
  });
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.t === 'sync') {
    connect();
    return false;
  }
  if (message.t !== 'show') return false;
  connect();
  send({ t: 'show' });
  sendResponse({ ok: socket?.readyState === WebSocket.OPEN });
  return false;
});

// Tarayıcı, kurulumdan önce açık olan sekmelere bağlayıcı eklemez; sayfa yenilemeye gerek kalmasın.
chrome.runtime.onInstalled.addListener(async () => {
  for (const script of chrome.runtime.getManifest().content_scripts) {
    const tabs = await chrome.tabs.query({ url: script.matches });
    for (const tab of tabs) {
      try {
        await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: script.js });
      } catch {
        // Yüklenmemiş veya uyuyan sekme ilk açılışında bağlanır.
      }
    }
  }
});
