// History: list of finished workouts, and the detail of a single one.

import { api } from '../api.js';
import { esc, escName, fmtDate, fmtTime, fmtDuration, fmtSet, plural, settings, fmtWeight, toast, attempt, icon } from '../util.js';

const PAGE_SIZE = 30;

export function workoutSummaryItem(workout) {
  return `
    <li>
      <a class="card link-card" href="#/history/${workout.id}">
        <span class="row">
          <strong>${esc(workout.name)}</strong>
          <span class="muted">${esc(fmtDate(workout.startedAt))}</span>
        </span>
        <span class="muted small">${esc(fmtDuration(workout.startedAt, workout.finishedAt))} · ${esc(plural(workout.setCount, 'set'))}</span>
        <span class="small">${workout.exerciseNames.map(escName).join(', ')}</span>
      </a>
    </li>`;
}

export async function historyView(el) {
  const first = await api.workouts(0, PAGE_SIZE);

  if (!first.length) {
    el.innerHTML = `
      <h1>History</h1>
      <p class="muted">No finished workouts yet.</p>
      <a class="btn primary" href="#/">Start a workout</a>`;
    return;
  }

  el.innerHTML = `
    <h1>History</h1>
    <ul class="list" id="workouts">${first.map(workoutSummaryItem).join('')}</ul>
    <button type="button" class="btn block" id="more" ${first.length < PAGE_SIZE ? 'hidden' : ''}>Load more</button>`;

  const list = el.querySelector('#workouts');
  const more = el.querySelector('#more');
  let loaded = first.length;

  more.addEventListener('click', async () => {
    more.disabled = true;
    await attempt(async () => {
      const page = await api.workouts(loaded, PAGE_SIZE);
      loaded += page.length;
      list.insertAdjacentHTML('beforeend', page.map(workoutSummaryItem).join(''));
      more.hidden = page.length < PAGE_SIZE;
    });
    more.disabled = false;
  });
}

export async function historyDetailView(el, id) {
  let workout;
  try {
    workout = await api.workout(id);
  } catch (err) {
    if (err.status !== 404) throw err;
    el.innerHTML = `
      <h1>Workout not found</h1>
      <p class="muted">It may have been deleted.</p>
      <a class="btn" href="#/history">Back to history</a>`;
    return;
  }

  if (!workout.finishedAt) {
    location.replace('#/workout');
    return;
  }

  const allSets = workout.exercises.flatMap((e) => e.sets);
  const volume = allSets.reduce((total, s) => total + s.weight * s.reps, 0);

  el.innerHTML = `
    <p><a href="#/history">‹ History</a></p>
    <h1>${esc(workout.name)}</h1>
    <p class="muted">
      ${esc(fmtDate(workout.startedAt))}, ${esc(fmtTime(workout.startedAt))} · ${esc(fmtDuration(workout.startedAt, workout.finishedAt))}<br>
      ${esc(plural(allSets.length, 'set'))} · ${esc(fmtWeight(volume))} ${esc(settings.weightUnit)} total volume
    </p>
    ${workout.exercises.map((exercise) => `
      <section class="card">
        <div class="row">
          <h2>${escName(exercise.exerciseName)}</h2>
          <a class="small" href="#/progress/${exercise.exerciseId}" aria-label="Progress for ${esc(exercise.exerciseName)}">Progress ›</a>
        </div>
        <ol class="sets">
          ${exercise.sets.map((set, i) => `
            <li><span class="set-number" aria-hidden="true">${i + 1}</span><span>${esc(fmtSet(set))}</span></li>`).join('')}
        </ol>
      </section>`).join('')}
    <button type="button" class="btn danger block" id="delete">${icon('trash')}Delete workout</button>`;

  el.querySelector('#delete').addEventListener('click', async () => {
    if (!confirm('Delete this workout from your history? This cannot be undone.')) return;
    if (await attempt(() => api.deleteWorkout(workout.id))) {
      toast('Workout deleted.');
      location.hash = '#/history';
    }
  });
}
