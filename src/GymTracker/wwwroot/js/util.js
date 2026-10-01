// Small shared helpers: escaping, formatting, toasts.

export const settings = { weightUnit: 'kg' };

const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
export const esc = (value) => String(value ?? '').replace(/[&<>"']/g, (c) => ESCAPES[c]);

/**
 * Escapes an exercise name for display, keeping hyphenated words ("Half-Kneeling", "3-1-1") on one
 * line so names only wrap at spaces. Very long hyphenated words are left alone so they can't overflow.
 */
export const escName = (name) => String(name ?? '')
  .split(' ')
  .map((word) => (word.includes('-') && word.length <= 18 ? `<span class="nobreak">${esc(word)}</span>` : esc(word)))
  .join(' ');

/** 60 -> "60", 62.5 -> "62.5" */
export const fmtWeight = (weight) => String(Math.round(weight * 100) / 100);

/** "60 kg × 8" */
export const fmtSet = (set) => `${fmtWeight(set.weight)} ${settings.weightUnit} × ${set.reps}`;

/** Compact list for "last time" lines: "60 × 8, 60 × 8, 55 × 10" */
export const fmtSetList = (sets) => sets.map((s) => `${fmtWeight(s.weight)} × ${s.reps}`).join(', ');

export function fmtDate(iso) {
  const date = new Date(iso);
  const sameYear = date.getFullYear() === new Date().getFullYear();
  return date.toLocaleDateString(undefined, {
    weekday: 'short', day: 'numeric', month: 'short', year: sameYear ? undefined : 'numeric',
  });
}

export const fmtTime = (iso) => new Date(iso).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });

export function fmtDuration(startIso, endIso) {
  const minutes = Math.max(0, Math.round((new Date(endIso ?? Date.now()) - new Date(startIso)) / 60000));
  if (minutes < 60) return `${minutes} min`;
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')} min`;
}

export const plural = (count, word) => `${count} ${word}${count === 1 ? '' : 's'}`;

let toastTimer;
/** Confirmations clear quickly so they don't sit over the screen; errors stay long enough to read. */
export function toast(message, { error = false } = {}) {
  const el = document.getElementById('toast');
  el.textContent = message;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), error ? 5000 : 2000);
}

/** Shows or hides the dot on the Workout tab that says a workout is in progress. */
export function setWorkoutActive(active) {
  document.getElementById('active-dot').hidden = !active;
}

/** Runs an async action, showing any failure as a toast. Returns true on success. */
export async function attempt(action) {
  try {
    await action();
    return true;
  } catch (err) {
    console.error(err);
    toast(err.message || 'Something went wrong.', { error: true });
    return false;
  }
}

/** Disables a form's buttons while its submit handler runs, to stop double submissions. */
export async function withBusy(form, action) {
  const buttons = [...form.querySelectorAll('button')].filter((b) => !b.disabled);
  buttons.forEach((b) => (b.disabled = true));
  try {
    return await attempt(action);
  } finally {
    buttons.forEach((b) => (b.disabled = false));
  }
}

// Inline SVG icons (24×24, drawn with the current text colour). Decorative: always paired with text or an aria-label.
const ICONS = {
  plus: '<path d="M12 5v14M5 12h14"/>',
  check: '<path d="M5 12.5l4.5 4.5L19 7.5"/>',
  play: '<path d="M8 5.5v13l10.5-6.5z" fill="currentColor"/>',
  close: '<path d="M6 6l12 12M18 6L6 18"/>',
  chevron: '<path d="M9 6l6 6-6 6"/>',
  trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 12a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-12M10 11v5M14 11v5"/>',
  dumbbell: '<path d="M6.5 6.5v11M3.5 9v6M17.5 6.5v11M20.5 9v6M6.5 12h11"/>',
};

export const icon = (name) =>
  `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">${ICONS[name]}</svg>`;
