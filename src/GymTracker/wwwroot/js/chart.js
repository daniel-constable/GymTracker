// A small single-series line chart drawn as inline SVG. No dependencies.
// Hover, touch or the arrow keys move a crosshair that reads out the nearest point.

const SVG_NS = 'http://www.w3.org/2000/svg';
const HEIGHT = 200;
const MARGIN = { top: 26, right: 14, bottom: 26, left: 42 };
const MAX_MARKERS = 24; // beyond this, dots on every point turn into clutter

function svg(tag, attrs = {}, text) {
  const node = document.createElementNS(SVG_NS, tag);
  for (const [name, value] of Object.entries(attrs)) node.setAttribute(name, value);
  if (text !== undefined) node.textContent = text;
  return node;
}

/**
 * Round axis bounds and tick values that cover [min, max] in about `count` steps.
 * With `wholeNumbers`, ticks never fall between integers (for counts such as reps).
 */
export function niceTicks(min, max, count = 4, wholeNumbers = false) {
  if (min === max) {
    const pad = Math.abs(min) * 0.1 || 1;
    min -= pad;
    max += pad;
  }
  const rawStep = (max - min) / count;
  const magnitude = 10 ** Math.floor(Math.log10(rawStep));
  const normalised = rawStep / magnitude;
  let step = (normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 2.5 ? 2.5 : normalised <= 5 ? 5 : 10) * magnitude;
  if (wholeNumbers) step = Math.max(1, normalised > 2 && normalised <= 2.5 ? 5 * magnitude : step);
  const low = Math.floor(min / step) * step;
  const high = Math.ceil(max / step) * step;
  const ticks = [];
  for (let value = low; value <= high + step / 2; value += step) ticks.push(Math.round(value * 1000) / 1000);
  return ticks;
}

/**
 * Renders the chart into `container` and returns a function that removes its listeners.
 * @param points  [{ date: Date, value: number }] in date order
 * @param options { label, formatValue(value), formatDate(date), formatAxisDate(date), wholeNumbers }
 *                label names the measure for screen readers
 */
export function lineChart(container, points, { label, formatValue, formatDate, formatAxisDate = formatDate, wholeNumbers = false }) {
  let active = -1;
  let geometry = [];
  let parts = {};

  container.classList.add('chart');
  container.tabIndex = 0;
  container.setAttribute('role', 'group');
  const first = points[0];
  const last = points.at(-1);
  container.setAttribute('aria-label',
    `${label} across ${points.length} sessions, from ${formatValue(first.value)} on ${formatDate(first.date)} ` +
    `to ${formatValue(last.value)} on ${formatDate(last.date)}. Use the left and right arrow keys to read each session.`);

  const tip = document.createElement('div');
  tip.className = 'chart-tip';
  tip.setAttribute('role', 'status');
  tip.hidden = true;
  const tipValue = document.createElement('strong');
  const tipDate = document.createElement('span');
  tip.append(tipValue, tipDate);

  function draw() {
    const width = Math.max(container.clientWidth, 240);
    const plot = {
      left: MARGIN.left, right: width - MARGIN.right,
      top: MARGIN.top, bottom: HEIGHT - MARGIN.bottom,
    };

    const ticks = niceTicks(Math.min(...points.map((p) => p.value)), Math.max(...points.map((p) => p.value)), 4, wholeNumbers);
    const yMin = ticks[0];
    const yMax = ticks.at(-1);
    const tMin = first.date.getTime();
    const tMax = last.date.getTime();
    const x = (date) => (tMax === tMin
      ? (plot.left + plot.right) / 2
      : plot.left + ((date.getTime() - tMin) / (tMax - tMin)) * (plot.right - plot.left));
    const y = (value) => plot.bottom - ((value - yMin) / (yMax - yMin)) * (plot.bottom - plot.top);
    geometry = points.map((p) => ({ x: x(p.date), y: y(p.value) }));

    const root = svg('svg', { width, height: HEIGHT, viewBox: `0 0 ${width} ${HEIGHT}`, 'aria-hidden': 'true' });

    for (const tick of ticks) {
      root.append(
        svg('line', { class: 'chart-grid', x1: plot.left, x2: plot.right, y1: y(tick), y2: y(tick) }),
        svg('text', { class: 'chart-axis', x: plot.left - 8, y: y(tick), 'text-anchor': 'end', 'dominant-baseline': 'middle' },
          tick.toLocaleString()));
    }

    if (points.length > 1) {
      root.append(
        svg('text', { class: 'chart-axis', x: plot.left, y: HEIGHT - 6, 'text-anchor': 'start' }, formatAxisDate(first.date)),
        svg('text', { class: 'chart-axis', x: plot.right, y: HEIGHT - 6, 'text-anchor': 'end' }, formatAxisDate(last.date)),
        svg('path', { class: 'chart-line', d: geometry.map((g, i) => `${i ? 'L' : 'M'}${g.x.toFixed(1)} ${g.y.toFixed(1)}`).join(' ') }));
    } else {
      root.append(svg('text', { class: 'chart-axis', x: geometry[0].x, y: HEIGHT - 6, 'text-anchor': 'middle' }, formatAxisDate(first.date)));
    }

    const crosshair = svg('line', { class: 'chart-crosshair', y1: plot.top, y2: plot.bottom, visibility: 'hidden' });
    root.append(crosshair);

    // Dots on every point while there's room; always on the latest one.
    const showAll = points.length <= MAX_MARKERS;
    geometry.forEach((g, i) => {
      if (showAll || i === geometry.length - 1) root.append(svg('circle', { class: 'chart-dot', cx: g.x, cy: g.y, r: 4 }));
    });
    const focusDot = svg('circle', { class: 'chart-dot focus', r: 6, visibility: 'hidden' });
    root.append(focusDot);

    // Label the latest value; the rest are read from the axis, the crosshair and the session list.
    const end = geometry.at(-1);
    const endLabel = svg('text', {
      class: 'chart-label', x: Math.min(end.x, plot.right), y: Math.max(end.y - 10, 12),
      'text-anchor': points.length > 1 ? 'end' : 'middle',
    }, formatValue(last.value));
    root.append(endLabel);

    parts = { crosshair, focusDot, endLabel, width };
    container.replaceChildren(root, tip);
    select(active);
  }

  function select(index) {
    active = index;
    const shown = index >= 0;
    tip.hidden = !shown;
    parts.crosshair.setAttribute('visibility', shown ? 'visible' : 'hidden');
    parts.focusDot.setAttribute('visibility', shown ? 'visible' : 'hidden');
    parts.endLabel.setAttribute('visibility', shown ? 'hidden' : 'visible');
    if (!shown) return;

    const g = geometry[index];
    parts.crosshair.setAttribute('x1', g.x);
    parts.crosshair.setAttribute('x2', g.x);
    parts.focusDot.setAttribute('cx', g.x);
    parts.focusDot.setAttribute('cy', g.y);
    tipValue.textContent = formatValue(points[index].value);
    tipDate.textContent = formatDate(points[index].date);
    const half = tip.offsetWidth / 2;
    tip.style.left = `${Math.min(Math.max(g.x, half), parts.width - half)}px`;
  }

  function nearest(event) {
    const pointerX = event.clientX - container.getBoundingClientRect().left;
    let best = 0;
    geometry.forEach((g, i) => {
      if (Math.abs(g.x - pointerX) < Math.abs(geometry[best].x - pointerX)) best = i;
    });
    return best;
  }

  const onPointer = (event) => select(nearest(event));
  const onLeave = (event) => {
    if (event.pointerType === 'mouse') select(-1);
  };
  const onKey = (event) => {
    const moves = { ArrowLeft: active - 1, ArrowRight: active + 1, Home: 0, End: points.length - 1 };
    if (event.key === 'Escape') {
      select(-1);
    } else if (event.key in moves) {
      event.preventDefault();
      select(Math.min(Math.max(active < 0 ? points.length - 1 : moves[event.key], 0), points.length - 1));
    }
  };
  const onBlur = () => select(-1);

  let resizeTimer;
  const onResize = () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(draw, 100);
  };

  container.addEventListener('pointermove', onPointer);
  container.addEventListener('pointerdown', onPointer);
  container.addEventListener('pointerleave', onLeave);
  container.addEventListener('keydown', onKey);
  container.addEventListener('blur', onBlur);
  window.addEventListener('resize', onResize);

  draw();

  return () => {
    clearTimeout(resizeTimer);
    window.removeEventListener('resize', onResize);
  };
}
