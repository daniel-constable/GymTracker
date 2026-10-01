// Thin wrapper around fetch for the JSON API. Throws ApiError with a user-readable message.

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

async function request(method, url, body) {
  let res;
  try {
    res = await fetch(url, {
      method,
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError("Can't reach the server. Check your connection and try again.", 0);
  }

  if (res.status === 204) return null;
  const data = await res.json().catch(() => null);
  if (!res.ok) {
    const message = data?.errors
      ? Object.values(data.errors).flat().join(' ')
      : data?.detail ?? (res.status === 404 ? 'That item no longer exists.' : `Something went wrong (${res.status}).`);
    throw new ApiError(message, res.status);
  }
  return data;
}

export const api = {
  config: () => request('GET', '/api/config'),

  sessionTypes: () => request('GET', '/api/session-types'),
  addSessionType: (name) => request('POST', '/api/session-types', { name }),
  renameSessionType: (id, name) => request('PUT', `/api/session-types/${id}`, { name }),
  deleteSessionType: (id) => request('DELETE', `/api/session-types/${id}`),

  exercises: () => request('GET', '/api/exercises'),
  addExercise: (exercise) => request('POST', '/api/exercises', exercise),
  updateExercise: (id, exercise) => request('PUT', `/api/exercises/${id}`, exercise),
  deleteExercise: (id) => request('DELETE', `/api/exercises/${id}`),

  progressList: () => request('GET', '/api/progress'),
  progress: (exerciseId) => request('GET', `/api/progress/${exerciseId}`),

  workouts: (skip, take) => request('GET', `/api/workouts?skip=${skip}&take=${take}`),
  activeWorkout: () => request('GET', '/api/workouts/active'),
  workout: (id) => request('GET', `/api/workouts/${id}`),
  startWorkout: (sessionTypeId) => request('POST', '/api/workouts', { sessionTypeId }),
  suggestedExercises: (workoutId) => request('GET', `/api/workouts/${workoutId}/suggested-exercises`),
  finishWorkout: (id) => request('POST', `/api/workouts/${id}/finish`),
  deleteWorkout: (id) => request('DELETE', `/api/workouts/${id}`),

  addWorkoutExercise: (workoutId, exerciseId) => request('POST', `/api/workouts/${workoutId}/exercises`, { exerciseId }),
  removeWorkoutExercise: (id) => request('DELETE', `/api/workout-exercises/${id}`),
  addSet: (workoutExerciseId, set) => request('POST', `/api/workout-exercises/${workoutExerciseId}/sets`, set),
  updateSet: (id, set) => request('PUT', `/api/sets/${id}`, set),
  deleteSet: (id) => request('DELETE', `/api/sets/${id}`),
};
