"use strict";
const $ = (id) => document.getElementById(id);
const state = { key: "", monitors: [], selected: null, editing: null, deleting: null, loading: false, detailsVersion: 0 };
const labels = { up: "Доступен", down: "Недоступен", stale: "Данные устарели", unknown: "Нет проверок" };
const date = (value) => value ? new Date(value).toLocaleString("ru-RU", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "—";
function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined && text !== null) node.textContent = text;
  if (className) node.className = className;
  return node;
}
function notice(message, success = false) {
  $("notice").textContent = message;
  $("notice").className = success ? "notice success" : "notice";
  $("notice").hidden = !message;
}
function formError(id, error) { $(id).textContent = error?.message || ""; $(id).hidden = !error; }
async function api(path, options = {}) {
  const response = await fetch(`/api${path}`, {
    ...options,
    headers: { ...(options.body ? { "Content-Type": "application/json" } : {}), ...(state.key ? { "X-Api-Key": state.key } : {}) },
    signal: AbortSignal.timeout(70000)
  });
  if (!response.ok) {
    let problem = {};
    try { problem = await response.json(); } catch { /* A reverse proxy may return plain text. */ }
    if (response.status === 401 && !$("access-dialog").open) $("access-dialog").showModal();
    const validation = problem.errors ? Object.values(problem.errors).flat().join(" ") : "";
    throw new Error(validation || problem.detail || problem.title || `Ошибка сервера (${response.status})`);
  }
  return response.status === 204 ? null : response.json();
}
function badge(status) {
  const node = element("span", undefined, `badge ${status}`);
  node.append(element("i", undefined, `dot ${status}`), document.createTextNode(labels[status]));
  return node;
}
function button(text, className, action, title) {
  const node = element("button", text, className);
  node.type = "button";
  if (title) { node.title = title; node.setAttribute("aria-label", title); }
  node.addEventListener("click", action);
  return node;
}
function render() {
  $("total").textContent = state.monitors.length;
  for (const status of ["up", "down"]) $(status).textContent = state.monitors.filter(m => m.status === status).length;
  $("unknown").textContent = state.monitors.filter(m => ["unknown", "stale"].includes(m.status)).length;
  const search = $("search").value.trim().toLowerCase();
  const filter = $("status-filter").value;
  const visible = state.monitors.filter(m => (filter === "all" || m.status === filter) && `${m.name} ${m.url}`.toLowerCase().includes(search));
  $("count").textContent = visible.length;
  $("monitors").replaceChildren();
  for (const monitor of visible) {
    const row = element("tr");
    const name = element("td");
    name.append(button(monitor.name, "site-name", () => showDetails(monitor.id), `История: ${monitor.name}`));
    const url = element("span", monitor.url, "site-url");
    url.title = monitor.url;
    name.append(url);
    const status = element("td"); status.append(badge(monitor.status));
    const response = element("td", monitor.lastCheck ? `${monitor.lastCheck.responseTimeMs} мс` : "—", "numeric");
    const uptime = element("td", monitor.uptimePercent24Hours === null ? "—" : `${monitor.uptimePercent24Hours.toLocaleString("ru-RU")}%`, "numeric");
    if (monitor.uptimePercent24Hours === 100) uptime.classList.add("uptime");
    uptime.title = `Доля успешных проверок из ${monitor.checksLast24Hours} за последние 24 часа`;
    const last = element("td", date(monitor.lastCheck?.checkedAtUtc), "check-time");
    status.dataset.label = "Статус";
    response.dataset.label = "Время ответа";
    uptime.dataset.label = "Успешных за 24 ч";
    last.dataset.label = "Последняя проверка";
    const actions = element("td");
    const group = element("div", undefined, "row-actions");
    group.append(button("↻", "secondary", e => runCheck(monitor.id, e.currentTarget), `Проверить ${monitor.name} сейчас`),
      button("Изменить", "quiet", () => openEditor(monitor)),
      button("×", "quiet", () => openDelete(monitor), `Удалить ${monitor.name}`));
    actions.append(group); row.append(name, status, response, uptime, last, actions); $("monitors").append(row);
  }
  $("empty").hidden = visible.length > 0;
  $("empty-title").textContent = state.monitors.length ? "Ничего не найдено" : "Добавьте первый сайт";
  $("empty-text").textContent = state.monitors.length ? "Попробуйте изменить запрос или фильтр статуса." : "Укажите адрес — сервер начнёт проверять его автоматически.";
  $("empty-add").hidden = state.monitors.length > 0;
}
async function refresh() {
  if (state.loading) return;
  state.loading = true;
  $("refresh-button").disabled = true;
  try {
    const overview = await api("/monitors/overview");
    state.monitors = overview.monitors;
    render();
    $("updated").textContent = `Обновлено ${date(overview.generatedAtUtc)} · история хранится ${overview.retentionDays} дн.`;
    $("loading").hidden = true;
    if (state.selected) {
      if (state.monitors.some(m => m.id === state.selected)) await showDetails(state.selected, false);
      else closeDetails();
    }
    return true;
  } catch (error) {
    state.monitors = state.monitors.map(m => ({ ...m, status: m.lastCheck ? "stale" : "unknown" }));
    render();
    $("updated").textContent = "Нет связи с сервером — показанные данные могут быть устаревшими";
    $("loading").hidden = true;
    notice(error.message);
    return false;
  } finally { state.loading = false; $("refresh-button").disabled = false; }
}
function openEditor(monitor = null) {
  state.editing = monitor?.id ?? null;
  const form = $("monitor-form"); form.reset();
  form.elements.name.value = monitor?.name ?? "";
  form.elements.url.value = monitor?.url ?? "";
  form.elements.url.readOnly = !!monitor?.lastCheck;
  form.elements.checkIntervalSeconds.value = monitor?.checkIntervalSeconds ?? 60;
  $("form-title").textContent = monitor ? "Изменить монитор" : "Добавить сайт";
  $("url-help").textContent = monitor?.lastCheck ? "У этого адреса уже есть история. Для другого URL создайте новый монитор." : "Публичный HTTP/HTTPS адрес, стандартный порт 80 или 443.";
  formError("form-error", null); $("monitor-dialog").showModal();
}
$("monitor-form").addEventListener("submit", async event => {
  event.preventDefault(); const form = event.currentTarget; const submit = form.querySelector('[type="submit"]'); submit.disabled = true;
  formError("form-error", null);
  try {
    const body = JSON.stringify({ name: form.elements.name.value.trim(), url: form.elements.url.value.trim(), checkIntervalSeconds: Number(form.elements.checkIntervalSeconds.value) });
    await api(state.editing ? `/monitors/${state.editing}` : "/monitors", { method: state.editing ? "PUT" : "POST", body });
    $("monitor-dialog").close(); notice(state.editing ? "Монитор обновлён." : "Сайт добавлен. Первая проверка появится автоматически.", true); await refresh();
  } catch (error) { formError("form-error", error); } finally { submit.disabled = false; }
});
async function runCheck(id, target) {
  target.disabled = true;
  try { await api(`/monitors/${id}/checks`, { method: "POST" }); notice("Проверка выполнена. Результат обновлён.", true); await refresh(); }
  catch (error) { notice(error.message); } finally { target.disabled = false; }
}
function openDelete(monitor) {
  state.deleting = monitor.id; $("delete-description").textContent = `${monitor.name} · ${monitor.url}`;
  formError("delete-error", null); $("delete-dialog").showModal();
}
$("delete-form").addEventListener("submit", async event => {
  event.preventDefault(); const submit = event.currentTarget.querySelector('[type="submit"]'); submit.disabled = true;
  try {
    await api(`/monitors/${state.deleting}`, { method: "DELETE" });
    if (state.selected === state.deleting) closeDetails();
    $("delete-dialog").close(); notice("Монитор удалён.", true); await refresh();
  } catch (error) { formError("delete-error", error); } finally { submit.disabled = false; }
});
function closeDetails() { state.selected = null; state.detailsVersion++; $("details").hidden = true; }
async function showDetails(id, scroll = true) {
  const version = ++state.detailsVersion;
  state.selected = id;
  const monitor = state.monitors.find(m => m.id === id);
  if (!monitor) return;
  try {
    const [checks, incidents] = await Promise.all([api(`/monitors/${id}/checks`), api(`/monitors/${id}/incidents`)]);
    if (version !== state.detailsVersion) return;
    $("detail-title").textContent = monitor.name;
    $("detail-summary").textContent = `${monitor.url} · интервал ${monitor.checkIntervalSeconds} с · проверок за 24 часа: ${monitor.checksLast24Hours}. Процент доступности — доля успешных проверок, а не точное время работы.`;
    $("checks").replaceChildren(); $("history-bars").replaceChildren(); $("incidents").replaceChildren();
    for (const check of [...checks].reverse()) {
      const bar = element("span", undefined, check.isSuccessful ? "" : "failed");
      bar.title = `${date(check.checkedAtUtc)} · ${check.isSuccessful ? "Доступен" : check.errorMessage || "Ошибка"} · ${check.responseTimeMs} мс`;
      bar.setAttribute("aria-label", bar.title); $("history-bars").append(bar);
    }
    for (const check of checks) {
      const row = element("tr"); const status = element("td", check.statusCode ? `HTTP ${check.statusCode}` : "Ошибка сети");
      status.title = check.errorMessage || "Проверка успешна";
      row.append(element("td", date(check.checkedAtUtc)), status, element("td", `${check.responseTimeMs} мс`)); $("checks").append(row);
    }
    if (!checks.length) { const row = element("tr"); const cell = element("td", "Проверок пока нет."); cell.colSpan = 3; row.append(cell); $("checks").append(row); }
    for (const incident of incidents) {
      const card = element("article", undefined, "incident");
      card.append(element("span", incident.isOpen ? "Открыт" : "Завершён", `badge ${incident.isOpen ? "down" : "up"}`), element("p", incident.errorMessage || "Проверка не прошла"), element("div", `Начало: ${date(incident.startedAtUtc)}`), element("div", `Восстановление: ${date(incident.resolvedAtUtc)}`));
      $("incidents").append(card);
    }
    if (!incidents.length) $("incidents").append(element("p", "Инцидентов за доступный период нет.", "no-history"));
    $("details").hidden = false;
    if (scroll) $("details").scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) { if (version === state.detailsVersion) notice(error.message); }
}
$("access-form").addEventListener("submit", async event => {
  event.preventDefault(); const submit = event.currentTarget.querySelector('[type="submit"]'); submit.disabled = true;
  state.key = $("api-key").value;
  try {
    await api("/session"); $("api-key").value = ""; formError("access-error", null); $("access-dialog").close(); notice(""); await refresh();
  } catch (error) { state.key = ""; formError("access-error", error); } finally { submit.disabled = false; }
});
$("logout").addEventListener("click", () => {
  state.key = ""; $("api-key").value = ""; state.monitors = []; render(); closeDetails(); notice("Ключ удалён из памяти вкладки."); $("updated").textContent = "Нет подключения";
});
$("access-button").addEventListener("click", () => { formError("access-error", null); $("access-dialog").showModal(); });
for (const id of ["add-button", "empty-add"]) $(id).addEventListener("click", () => openEditor());
$("refresh-button").addEventListener("click", async () => { notice(""); await refresh(); });
$("close-details").addEventListener("click", closeDetails);
$("search").addEventListener("input", render); $("status-filter").addEventListener("change", render);
for (const closer of document.querySelectorAll("[data-close]")) closer.addEventListener("click", () => $(closer.dataset.close).close());
setInterval(() => { if (!document.hidden && !document.querySelector("dialog[open]")) refresh(); }, 15000);
refresh();
