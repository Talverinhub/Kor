// TalverinConnect ortak çekirdeği. Bağlayıcılar yalnızca şu yapıyı verir:
// { source, capabilities, root(), ignore, read(), run(name, value) }
// Çekirdek sayfayı olaylarla izler ve yalnızca durum değişince mesaj gönderir.
globalThis.TalverinConnect = (() => {
  const DEBOUNCE_MS = 40;
  const DRIFT_MS = 1500;
  const CHECK_MS = 5000;
  const IDLE_CHECK_MS = 30000;
  const BROKEN_AFTER_MS = 20000;

  let connector = null;
  let port = null;
  let timer = null;
  let lastKey = 'none';
  let lastPosition = 0;
  let lastAt = 0;
  let observed = null;
  let problem = '';
  let disabled = false;
  const startedAt = Date.now();

  function open() {
    if (port) return;
    try {
      port = chrome.runtime.connect({ name: 'talverin' });
    } catch {
      return;
    }
    port.onMessage.addListener((message) => {
      if (message.t === 'resend') {
        const last = problem;
        problem = '';
        report(last);
        publish(true);
      } else if (message.t === 'command') {
        connector.run(message.name, message.value);
        // Site tepkisi değişebildiği için komuttan sonra birkaç kez kontrol edilir.
        for (const delay of [60, 250, 800]) setTimeout(() => publish(false), delay);
      }
    });
    port.onDisconnect.addListener(() => {
      port = null;
      // Şarkı yokken yeniden bağlanılmaz; arka plan servisi uyuyabilir.
      if (lastKey !== 'none' && !disabled) setTimeout(open, 1000);
    });
  }

  function post(message) {
    open();
    try { port?.postMessage(message); } catch { port = null; }
  }

  // Yalnızca durum değişince bildirilir. Boş sorun metni sorunun bittiği anlamına gelir.
  function report(error) {
    if (error === problem) return;
    problem = error;
    post({ t: 'health', source: connector.source, error });
  }

  function clear() {
    if (lastKey !== 'none') post({ t: 'state', source: connector.source, active: false });
    lastKey = 'none';
  }

  function publish(force) {
    if (disabled) {
      clear();
      return;
    }
    let state;
    try {
      state = connector.read();
    } catch (error) {
      report(String(error));
      return;
    }
    // Şarkı olmaması sorun değildir; oynatıcının hiç bulunamaması sayfanın değiştiğini gösterir.
    const missing = !connector.root() && Date.now() - startedAt > BROKEN_AFTER_MS;
    report(missing ? 'oynatıcı bulunamadı' : '');
    if (!state) {
      clear();
      return;
    }
    const { position, ...stable } = state;
    const key = JSON.stringify(stable);
    const expected = lastPosition + (state.playing ? Date.now() - lastAt : 0);
    if (!force && key === lastKey && Math.abs(position - expected) < DRIFT_MS) return;
    lastKey = key;
    lastPosition = position;
    lastAt = Date.now();
    post({ t: 'state', source: connector.source, capabilities: connector.capabilities, active: true, ...state });
  }

  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(() => publish(false), DEBOUNCE_MS);
  }

  // Sürekli değişen öğeler (süre yazısı gibi) izlemeyi tetiklemez.
  function relevant(mutation) {
    if (!connector.ignore) return true;
    const node = mutation.target.nodeType === Node.ELEMENT_NODE ? mutation.target : mutation.target.parentElement;
    return !node?.closest(connector.ignore);
  }

  function observe() {
    const root = connector.root();
    if (!root || root === observed) return;
    observed = root;
    new MutationObserver((mutations) => {
      if (mutations.some(relevant)) schedule();
    }).observe(root, { subtree: true, childList: true, characterData: true, attributes: true });
    schedule();
  }

  // Oynatıcı yoksa sayfa daha seyrek kontrol edilir.
  function check() {
    observe();
    publish(false);
    setTimeout(check, connector.root() ? CHECK_MS : IDLE_CHECK_MS);
  }

  async function register(definition) {
    connector = definition;
    const key = `off-${connector.source}`;
    const stored = await chrome.storage.local.get(key);
    disabled = Boolean(stored[key]);
    chrome.storage.onChanged.addListener((changes) => {
      if (!changes[key]) return;
      disabled = Boolean(changes[key].newValue);
      publish(true);
    });
    for (const name of ['play', 'pause', 'seeked', 'volumechange', 'durationchange', 'loadedmetadata']) {
      document.addEventListener(name, schedule, true);
    }
    // İlk açılışta bir kez bağlanılır ki uygulamada onay ve durum görünsün.
    if (!disabled) open();
    check();
  }

  return { register };
})();
