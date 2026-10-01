// Home: pick a session to start. If a workout is already in progress, go straight to it.

import { api } from '../api.js';
import { esc, attempt, icon, setWorkoutActive } from '../util.js';
import { workoutSummaryItem } from './history.js';

export async function homeView(el) {
  const active = await api.activeWorkout();
  setWorkoutActive(Boolean(active));
  if (active) {
    location.replace('#/workout');
    return;
  }

  const [sessionTypes, recent] = await Promise.all([api.sessionTypes(), api.workouts(0, 3)]);

  const startSection = sessionTypes.length
    ? `
      <section aria-labelledby="start-heading">
        <h2 id="start-heading">Start a workout</h2>
        <div class="session-grid">
          ${sessionTypes.map((s) => `
            <button type="button" class="btn session accent-${s.id % 6}" data-session="${s.id}">
              <span class="session-icon">${icon('dumbbell')}</span>
              <span class="session-name">${esc(s.name)}</span>
              <span class="session-start" aria-hidden="true">Start ${icon('chevron')}</span>
            </button>`).join('')}
        </div>
      </section>`
    : `
      <section class="card">
        <p>You don't have any sessions yet.</p>
        <a class="btn primary block" href="#/exercises">Set up sessions</a>
      </section>`;

  el.innerHTML = `
    <h1>Gym Tracker</h1>
    ${startSection}
    <section aria-labelledby="recent-heading">
      <h2 id="recent-heading">Recent workouts</h2>
      ${recent.length
        ? `<ul class="list">${recent.map(workoutSummaryItem).join('')}</ul>
           <p><a href="#/history">See all history</a></p>`
        : '<p class="muted">Nothing logged yet. Your finished workouts will show up here.</p>'}
    </section>`;

  el.querySelectorAll('[data-session]').forEach((button) => {
    button.addEventListener('click', async () => {
      button.disabled = true;
      const started = await attempt(() => api.startWorkout(Number(button.dataset.session)));
      if (started) location.hash = '#/workout';
      else button.disabled = false;
    });
  });
}
