// Progress: pick an exercise, then see its estimated 1RM, trend, personal records and recent sessions.

import { api } from '../api.js';
import { lineChart } from '../chart.js';
import { esc, escName, fmtDate, fmtWeight, plural, settings, icon } from '../util.js';

const RECENT_COUNT = 5;

const PERIOD_LABELS = {
  month: '1 month ago',
  threeMonths: '3 months ago',
  year: '1 year ago',
  start: 'First session',
};

/** Always shows the year, unlike fmtDate: progress spans years. */
const fmtFullDate = (date) => new Date(date).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
const fmtMonth = (date) => new Date(date).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });

const kg = (value) => `${fmtWeight(value)} ${settings.weightUnit}`;

// Sets with no weight (bodyweight work) read as "12 reps" rather than "0 kg × 12".
const fmtSet = (weight, reps) => (weight > 0 ? `${kg(weight)} × ${reps}` : plural(reps, 'rep'));
const fmtSets = (sets) => sets.map((s) => (s.weight > 0 ? `${fmtWeight(s.weight)} × ${s.reps}` : s.reps)).join(', ')
  + (sets.some((s) => s.weight > 0) ? '' : ' reps');

/**
 * The one measure a page is built around. Estimated 1RM where it can be worked out;
 * otherwise the heaviest weight, or for bodyweight exercises the most reps in a set.
 */
function pickMeasure(sessions) {
  if (sessions.some((s) => s.estimatedOneRepMax !== null)) {
    return {
      title: 'Estimated 1RM',
      format: kg,
      ofSession: (s) => s.estimatedOneRepMax,
      ofChange: (c) => [c.oneRepMaxThen, c.oneRepMaxNow],
    };
  }
  if (sessions.some((s) => s.topWeight > 0)) {
    return {
      title: 'Top weight',
      format: kg,
      ofSession: (s) => s.topWeight,
      ofChange: (c) => [c.topWeightThen, c.topWeightNow],
    };
  }
  return {
    title: 'Most reps in a set',
    wholeNumbers: true,
    format: (value) => plural(value, 'rep'),
    ofSession: (s) => s.bestReps,
    ofChange: (c) => [c.bestRepsThen, c.bestRepsNow],
  };
}

/** "▲ 10 kg (14%)" – direction is carried by the arrow and the sign of the wording, not just colour. */
function deltaHtml(then, now, format) {
  if (then === null || now === null) return '<span class="delta flat">No comparison</span>';
  const diff = Math.round((now - then) * 100) / 100;
  if (diff === 0) return '<span class="delta flat">No change</span>';
  const percent = then > 0 ? ` (${Math.abs(Math.round((diff / then) * 100))}%)` : '';
  return `
    <span class="delta ${diff > 0 ? 'up' : 'down'}">
      <span aria-hidden="true">${diff > 0 ? '▲' : '▼'}</span>
      <span class="visually-hidden">${diff > 0 ? 'Up' : 'Down'}</span>
      ${esc(format(Math.abs(diff)))}${percent}
    </span>`;
}

export async function progressView(el) {
  const exercises = await api.progressList();

  if (!exercises.length) {
    el.innerHTML = `
      <h1>Progress</h1>
      <p class="muted">Finish a workout and each exercise you logged will show up here with its records and trend.</p>
      <a class="btn primary" href="#/">Start a workout</a>`;
    return;
  }

  el.innerHTML = `
    <h1>Progress</h1>
    <p class="muted">Choose an exercise to see how it's going.</p>
    <ul class="list">
      ${exercises.map((exercise) => `
        <li>
          <a class="card link-card with-chevron" href="#/progress/${exercise.exerciseId}">
            <span>
              <strong>${escName(exercise.name)}</strong>
              <span class="muted small">${esc(plural(exercise.sessionCount, 'session'))} · last ${esc(fmtDate(exercise.lastPerformed))}</span>
            </span>
            ${icon('chevron')}
          </a>
        </li>`).join('')}
    </ul>`;
}

export async function progressDetailView(el, id) {
  let progress;
  try {
    progress = await api.progress(id);
  } catch (err) {
    if (err.status !== 404) throw err;
    el.innerHTML = `
      <h1>Exercise not found</h1>
      <p class="muted">It may have been deleted.</p>
      <a class="btn" href="#/progress">Back to progress</a>`;
    return;
  }

  const { sessions, records, changes } = progress;
  const header = `
    <p><a href="#/progress">‹ Progress</a></p>
    <h1>${escName(progress.name)}</h1>
    ${progress.isArchived ? '<p class="muted small">This exercise is archived.</p>' : ''}`;

  if (!sessions.length) {
    el.innerHTML = `
      ${header}
      <p class="muted">Nothing logged for this exercise yet. Finish a workout that includes it to see progress here.</p>`;
    return;
  }

  const measure = pickMeasure(sessions);
  const latest = sessions.at(-1);
  const points = sessions
    .filter((s) => measure.ofSession(s) !== null)
    .map((s) => ({ date: new Date(s.date), value: measure.ofSession(s) }));
  // The headline compares with a year ago when there's that much history, otherwise as far back as it goes.
  const headline = changes.find((c) => c.period === 'year') ?? changes.at(-1);
  const newestFirst = [...sessions].reverse();
  const recordSessions = new Set([records.bestOneRepMax?.workoutId ?? records.heaviestSet.workoutId]);

  const recordTile = (label, value, detail) => `
    <div class="stat">
      <dt>${esc(label)}</dt>
      <dd><strong>${esc(value)}</strong><span class="muted small">${esc(detail)}</span></dd>
    </div>`;

  const sessionRow = (session) => `
    <li>
      <a class="session-row" href="#/history/${session.workoutId}">
        <span>
          <strong>${esc(fmtFullDate(session.date))}</strong>
          ${recordSessions.has(session.workoutId) ? '<span class="badge">Best</span>' : ''}
          <span class="muted small">${esc(fmtSets(session.sets))}</span>
        </span>
        <span class="session-value">${measure.ofSession(session) === null ? '–' : esc(measure.format(measure.ofSession(session)))}</span>
      </a>
    </li>`;

  el.innerHTML = `
    ${header}

    <section class="card" aria-labelledby="headline-title">
      <h2 id="headline-title" class="eyebrow">${esc(measure.title)}</h2>
      <p class="hero-value">${measure.ofSession(latest) === null ? '–' : esc(measure.format(measure.ofSession(latest)))}</p>
      <p class="muted small">
        From your last session on ${esc(fmtFullDate(latest.date))}: best set ${esc(fmtSet(latest.topWeight, latest.topWeightReps))}.
      </p>
      ${headline ? `
        <p class="headline-delta">
          ${deltaHtml(...measure.ofChange(headline), measure.format)}
          <span class="muted small">vs ${esc(headline.period === 'start' ? 'your first session' : PERIOD_LABELS[headline.period])}</span>
        </p>` : ''}
    </section>

    <section class="card" aria-labelledby="chart-title">
      <h2 id="chart-title">${esc(measure.title)} over time</h2>
      ${points.length > 1
        ? '<div id="chart"></div>'
        : '<p class="muted small">Log this exercise in another workout to see a trend.</p>'}
    </section>

    ${changes.length ? `
      <section class="card" aria-labelledby="changes-title">
        <h2 id="changes-title">Then and now</h2>
        <ul class="rows">
          ${changes.map((change) => {
            const [then, now] = measure.ofChange(change);
            return `
              <li class="compare">
                <span>
                  <strong>${esc(PERIOD_LABELS[change.period])}</strong>
                  <span class="muted small">${esc(fmtFullDate(change.date))}</span>
                </span>
                <span class="compare-values">
                  <span>${then === null ? '–' : esc(measure.format(then))} → ${now === null ? '–' : esc(measure.format(now))}</span>
                  ${deltaHtml(then, now, measure.format)}
                </span>
              </li>`;
          }).join('')}
        </ul>
      </section>` : ''}

    <section class="card" aria-labelledby="records-title">
      <h2 id="records-title">Personal records</h2>
      <dl class="stat-grid">
        ${records.heaviestSet.weight > 0 ? recordTile('Heaviest set', fmtSet(records.heaviestSet.weight, records.heaviestSet.reps), fmtFullDate(records.heaviestSet.date)) : ''}
        ${records.bestOneRepMax ? recordTile('Best estimated 1RM', kg(records.bestOneRepMax.value),
          `${fmtWeight(records.bestOneRepMax.weight)} × ${records.bestOneRepMax.reps} · ${fmtFullDate(records.bestOneRepMax.date)}`) : ''}
        ${recordTile('Most reps in a set', plural(records.mostReps.reps, 'rep'),
          `${records.mostReps.weight ? `at ${kg(records.mostReps.weight)} · ` : ''}${fmtFullDate(records.mostReps.date)}`)}
        ${records.bestVolume.value > 0 ? recordTile('Biggest session', `${Math.round(records.bestVolume.value).toLocaleString()} ${settings.weightUnit}`,
          `total lifted · ${fmtFullDate(records.bestVolume.date)}`) : ''}
      </dl>
    </section>

    <section class="card" aria-labelledby="sessions-title">
      <h2 id="sessions-title">Recent sessions</h2>
      <ul class="rows" id="sessions">${newestFirst.slice(0, RECENT_COUNT).map(sessionRow).join('')}</ul>
      ${sessions.length > RECENT_COUNT
        ? `<button type="button" class="btn quiet block" id="show-all">Show all ${sessions.length} sessions</button>`
        : ''}
    </section>

    ${measure.title === 'Estimated 1RM' ? `
      <p class="muted small">
        Estimated 1RM is the most you could probably lift once, worked out from your best set with the
        Epley formula (weight × (1 + reps ÷ 30)). Sets of more than 12 reps aren't used.
      </p>` : ''}`;

  el.querySelector('#show-all')?.addEventListener('click', (event) => {
    el.querySelector('#sessions').innerHTML = newestFirst.map(sessionRow).join('');
    el.querySelector('#sessions-title').textContent = 'All sessions';
    event.target.remove();
  });

  const chart = el.querySelector('#chart');
  if (chart) {
    return lineChart(chart, points, {
      label: measure.title,
      wholeNumbers: measure.wholeNumbers,
      formatValue: measure.format,
      formatDate: fmtFullDate,
      formatAxisDate: fmtMonth,
    });
  }
}
