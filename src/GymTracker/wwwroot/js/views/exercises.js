// Manage the exercise list and the sessions (Push, Pull, ...) they're grouped under.

import { api } from '../api.js';
import { esc, escName, toast, attempt, withBusy, icon } from '../util.js';

// Which exercise groups are expanded. Kept for the life of the page so it survives switching tabs.
const openGroups = new Set();
const groupKey = (sessionTypeId) => (sessionTypeId === null ? 'none' : `session-${sessionTypeId}`);

export async function exercisesView(el) {
  let [exercises, sessionTypes] = await Promise.all([api.exercises(), api.sessionTypes()]);
  let editing = null; // { kind: 'exercise' | 'session', id }
  let lastSessionId = sessionTypes[0]?.id ?? '';

  const isEditing = (kind, id) => editing?.kind === kind && editing.id === id;
  const byName = (a, b) => a.name.localeCompare(b.name);

  const sessionOptions = (selectedId) => `
    <option value="">No session</option>
    ${sessionTypes.map((s) => `<option value="${s.id}" ${s.id === selectedId ? 'selected' : ''}>${esc(s.name)}</option>`).join('')}`;

  function exerciseItem(exercise) {
    if (isEditing('exercise', exercise.id)) {
      return `
        <li>
          <form class="stack" data-form="edit-exercise" data-id="${exercise.id}" aria-label="Edit ${esc(exercise.name)}">
            <label>Name<input name="name" required maxlength="60" autocomplete="off" value="${esc(exercise.name)}"></label>
            <label>Session<select name="sessionTypeId">${sessionOptions(exercise.sessionTypeId)}</select></label>
            <div class="actions">
              <button type="submit" class="btn primary">Save</button>
              <button type="button" class="btn" data-action="cancel">Cancel</button>
              <button type="button" class="btn danger" data-action="delete-exercise" data-id="${exercise.id}">Delete</button>
            </div>
          </form>
        </li>`;
    }
    return `
      <li class="row">
        <span>${escName(exercise.name)}</span>
        ${exercise.isArchived
          ? `<button type="button" class="btn small" data-action="restore-exercise" data-id="${exercise.id}" aria-label="Restore ${esc(exercise.name)}">Restore</button>`
          : `<button type="button" class="btn quiet small" data-action="edit-exercise" data-id="${exercise.id}" aria-label="Edit ${esc(exercise.name)}">Edit</button>`}
      </li>`;
  }

  function sessionItem(session) {
    if (isEditing('session', session.id)) {
      return `
        <li>
          <form class="stack" data-form="edit-session" data-id="${session.id}" aria-label="Edit ${esc(session.name)}">
            <label>Name<input name="name" required maxlength="60" autocomplete="off" value="${esc(session.name)}"></label>
            <div class="actions">
              <button type="submit" class="btn primary">Save</button>
              <button type="button" class="btn" data-action="cancel">Cancel</button>
              <button type="button" class="btn danger" data-action="delete-session" data-id="${session.id}">Delete</button>
            </div>
          </form>
        </li>`;
    }
    return `
      <li class="row">
        <span>${esc(session.name)}</span>
        <button type="button" class="btn quiet small" data-action="edit-session" data-id="${session.id}" aria-label="Edit ${esc(session.name)}">Edit</button>
      </li>`;
  }

  function draw(focusSelector) {
    const active = exercises.filter((e) => !e.isArchived).sort(byName);
    const archived = exercises.filter((e) => e.isArchived).sort(byName);
    // Sessions always get a group, even when empty; the other two only appear when they have something in them.
    const groups = [
      ...sessionTypes.map((s) => ({ key: groupKey(s.id), name: s.name, items: active.filter((e) => e.sessionTypeId === s.id) })),
      { key: 'none', name: 'No session', items: active.filter((e) => e.sessionTypeId === null), optional: true },
      { key: 'archived', name: 'Archived', items: archived, optional: true, note: 'Hidden when adding exercises to a workout. Their history is kept.' },
    ].filter((g) => g.items.length || !g.optional);

    el.innerHTML = `
      <h1>Exercises</h1>

      <form class="card stack" data-form="add-exercise">
        <h2>Add exercise</h2>
        <label>Name<input name="name" required maxlength="60" autocomplete="off"></label>
        <label>Session<select name="sessionTypeId">${sessionOptions(lastSessionId)}</select></label>
        <button type="submit" class="btn primary">${icon('plus')}Add exercise</button>
      </form>

      ${groups.map((group) => `
        <details class="card group" data-group="${group.key}" ${openGroups.has(group.key) ? 'open' : ''}>
          <summary>
            <h2>${esc(group.name)}</h2>
            <span class="count">${group.items.length}<span class="visually-hidden"> exercises</span></span>
            ${icon('chevron')}
          </summary>
          <div class="group-body">
            ${group.note ? `<p class="muted small">${esc(group.note)}</p>` : ''}
            ${group.items.length
              ? `<ul class="rows">${group.items.map(exerciseItem).join('')}</ul>`
              : '<p class="muted small">No exercises in this session yet.</p>'}
          </div>
        </details>`).join('')}

      <h2 class="section-title">Sessions</h2>
      <section class="card">
        <p class="muted small">The workout types you can start from the Workout tab.</p>
        <ul class="rows">${sessionTypes.map(sessionItem).join('')}</ul>
        <form class="inline-form" data-form="add-session">
          <label>New session<input name="name" required maxlength="60" autocomplete="off" placeholder="e.g. Upper body"></label>
          <button type="submit" class="btn primary">${icon('plus')}Add</button>
        </form>
      </section>`;

    if (focusSelector) el.querySelector(focusSelector)?.focus();
  }

  const readExercise = (form) => ({
    name: form.elements.name.value,
    sessionTypeId: form.elements.sessionTypeId.value ? Number(form.elements.sessionTypeId.value) : null,
  });

  // "toggle" doesn't bubble, so listen in the capture phase.
  el.addEventListener('toggle', (event) => {
    const key = event.target.dataset?.group;
    if (!key) return;
    if (event.target.open) openGroups.add(key);
    else openGroups.delete(key);
  }, true);

  el.addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.target;
    const id = Number(form.dataset.id);

    await withBusy(form, async () => {
      switch (form.dataset.form) {
        case 'add-exercise': {
          const created = await api.addExercise(readExercise(form));
          exercises.push(created);
          lastSessionId = created.sessionTypeId ?? '';
          openGroups.add(groupKey(created.sessionTypeId));
          toast(`${created.name} added.`);
          draw('[data-form="add-exercise"] input[name="name"]');
          break;
        }
        case 'edit-exercise': {
          const updated = await api.updateExercise(id, readExercise(form));
          exercises = exercises.map((e) => (e.id === id ? updated : e));
          editing = null;
          openGroups.add(groupKey(updated.sessionTypeId)); // it may have moved to another group
          draw(`[data-action="edit-exercise"][data-id="${id}"]`);
          break;
        }
        case 'add-session': {
          const created = await api.addSessionType(form.elements.name.value);
          sessionTypes.push(created);
          toast(`${created.name} added.`);
          draw('[data-form="add-session"] input');
          break;
        }
        case 'edit-session': {
          const updated = await api.renameSessionType(id, form.elements.name.value);
          sessionTypes = sessionTypes.map((s) => (s.id === id ? updated : s));
          editing = null;
          draw(`[data-action="edit-session"][data-id="${id}"]`);
          break;
        }
      }
    });
  });

  el.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-action]');
    if (!button) return;
    const id = Number(button.dataset.id);

    switch (button.dataset.action) {
      case 'edit-exercise':
      case 'edit-session':
        editing = { kind: button.dataset.action.replace('edit-', ''), id };
        draw(`form[data-id="${id}"] input[name="name"]`);
        break;

      case 'cancel': {
        const selector = `[data-action="edit-${editing.kind}"][data-id="${editing.id}"]`;
        editing = null;
        draw(selector);
        break;
      }

      case 'delete-exercise': {
        const exercise = exercises.find((e) => e.id === id);
        if (!confirm(`Delete ${exercise.name}? If it has been used in a workout it will be archived instead.`)) return;
        await attempt(async () => {
          const result = await api.deleteExercise(id);
          exercises = result.archived
            ? exercises.map((e) => (e.id === id ? { ...e, isArchived: true } : e))
            : exercises.filter((e) => e.id !== id);
          editing = null;
          if (result.archived) openGroups.add('archived');
          toast(result.archived ? `${exercise.name} archived to keep its history.` : `${exercise.name} deleted.`);
          draw(result.archived ? `[data-action="restore-exercise"][data-id="${id}"]` : undefined);
        });
        break;
      }

      case 'restore-exercise': {
        const exercise = exercises.find((e) => e.id === id);
        await attempt(async () => {
          const updated = await api.updateExercise(id, { name: exercise.name, sessionTypeId: exercise.sessionTypeId, isArchived: false });
          exercises = exercises.map((e) => (e.id === id ? updated : e));
          openGroups.add(groupKey(updated.sessionTypeId));
          toast(`${updated.name} restored.`);
          draw(`[data-action="edit-exercise"][data-id="${id}"]`);
        });
        break;
      }

      case 'delete-session': {
        const session = sessionTypes.find((s) => s.id === id);
        if (!confirm(`Delete the ${session.name} session? Its exercises and past workouts are kept.`)) return;
        await attempt(async () => {
          await api.deleteSessionType(id);
          sessionTypes = sessionTypes.filter((s) => s.id !== id);
          exercises = exercises.map((e) => (e.sessionTypeId === id ? { ...e, sessionTypeId: null } : e));
          if (lastSessionId === id) lastSessionId = '';
          openGroups.delete(groupKey(id));
          editing = null;
          toast(`${session.name} deleted.`);
          draw();
        });
        break;
      }
    }
  });

  draw();
}
