// Entry point: a tiny hash router that swaps views into <main>.

import { api } from './api.js';
import { esc, settings, setWorkoutActive } from './util.js';
import { homeView } from './views/home.js';
import { workoutView } from './views/workout.js';
import { historyView, historyDetailView } from './views/history.js';
import { exercisesView } from './views/exercises.js';
import { progressView, progressDetailView } from './views/progress.js';

const routes = [
  { pattern: /^\/$/, view: homeView, tab: 'home' },
  { pattern: /^\/workout$/, view: workoutView, tab: 'home' },
  { pattern: /^\/history$/, view: historyView, tab: 'history' },
  { pattern: /^\/history\/(\d+)$/, view: historyDetailView, tab: 'history' },
  { pattern: /^\/progress$/, view: progressView, tab: 'progress' },
  { pattern: /^\/progress\/(\d+)$/, view: progressDetailView, tab: 'progress' },
  { pattern: /^\/exercises$/, view: exercisesView, tab: 'exercises' },
];

const main = document.getElementById('main');
let cleanup = null;
let navigation = 0;

async function render() {
  const path = location.hash.replace(/^#/, '') || '/';
  const route = routes.find((r) => r.pattern.test(path));
  if (!route) {
    location.replace('#/');
    return;
  }

  // Views may return a cleanup function (e.g. to stop timers).
  cleanup?.();
  cleanup = null;
  const thisNavigation = ++navigation;

  document.querySelectorAll('.tabs a').forEach((a) => {
    if (a.dataset.tab === route.tab) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });

  // Each view renders into its own container, so a slow view can't overwrite a newer one.
  const container = document.createElement('div');
  container.className = 'view';
  container.innerHTML = '<p class="muted">Loading…</p>';
  main.replaceChildren(container);
  window.scrollTo(0, 0);

  try {
    const result = await route.view(container, ...path.match(route.pattern).slice(1));
    if (thisNavigation === navigation) {
      cleanup = result ?? null;
      main.focus({ preventScroll: true });
    } else {
      result?.();
    }
  } catch (err) {
    console.error(err);
    container.innerHTML = `
      <h1>Something went wrong</h1>
      <p class="error" role="alert">${esc(err.message)}</p>
      <button type="button" class="btn" id="retry">Try again</button>`;
    container.querySelector('#retry').addEventListener('click', render);
  }
}

window.addEventListener('hashchange', render);

// So the Workout tab shows its "in progress" dot whichever page the app is opened on.
api.activeWorkout().then((workout) => setWorkoutActive(Boolean(workout))).catch(() => {});

api.config()
  .then((config) => Object.assign(settings, config))
  .catch(() => {})
  .finally(render);
