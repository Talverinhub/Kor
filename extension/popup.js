const statusLine = document.getElementById('status');
const nowBox = document.getElementById('now');
const boxes = [...document.querySelectorAll('input[data-source]')];
const texts = {
  tr: {
    allow: 'Kor\'da İzin ver düğmesine bas.',
    connected: 'Bağlı',
    outdated: 'Bağlı, ama eklenti eski. Kor Ayarlar, Müzik bölümünden TalverinConnect\'i yeniden kur.',
    offline: 'Kor\'a ulaşılamıyor. Uygulamayı aç ve Ayarlar, Müzik bölümünden TalverinConnect\'i etkinleştir.',
    start: 'YouTube Music sekmesini aç. Açıksa sayfayı yenile.',
    problem: 'okunamadı. Sayfayı yenile. Düzelmezse eklentiyi güncelle.',
    closed: 'Kor\'a ulaşılamıyor. Önce uygulamayı aç.',
    open: 'Kor\'u aç',
  },
  en: {
    allow: 'Press Allow in Kor.',
    connected: 'Connected',
    outdated: 'Connected, but the extension is out of date. Reinstall TalverinConnect from Settings, Music in Kor.',
    offline: 'Can\'t reach Kor. Open the app and turn on TalverinConnect in Settings, Music.',
    start: 'Open a YouTube Music tab. If it\'s open, refresh the page.',
    problem: 'could not be read. Refresh the page. If that doesn\'t help, update the extension.',
    closed: 'Can\'t reach Kor. Open the app first.',
    open: 'Open Kor',
  },
};
let text = texts.en;
const names = { 'youtube-music': 'YouTube Music' };

async function load() {
  const keys = boxes.flatMap((box) => [`off-${box.dataset.source}`, `problem-${box.dataset.source}`]);
  const stored = await chrome.storage.local.get(['status', 'lang', ...keys]);
  // Dil Kor'dan gelir; Kor'a henüz bağlanılmadıysa tarayıcı dili kullanılır.
  const lang = stored.lang ?? chrome.i18n.getUILanguage().slice(0, 2);
  text = texts[lang] ?? texts.en;
  document.documentElement.lang = texts[lang] ? lang : 'en';
  document.getElementById('open').textContent = text.open;
  statusLine.textContent = text[stored.status] ?? text.start;
  const broken = boxes.map((box) => box.dataset.source).filter((source) => stored[`problem-${source}`]);
  const problem = document.getElementById('problem');
  problem.hidden = broken.length === 0;
  problem.textContent = broken.map((source) => `${names[source]} ${text.problem}`).join(' ');
  for (const box of boxes) box.checked = !stored[`off-${box.dataset.source}`];
  const { now } = await chrome.storage.session.get('now');
  nowBox.hidden = !now;
  if (now) {
    document.getElementById('title').textContent = now.title;
    document.getElementById('artist').textContent = now.artist;
  }
}

for (const box of boxes) {
  box.addEventListener('change', async () => {
    await chrome.storage.local.set({ [`off-${box.dataset.source}`]: !box.checked });
  });
}

document.getElementById('open').addEventListener('click', async () => {
  const answer = await chrome.runtime.sendMessage({ t: 'show' });
  if (!answer?.ok) statusLine.textContent = text.closed;
});

// Pencere açılınca Kor'a bağlanılır; dil veya durum değişirse yazılar hemen yenilenir.
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === 'local' && (changes.lang || changes.status)) load();
});
chrome.runtime.sendMessage({ t: 'sync' }).catch(() => {});
load();
