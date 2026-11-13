// ==UserScript==
// @name         Auxline: live media position for Windows
// @namespace    https://github.com/ksadowy/Auxline
// @version      1.0.0
// @description  Reports playback position via the Media Session API, so Firefox-based browsers pass a live timeline to Windows media controls (used by Auxline's progress bar).
// @match        *://*/*
// @run-at       document-start
// @grant        none
// ==/UserScript==

(() => {
  'use strict';

  if (!navigator.mediaSession?.setPositionState) return;

  // Moments when the position jumps or the timeline changes. Between them Windows (and Auxline)
  // extrapolate from position + timestamp + rate.
  const POSITION_EVENTS = ['loadedmetadata', 'durationchange', 'play', 'playing', 'pause', 'seeked', 'ratechange'];

  function reportPosition(media) {
    const { duration, currentTime, playbackRate } = media;
    // NaN before metadata loads, Infinity for live streams; setPositionState throws on both.
    if (!Number.isFinite(duration) || duration <= 0) return;

    try {
      navigator.mediaSession.setPositionState({
        duration,
        position: Math.min(currentTime, duration),
        playbackRate: playbackRate > 0 ? playbackRate : 1,
      });
    } catch (error) {
      console.debug('[Auxline] setPositionState rejected:', error);
    }
  }

  // Firefox clears the system timeline on its own at unpredictable moments (e.g. around metadata updates),
  // so also re-report periodically while playing; a cleared timeline comes back within this interval.
  const REFRESH_INTERVAL_MS = 5000;
  let lastRefreshAt = 0;

  function refreshWhilePlaying(media) {
    const now = Date.now();
    if (media.paused || now - lastRefreshAt < REFRESH_INTERVAL_MS) return;
    lastRefreshAt = now;
    reportPosition(media);
  }

  // Media events don't bubble, so capture them on the document. This also covers elements created later
  // (e.g. YouTube swapping videos without a page reload).
  for (const type of POSITION_EVENTS) {
    document.addEventListener(type, (event) => {
      if (event.target instanceof HTMLMediaElement) reportPosition(event.target);
    }, true);
  }
  document.addEventListener('timeupdate', (event) => {
    if (event.target instanceof HTMLMediaElement) refreshWhilePlaying(event.target);
  }, true);
})();
