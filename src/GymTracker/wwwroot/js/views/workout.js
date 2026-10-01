// Active workout: add exercises and log sets. Built for one-handed use on a phone.

import { api } from '../api.js';
import {
  esc, escName, fmtDate, fmtTime, fmtDuration, fmtSet, fmtSetList, fmtWeight, plural,
  settings, toast, attempt, withBusy, icon, setWorkoutActive,
} from '../util.js';

const WEIGHT_ATTRS = 'type="number" inputmode="decimal" enterkeyhint="next" step="any" min="0" max="2000" required';
const REPS_ATTRS = 'type="number" inputmode="numeric" enterkeyhint="done" step="1" min="1" max="1000" required';

export async function workoutView(el) {
  const workout = await api.activeWorkout();
  setWorkoutActive(Boolean(workout));
  if (!workout) {
    el.innerHTML = `
      <h1>No workout in progress</h1>
      <a class="btn primary" href="#/">Start a workout</a>`;
    return;
  }

  const [exercises, sessionTypes, suggestions] = await Promise.all([
    api.exercises(),
    api.sessionTypes(),
    workout.exercises.length ? [] : api.suggestedExercises(workout.id),
  ]);

  el.innerHTML = `
    <header class="row workout-header">
      <div>
        <h1>${esc(workout.name)}</h1>
        <p class="muted" id="elapsed"></p>
      </div>
      <button type="button" class="btn success" id="finish">${icon('check')}Finish</button>
    </header>
    <div id="cards"></div>
    ${suggestions.length ? `
      <section class="card" id="suggestions" aria-labelledby="suggestions-title">
        <h2 id="suggestions-title">Same as last time?</h2>
        <p class="muted small">Your last ${esc(workout.name)} workout: ${suggestions.map((e) => escName(e.name)).join(', ')}</p>
        <button type="button" class="btn primary block" id="add-suggested">
          ${icon('plus')}Add ${suggestions.length === 1 ? 'this exercise' : `these ${suggestions.length} exercises`}
        </button>
      </section>` : '<p class="muted" id="empty-hint">Add your first exercise to start logging sets.</p>'}
    <button type="button" class="btn dashed block" id="add-exercise">${icon('plus')}Add exercise</button>
    <button type="button" class="btn danger quiet block" id="discard">${icon('trash')}Discard workout</button>

    <dialog id="picker" aria-labelledby="picker-title">
      <div class="dialog-body">
        <div class="row">
          <h2 id="picker-title">Add exercise</h2>
          <button type="button" class="btn icon-only" id="picker-close" aria-label="Close">${icon('close')}</button>
        </div>
        <input type="search" id="picker-search" placeholder="Search, or type a new name" aria-label="Search exercises" autocomplete="off" maxlength="60">
        <div id="picker-list"></div>
      </div>
    </dialog>`;

  const cards = el.querySelector('#cards');
  // Either the "same as last time?" offer or a plain hint; both only make sense while the workout is empty.
  const emptyHint = el.querySelector('#suggestions, #empty-hint');
  const elapsed = el.querySelector('#elapsed');

  const updateElapsed = () => {
    elapsed.textContent = `Started ${fmtTime(workout.startedAt)} · ${fmtDuration(workout.startedAt)}`;
  };
  updateElapsed();
  const timer = setInterval(updateElapsed, 30000);

  const updateEmptyHint = () => (emptyHint.hidden = workout.exercises.length > 0);

  // ----- Exercise cards -----

  /** What the "log set" inputs start with: the last set logged today, else the first set from last time. */
  function defaultDraft(entry) {
    const basis = entry.sets.at(-1) ?? entry.previous?.sets[0];
    return basis ? { weight: fmtWeight(basis.weight), reps: String(basis.reps) } : { weight: '', reps: '' };
  }

  function renderCard(entry) {
    entry.draft ??= defaultDraft(entry);
    const name = esc(entry.exerciseName);
    const unit = esc(settings.weightUnit);

    const previous = entry.previous
      ? `<span>Last time (${esc(fmtDate(entry.previous.date))}): ${esc(fmtSetList(entry.previous.sets))}</span>
         <a href="#/progress/${entry.exerciseId}" aria-label="Progress for ${name}">Progress ›</a>`
      : '<span>First time logging this exercise.</span>';

    const setRows = entry.sets.map((set, i) => set.id === entry.editingSetId
      ? `
        <li>
          <form class="set-form editing" data-form="edit-set" data-set="${set.id}" aria-label="Edit set ${i + 1} of ${name}">
            <label>Weight (${unit})<input name="weight" ${WEIGHT_ATTRS} value="${esc(fmtWeight(set.weight))}"></label>
            <label>Reps<input name="reps" ${REPS_ATTRS} value="${set.reps}"></label>
            <div class="actions">
              <button type="submit" class="btn primary">Save</button>
              <button type="button" class="btn" data-action="cancel-edit">Cancel</button>
              <button type="button" class="btn danger" data-action="delete-set" data-set="${set.id}">Delete</button>
            </div>
          </form>
        </li>`
      : `
        <li>
          <button type="button" class="set-row" data-action="edit-set" data-set="${set.id}">
            <span class="set-number" aria-hidden="true">${i + 1}</span>
            <span class="visually-hidden">Set ${i + 1}:</span>
            <span class="set-value">${esc(fmtSet(set))}</span>
            <span class="muted small">Edit</span>
          </button>
        </li>`).join('');

    const card = document.createElement('section');
    card.className = 'card exercise';
    card.dataset.entry = entry.id;
    card.setAttribute('aria-label', entry.exerciseName);
    card.innerHTML = `
      <div class="row">
        <h2>${escName(entry.exerciseName)}</h2>
        <button type="button" class="btn icon-only" data-action="remove" aria-label="Remove ${name} from workout" title="Remove from workout">${icon('close')}</button>
      </div>
      <p class="muted small previous">${previous}</p>
      ${entry.sets.length ? `<ol class="sets">${setRows}</ol>` : ''}
      <form class="set-form" data-form="add-set" aria-label="Log a set of ${name}">
        <label>Weight (${unit})<input name="weight" ${WEIGHT_ATTRS} value="${esc(entry.draft.weight)}"></label>
        <label>Reps<input name="reps" ${REPS_ATTRS} value="${esc(entry.draft.reps)}"></label>
        <button type="submit" class="btn primary">${icon('check')}Log set ${entry.sets.length + 1}</button>
      </form>`;
    return card;
  }

  /** Re-renders one card in place, then moves focus to the given element inside it. */
  function refreshCard(entry, focusSelector) {
    const card = renderCard(entry);
    cards.querySelector(`[data-entry="${entry.id}"]`).replaceWith(card);
    if (focusSelector) card.querySelector(focusSelector)?.focus();
  }

  const entryFor = (node) => {
    const id = Number(node.closest('[data-entry]').dataset.entry);
    return workout.exercises.find((e) => e.id === id);
  };

  const readSet = (form) => ({
    weight: form.elements.weight.valueAsNumber,
    reps: form.elements.reps.valueAsNumber,
  });

  workout.exercises.forEach((entry) => cards.append(renderCard(entry)));
  updateEmptyHint();

  // Remember what's typed in the "log set" inputs so it survives a card re-render.
  cards.addEventListener('input', (event) => {
    const form = event.target.closest('[data-form="add-set"]');
    if (form) entryFor(form).draft = { weight: form.elements.weight.value, reps: form.elements.reps.value };
  });

  cards.addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.target;
    const entry = entryFor(form);

    if (form.dataset.form === 'add-set') {
      await withBusy(form, async () => {
        const set = await api.addSet(entry.id, readSet(form));
        entry.sets.push(set);
        entry.draft = { weight: fmtWeight(set.weight), reps: String(set.reps) };
        refreshCard(entry, '[data-form="add-set"] button');
        // The new row flashes instead of a toast, which would sit over the next card.
        cards.querySelector(`[data-action="edit-set"][data-set="${set.id}"]`)?.classList.add('flash');
      });
    } else if (form.dataset.form === 'edit-set') {
      const setId = Number(form.dataset.set);
      await withBusy(form, async () => {
        const updated = await api.updateSet(setId, readSet(form));
        entry.sets = entry.sets.map((s) => (s.id === setId ? updated : s));
        entry.editingSetId = null;
        refreshCard(entry, `[data-action="edit-set"][data-set="${setId}"]`);
      });
    }
  });

  // Tapping into a filled-in box selects its contents, so typing replaces the value instead of adding to it.
  cards.addEventListener('focusin', (event) => {
    if (event.target.matches('input[type="number"]')) event.target.select();
  });

  // Enter (or "next" on a phone keyboard) in the weight box moves to reps; in reps it logs the set.
  cards.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && event.target.name === 'weight') {
      event.preventDefault();
      event.target.form.elements.reps.focus();
    }
  });

  cards.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-action]');
    if (!button) return;
    const entry = entryFor(button);
    const setId = Number(button.dataset.set);

    switch (button.dataset.action) {
      case 'edit-set':
        entry.editingSetId = setId;
        refreshCard(entry, '.editing input[name="weight"]');
        break;

      case 'cancel-edit': {
        const editedId = entry.editingSetId;
        entry.editingSetId = null;
        refreshCard(entry, `[data-action="edit-set"][data-set="${editedId}"]`);
        break;
      }

      case 'delete-set':
        button.disabled = true;
        if (await attempt(() => api.deleteSet(setId))) {
          entry.sets = entry.sets.filter((s) => s.id !== setId);
          entry.editingSetId = null;
          refreshCard(entry, '[data-form="add-set"] button');
          toast('Set deleted.');
        } else {
          button.disabled = false;
        }
        break;

      case 'remove':
        if (entry.sets.length && !confirm(`Remove ${entry.exerciseName} and its ${entry.sets.length} logged set(s)?`)) return;
        button.disabled = true;
        if (await attempt(() => api.removeWorkoutExercise(entry.id))) {
          workout.exercises = workout.exercises.filter((e) => e !== entry);
          button.closest('[data-entry]').remove();
          updateEmptyHint();
          el.querySelector('#add-exercise').focus();
        } else {
          button.disabled = false;
        }
        break;
    }
  });

  // ----- Exercise picker -----

  const picker = el.querySelector('#picker');
  const search = el.querySelector('#picker-search');
  const pickerList = el.querySelector('#picker-list');

  function renderPicker() {
    const query = search.value.trim().toLowerCase();
    const inWorkout = new Set(workout.exercises.map((e) => e.exerciseId));
    const available = exercises.filter((e) => !inWorkout.has(e.id));
    const item = (e) => `
      <li>
        <button type="button" class="picker-item" data-exercise="${e.id}">
          <span>${escName(e.name)}${e.isArchived ? ' <span class="muted small">(archived)</span>' : ''}</span>
          ${icon('plus')}
        </button>
      </li>`;

    let html;
    if (query) {
      // Searching looks across every session, including archived exercises.
      const matches = available.filter((e) => e.name.toLowerCase().includes(query));
      const exists = exercises.some((e) => e.name.toLowerCase() === query);
      html = matches.length ? `<ul class="picker-group">${matches.map(item).join('')}</ul>` : '';
      if (exists && !matches.length) html = '<p class="muted">That exercise is already in this workout.</p>';
      if (!exists) html += `<button type="button" class="btn dashed block" id="picker-create">${icon('plus')}Create “${esc(search.value.trim())}”</button>`;
    } else {
      // This workout's session first, then the rest.
      const active = available.filter((e) => !e.isArchived);
      const groups = [
        ...sessionTypes.filter((s) => s.id === workout.sessionTypeId),
        ...sessionTypes.filter((s) => s.id !== workout.sessionTypeId),
        { id: null, name: 'Other' },
      ];
      html = groups.map((group) => {
        const members = active.filter((e) => (e.sessionTypeId ?? null) === group.id);
        return members.length
          ? `<h3>${esc(group.name)}</h3><ul class="picker-group">${members.map(item).join('')}</ul>`
          : '';
      }).join('') || '<p class="muted">No more exercises to add. Type a name above to create one.</p>';
    }
    pickerList.innerHTML = html;
  }

  async function appendExercise(exerciseId) {
    const entry = await api.addWorkoutExercise(workout.id, exerciseId);
    workout.exercises.push(entry);
    const card = renderCard(entry);
    cards.append(card);
    updateEmptyHint();
    return card;
  }

  /** Where focus goes on a fresh card: ready to type if the boxes are empty, ready to log if they're prefilled. */
  function focusCard(card) {
    const weight = card.querySelector('[data-form="add-set"] input[name="weight"]');
    (weight.value ? card.querySelector('[data-form="add-set"] button') : weight).focus({ preventScroll: true });
  }

  async function addToWorkout(exerciseId) {
    const card = await appendExercise(exerciseId);
    picker.close();
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });
    focusCard(card);
  }

  el.querySelector('#add-suggested')?.addEventListener('click', async (event) => {
    event.target.disabled = true;
    const before = workout.exercises.length;
    await attempt(async () => {
      for (const exercise of suggestions) {
        if (!workout.exercises.some((e) => e.exerciseId === exercise.id)) await appendExercise(exercise.id);
      }
    });
    event.target.disabled = false;
    if (workout.exercises.length > before) focusCard(cards.children[before]);
  });

  el.querySelector('#add-exercise').addEventListener('click', () => {
    search.value = '';
    renderPicker();
    picker.showModal();
  });
  el.querySelector('#picker-close').addEventListener('click', () => picker.close());
  // Tapping the backdrop closes the picker.
  picker.addEventListener('click', (event) => {
    if (event.target === picker) picker.close();
  });
  search.addEventListener('input', renderPicker);

  pickerList.addEventListener('click', async (event) => {
    const button = event.target.closest('button');
    if (!button) return;
    button.disabled = true;
    const added = await attempt(async () => {
      if (button.id === 'picker-create') {
        const created = await api.addExercise({ name: search.value, sessionTypeId: workout.sessionTypeId });
        exercises.push(created);
        await addToWorkout(created.id);
      } else {
        await addToWorkout(Number(button.dataset.exercise));
      }
    });
    if (!added) button.disabled = false;
  });

  // ----- Finish / discard -----

  el.querySelector('#finish').addEventListener('click', async (event) => {
    const setCount = workout.exercises.reduce((total, e) => total + e.sets.length, 0);
    if (!setCount) {
      toast('Log at least one set before finishing, or discard the workout.');
      return;
    }
    const unused = workout.exercises.filter((e) => !e.sets.length).length;
    const summary = `${plural(setCount, 'set')} across ${plural(workout.exercises.length - unused, 'exercise')}`;
    const dropped = unused ? `\n\n${plural(unused, 'exercise')} with no sets will be left out.` : '';
    if (!confirm(`Finish this workout?\n\n${summary}.${dropped}`)) return;
    event.target.disabled = true;
    if (await attempt(() => api.finishWorkout(workout.id))) {
      setWorkoutActive(false);
      toast('Workout saved.');
      location.hash = `#/history/${workout.id}`;
    } else {
      event.target.disabled = false;
    }
  });

  el.querySelector('#discard').addEventListener('click', async () => {
    if (!confirm('Discard this workout? Everything logged in it will be deleted.')) return;
    if (await attempt(() => api.deleteWorkout(workout.id))) {
      setWorkoutActive(false);
      toast('Workout discarded.');
      location.hash = '#/';
    }
  });

  return () => clearInterval(timer);
}
