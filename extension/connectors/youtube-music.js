// YouTube Music bağlayıcısı.
(() => {
  const bar = () => document.querySelector('ytmusic-player-bar');
  const media = () => document.querySelector('video');
  const text = (selector) => bar()?.querySelector(selector)?.textContent?.trim() ?? '';
  const click = (selector) => bar()?.querySelector(selector)?.click();

  TalverinConnect.register({
    source: 'youtube-music',
    capabilities: ['play', 'next', 'previous', 'seek', 'volume', 'shuffle', 'repeat', 'like'],
    root: bar,
    ignore: '#progress-bar, .time-info',
    read() {
      const player = bar();
      const video = media();
      const meta = navigator.mediaSession?.metadata;
      const title = meta?.title || text('.title');
      if (!player || !video || !title) return null;
      const artist = meta?.artist || (text('.subtitle .byline') || text('.subtitle')).split('•')[0].trim();
      const artwork = meta?.artwork?.at(-1)?.src;
      return {
        title,
        artist,
        cover: artwork || player.querySelector('img.image')?.src || '',
        playing: !video.paused,
        position: Math.round(video.currentTime * 1000),
        duration: Math.round((video.duration || 0) * 1000),
        volume: Math.round(video.volume * 100),
        repeat: player.getAttribute('repeat-mode') ?? 'NONE',
        liked: player.querySelector('ytmusic-like-button-renderer')?.getAttribute('like-status') === 'LIKE',
      };
    },
    run(name, value) {
      const video = media();
      if (name === 'toggle') click('#play-pause-button');
      else if (name === 'next') click('.next-button');
      else if (name === 'previous') click('.previous-button');
      else if (name === 'shuffle') click('.shuffle');
      else if (name === 'repeat') click('.repeat');
      else if (name === 'like') click('ytmusic-like-button-renderer #button-shape-like button');
      else if (name === 'seek' && video) video.currentTime = Number(value) / 1000;
      else if (name === 'volume' && video) video.volume = Number(value) / 100;
    },
  });
})();
