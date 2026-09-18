// ParkFlow frontend — Day 32 (day-32/piece1).
//
// The API key this page calls the API with comes from GET /api/v1/client-config (fetched once at
// boot, see start() below) rather than being hardcoded here — that endpoint just echoes back
// whatever *this* environment's Security:ApiKey actually is, so the same shipped JS works
// unmodified against a local dev instance and a live demo, which each have their own,
// independently-generated key. This doesn't weaken the key as a control: anyone who could read it
// out of this file could equally have called that endpoint directly.
let API_KEY = null;

const RATE_PER_HOUR = 5.0;
const POLL_INTERVAL_MS = 5000; // how often the map re-syncs so another user's booking shows up here

const DEMO_USERS = {
  userA: "11111111-1111-1111-1111-111111111111",
  userB: "22222222-2222-2222-2222-222222222222",
};

const TYPE_ICON = {
  Standard: "🚗",
  Compact: "🚙",
  Accessible: "♿",
  ElectricVehicle: "⚡",
  Motorcycle: "🏍️",
};

// Arbitrary demo coordinate — not a claim about where "Downtown Garage" really is (it isn't a real
// place; the seed data's "100 Market St" is a placeholder, same idea as "123 Main St"). Deliberately
// picked somewhere with no notable landmark nearby, so the map tiles don't visually associate this
// fictional business with an actual, specific real-world site. Spots are spread a small synthetic
// distance apart around this point purely so each one is its own clickable marker on the map.
const MAP_BASE_COORD = [39.9612, -82.9988];
const MAP_SPOT_SPACING = 0.0006;

const state = {
  currentUser: "userA",
  facilityId: null,
  spots: [],
  reservations: [],
  pendingSpot: null,
  activeFloor: null,
  searchTerm: "",
};

let leafletMap = null;
let mapMarkers = [];
let pollTimer = null;

// ---- tiny API client ----------------------------------------------------

async function api(method, path, { body, auth = false } = {}) {
  const headers = {};
  if (API_KEY) headers["X-Api-Key"] = API_KEY;
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (auth) headers["Authorization"] = `Bearer ${await getToken()}`;

  const response = await fetch(path, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (!response.ok) {
    let message = `${method} ${path} failed (${response.status})`;
    try {
      const problem = await response.json();
      if (problem?.error) message = problem.error;
      else if (problem?.title) message = problem.title;
    } catch { /* body wasn't JSON — keep the generic message */ }
    throw new Error(message);
  }

  if (response.status === 204) return null;
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

async function getToken() {
  const cacheKey = `pf.token.${state.currentUser}`;
  const cached = sessionStorage.getItem(cacheKey);
  if (cached) {
    const { token, expiresAt } = JSON.parse(cached);
    if (new Date(expiresAt).getTime() - Date.now() > 30_000) return token;
  }

  const minted = await api("POST", "/api/v1/dev/token", { body: { user: state.currentUser } });
  sessionStorage.setItem(cacheKey, JSON.stringify(minted));
  return minted.token;
}

function vehicleKey() { return `pf.vehicle.${state.currentUser}`; }
function getStoredVehicle() {
  const raw = sessionStorage.getItem(vehicleKey());
  return raw ? JSON.parse(raw) : null;
}

// ---- toast ----------------------------------------------------------------

let toastTimer;
function toast(message, isError = false) {
  const el = document.getElementById("toast");
  el.textContent = message;
  el.classList.toggle("error", isError);
  el.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove("show"), 3200);
}

async function guarded(action, successMessage) {
  try {
    await action();
    if (successMessage) toast(successMessage);
  } catch (err) {
    toast(err.message, true);
  }
}

// ============================================================================
// AUTH: login, then (first time only) vehicle registration, then the app.
// ============================================================================

const authScreen = document.getElementById("auth-screen");
const appShell = document.getElementById("app-shell");
const stepLogin = document.getElementById("auth-step-login");
const stepVehicle = document.getElementById("auth-step-vehicle");
const stepDot1 = document.getElementById("auth-step-indicator-1");
const stepDot2 = document.getElementById("auth-step-indicator-2");

function showLoginStep() {
  stepVehicle.hidden = true;
  stepLogin.hidden = false;
  stepDot1.classList.remove("done");
  stepDot1.classList.add("active");
  stepDot2.classList.remove("active", "done");
}

function showVehicleStep() {
  stepLogin.hidden = true;
  stepVehicle.hidden = false;
  stepDot1.classList.remove("active");
  stepDot1.classList.add("done");
  stepDot2.classList.add("active");
}

document.getElementById("auth-login-btn").addEventListener("click", () => guarded(async () => {
  state.currentUser = document.getElementById("auth-user-select").value;
  if (getStoredVehicle()) {
    await enterApp();
  } else {
    showVehicleStep();
  }
}));

document.getElementById("auth-back-btn").addEventListener("click", showLoginStep);

document.getElementById("register-vehicle-btn").addEventListener("click", () => guarded(async () => {
  const plate = document.getElementById("plate-input").value.trim() || "DEMO-123";
  const vehicleType = Number(document.getElementById("vehicle-type-select").value);
  const result = await api("POST", "/api/v1/vehicles", {
    body: { ownerUserId: DEMO_USERS[state.currentUser], licensePlate: plate, vehicleType },
  });
  sessionStorage.setItem(vehicleKey(), JSON.stringify({ id: result.vehicleId, plate }));
  await enterApp();
}, "Vehicle registered."));

document.getElementById("logout-btn").addEventListener("click", () => {
  stopPolling();
  appShell.hidden = true;
  authScreen.hidden = false;
  showLoginStep();
});

async function enterApp() {
  authScreen.hidden = true;
  appShell.hidden = false;

  const vehicle = getStoredVehicle();
  document.getElementById("whoami-name").textContent = state.currentUser;
  document.getElementById("whoami-plate").textContent = vehicle ? vehicle.plate : "";

  await guarded(loadFacilityAndSpots);
  await guarded(loadMyReservations);
  startPolling();
}

function startPolling() {
  stopPolling();
  pollTimer = setInterval(() => guarded(loadFacilityAndSpots), POLL_INTERVAL_MS);
}
function stopPolling() {
  if (pollTimer) clearInterval(pollTimer);
  pollTimer = null;
}

// ============================================================================
// TABS
// ============================================================================

const TABS = ["book", "reservations"];

function switchTab(name) {
  for (const t of TABS) {
    const isActive = t === name;
    document.getElementById(`tab-${t}`).hidden = !isActive;
    const btn = document.getElementById(`tab-btn-${t}`);
    btn.classList.toggle("active", isActive);
    btn.setAttribute("aria-selected", String(isActive));
  }
  // Leaflet needs a nudge after being unhidden, or it renders at a stale/zero size.
  if (name === "book" && leafletMap) setTimeout(() => leafletMap.invalidateSize(), 0);
}

document.getElementById("tab-btn-book").addEventListener("click", () => switchTab("book"));
document.getElementById("tab-btn-reservations").addEventListener("click", () => switchTab("reservations"));

// ============================================================================
// MAP: real, interactive (Leaflet + OpenStreetMap tiles) — pan, zoom, click a marker to book.
// ============================================================================

async function loadFacilityAndSpots() {
  const facilities = await api("GET", "/api/v1/parking/facilities");
  if (facilities.length === 0) return;
  state.facilityId = facilities[0].id;
  state.spots = await api("GET", `/api/v1/parking/facilities/${state.facilityId}/spots`);
  if (state.activeFloor === null) {
    state.activeFloor = Math.min(...state.spots.map(s => s.floorLevel));
  }
  renderFloorTabs();
  renderSpotMap();
}

function renderFloorTabs() {
  const floors = [...new Set(state.spots.map(s => s.floorLevel))].sort((a, b) => a - b);
  const container = document.getElementById("floor-tabs");
  container.innerHTML = "";
  for (const floor of floors) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = `floor-tab-btn${floor === state.activeFloor ? " active" : ""}`;
    btn.textContent = `Floor ${floor}`;
    btn.addEventListener("click", () => { state.activeFloor = floor; renderFloorTabs(); renderSpotMap(); });
    container.appendChild(btn);
  }
}

function spotMatchesSearch(spot) {
  if (!state.searchTerm) return true;
  const term = state.searchTerm.toLowerCase();
  return spot.spotNumber.toLowerCase().includes(term) || spot.spotType.toLowerCase().includes(term);
}

function ensureMap() {
  if (leafletMap) return;
  leafletMap = L.map("leaflet-map", { scrollWheelZoom: true }).setView(MAP_BASE_COORD, 19);
  L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    maxZoom: 20,
  }).addTo(leafletMap);
}

function spotCoord(indexOnFloor, countOnFloor) {
  const offset = (indexOnFloor - (countOnFloor - 1) / 2) * MAP_SPOT_SPACING;
  return [MAP_BASE_COORD[0], MAP_BASE_COORD[1] + offset];
}

function renderSpotMap() {
  ensureMap();

  // If searching and nothing on the active floor matches but something elsewhere does, jump there.
  if (state.searchTerm) {
    const activeHasMatch = state.spots.some(s => s.floorLevel === state.activeFloor && spotMatchesSearch(s));
    if (!activeHasMatch) {
      const elsewhere = state.spots.find(spotMatchesSearch);
      if (elsewhere) {
        state.activeFloor = elsewhere.floorLevel;
        renderFloorTabs();
      }
    }
  }

  for (const { marker } of mapMarkers) leafletMap.removeLayer(marker);
  mapMarkers = [];

  const onFloor = state.spots.filter(s => s.floorLevel === state.activeFloor);
  onFloor.forEach((spot, i) => {
    const matches = spotMatchesSearch(spot);
    const classes = ["map-marker", spot.status.toLowerCase()];
    if (state.searchTerm && !matches) classes.push("dimmed");
    if (state.searchTerm && matches) classes.push("match");

    const icon = L.divIcon({
      className: "",
      html: `<div class="${classes.join(" ")}">${spot.spotNumber}</div>`,
      iconSize: [40, 40],
      iconAnchor: [20, 20],
      popupAnchor: [0, -20],
    });

    const marker = L.marker(spotCoord(i, onFloor.length), { icon }).addTo(leafletMap);
    marker.bindPopup(`<div class="map-popup"><b>${spot.spotNumber}</b> &middot; ${spot.spotType}<br>${spot.status}</div>`);
    if (spot.status === "Available") {
      marker.on("click", () => openBookingForm(spot));
    }
    mapMarkers.push({ marker, spot });
  });

  if (mapMarkers.length === 1) {
    leafletMap.setView(mapMarkers[0].marker.getLatLng(), 19);
  } else if (mapMarkers.length > 1) {
    const group = L.featureGroup(mapMarkers.map(m => m.marker));
    leafletMap.fitBounds(group.getBounds().pad(0.6), { maxZoom: 19 });
  }
}

document.getElementById("spot-search").addEventListener("input", (e) => {
  state.searchTerm = e.target.value.trim();
  renderSpotMap();
});

// ============================================================================
// BOOKING
// ============================================================================

function defaultDateTimeLocal(hoursFromNow) {
  const d = new Date(Date.now() + hoursFromNow * 3600_000);
  d.setSeconds(0, 0);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function openBookingForm(spot) {
  state.pendingSpot = spot;
  document.getElementById("booking-spot-label").textContent = spot.spotNumber;
  document.getElementById("start-input").value = defaultDateTimeLocal(1);
  document.getElementById("end-input").value = defaultDateTimeLocal(3);
  document.getElementById("booking-form").hidden = false;
  updatePricePreview();
  document.getElementById("booking-form").scrollIntoView({ behavior: "smooth", block: "nearest" });
}

function closeBookingForm() {
  state.pendingSpot = null;
  document.getElementById("booking-form").hidden = true;
}

function currentBookingPrice() {
  const start = new Date(document.getElementById("start-input").value);
  const end = new Date(document.getElementById("end-input").value);
  const hours = Math.max(0, (end - start) / 3600_000);
  return Math.round(hours * RATE_PER_HOUR * 100) / 100;
}

function updatePricePreview() {
  document.getElementById("price-preview-amount").textContent = `$${currentBookingPrice().toFixed(2)}`;
}
document.getElementById("start-input").addEventListener("input", updatePricePreview);
document.getElementById("end-input").addEventListener("input", updatePricePreview);
document.getElementById("cancel-booking-form-btn").addEventListener("click", closeBookingForm);

document.getElementById("confirm-booking-btn").addEventListener("click", () => guarded(async () => {
  const vehicle = getStoredVehicle();
  if (!vehicle) throw new Error("No vehicle on file — log out and register one.");
  const spot = state.pendingSpot;
  if (!spot) return;

  const start = new Date(document.getElementById("start-input").value);
  const end = new Date(document.getElementById("end-input").value);
  if (!(end > start)) throw new Error("End time must be after start time.");

  // Re-check right before submitting: another user's poll-refresh only runs every few seconds,
  // so this closes most of the gap between "looked available" and "actually still is" — the
  // backend's own overlap check (ReservationApplicationService.CreateAsync) is what actually
  // guarantees no double-booking; this just avoids a doomed request and a confusing error.
  const fresh = await api("GET", `/api/v1/parking/facilities/${state.facilityId}/spots`);
  state.spots = fresh;
  const stillAvailable = fresh.find(s => s.id === spot.id)?.status === "Available";
  if (!stillAvailable) {
    renderSpotMap();
    throw new Error("That spot was just taken — pick another one.");
  }

  const created = await api("POST", "/api/v1/reservations", {
    body: {
      userId: DEMO_USERS[state.currentUser],
      vehicleId: vehicle.id,
      parkingSpotId: spot.id,
      startTime: start.toISOString(),
      endTime: end.toISOString(),
      price: currentBookingPrice() || 0.01,
      idempotencyKey: crypto.randomUUID(),
    },
  });

  // Two explicit calls, not one atomic operation — this app has no message broker yet (see
  // ParkingSpotApplicationService's own doc comment); the frontend is standing in for it today.
  // If this second call fails after Create already succeeded, the map's next poll (or Cancel,
  // which also releases) will reconcile the spot's visible status.
  await api("POST", `/api/v1/parking/spots/${spot.id}/reserve`);

  closeBookingForm();
  await Promise.all([loadFacilityAndSpots(), loadMyReservations()]);
  switchTab("reservations");
}, "Booked! Confirm it to move it along."));

// ============================================================================
// MY RESERVATIONS
// ============================================================================

const STATUS_CLASS = {
  Pending: "pending", Confirmed: "confirmed", CheckedIn: "checkedin",
  Completed: "completed", Cancelled: "cancelled", Expired: "expired", NoShow: "noshow",
};

function spotLabel(spotId) {
  const spot = state.spots.find(s => s.id === spotId);
  return spot ? spot.spotNumber : spotId.slice(0, 8);
}

function fmtRange(startIso, endIso) {
  const opts = { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" };
  return `${new Date(startIso).toLocaleString(undefined, opts)} → ${new Date(endIso).toLocaleString(undefined, opts)}`;
}

async function loadMyReservations() {
  state.reservations = await api("GET", "/api/v1/reservations/mine", { auth: true });
  renderReservations();
}

function renderReservations() {
  const list = document.getElementById("res-list");
  list.innerHTML = "";

  if (state.reservations.length === 0) {
    list.innerHTML = '<p class="empty">No reservations yet — switch to "Book a spot" to make one.</p>';
  }

  let active = 0, completed = 0, total = 0;

  for (const r of state.reservations) {
    if (["Pending", "Confirmed", "CheckedIn"].includes(r.status)) active++;
    if (r.status === "Completed") { completed++; total += r.price; }

    const item = document.createElement("div");
    item.className = "res-item";
    item.innerHTML = `
      <div class="meta">
        <span class="spot-name">Spot ${spotLabel(r.parkingSpotId)}</span>
        <span class="when">${fmtRange(r.startTime, r.endTime)}</span>
      </div>
      <span class="status-badge ${STATUS_CLASS[r.status] || ""}">${r.status}</span>
      <span class="price">$${r.price.toFixed(2)}</span>
      <div class="res-actions" data-id="${r.id}" data-spot="${r.parkingSpotId}" data-status="${r.status}"></div>
    `;
    renderActionsFor(item.querySelector(".res-actions"), r);
    list.appendChild(item);
  }

  document.getElementById("stat-active").textContent = active;
  document.getElementById("stat-completed").textContent = completed;
  document.getElementById("stat-total").textContent = `$${total.toFixed(2)}`;

  const badge = document.getElementById("tab-badge-active");
  badge.textContent = active;
  badge.hidden = active === 0;
}

function actionButton(label, cls, handler) {
  const btn = document.createElement("button");
  btn.textContent = label;
  btn.className = cls;
  btn.type = "button";
  btn.addEventListener("click", handler);
  return btn;
}

function renderActionsFor(container, reservation) {
  const { id, parkingSpotId, status } = reservation;

  if (status === "Pending") {
    container.appendChild(actionButton("Confirm", "accent", () => guarded(async () => {
      await api("POST", `/api/v1/reservations/${id}/confirm`, { auth: true });
      await loadMyReservations();
    }, "Confirmed.")));
    container.appendChild(actionButton("Cancel", "danger", () => guarded(async () => {
      await api("POST", `/api/v1/reservations/${id}/cancel`, { auth: true });
      await api("POST", `/api/v1/parking/spots/${parkingSpotId}/release`);
      await Promise.all([loadFacilityAndSpots(), loadMyReservations()]);
    }, "Cancelled — spot released.")));
  } else if (status === "Confirmed") {
    container.appendChild(actionButton("Check in", "accent", () => guarded(async () => {
      await api("POST", `/api/v1/reservations/${id}/check-in`, { auth: true });
      await api("POST", `/api/v1/parking/spots/${parkingSpotId}/occupy`);
      await Promise.all([loadFacilityAndSpots(), loadMyReservations()]);
    }, "Checked in.")));
    container.appendChild(actionButton("Cancel", "danger", () => guarded(async () => {
      await api("POST", `/api/v1/reservations/${id}/cancel`, { auth: true });
      await api("POST", `/api/v1/parking/spots/${parkingSpotId}/release`);
      await Promise.all([loadFacilityAndSpots(), loadMyReservations()]);
    }, "Cancelled — spot released.")));
  } else if (status === "CheckedIn") {
    container.appendChild(actionButton("Complete & release", "accent", () => guarded(async () => {
      await api("POST", `/api/v1/reservations/${id}/complete`, { auth: true });
      await api("POST", `/api/v1/parking/spots/${parkingSpotId}/release`);
      await Promise.all([loadFacilityAndSpots(), loadMyReservations()]);
    }, "Trip complete — charge finalized, spot released.")));
  }
  // Completed/Cancelled/Expired/NoShow: no actions, just the record.
}

// ============================================================================
// BOOT — just fetches the API key; the app stays on the auth screen until login.
// ============================================================================

(async function start() {
  const config = await api("GET", "/api/v1/client-config");
  API_KEY = config.apiKey;
})();
