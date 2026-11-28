"use strict";

// Published StatusPopup.xlsx values; the card height is BattleField1's first allied art rect.
// Geometry/alpha/motion follow BattlefieldStatusPopupStyle.CenterAbove and BattlefieldPopupStyle.
const UNITY_REFERENCE = Object.freeze({ cardHeight: 338, anchorY: 1, offsetX: 0, offsetY: 0,
  cardGap: 3, stackGap: 1.5, riseRatio: 12, lifetime: .9, fadeIn: .12, fadeOut: .22, maxVisible: 64 });
const DEFAULTS = Object.freeze({ ...UNITY_REFERENCE, maxWidth: 360, fontSize: 25, lineHeight: 1.36,
  paddingX: 42, paddingY: 11, backgroundOpacity: 20, edgeFade: 20, goldWidth: 1.5, goldOpacity: 88,
  textColor: "#fefefe", backgroundColor: "#30251c", goldColor: "#ddbf85", spawnInterval: .18, loopDelay: .65,
  guides: true, backgroundOnly: false });
const GROUPS = {
  position: [
    ["cardHeight", "卡牌高度", 200, 600, 1, "px"],
    ["offsetX", "水平偏移 X", -300, 300, 1, "px"],
    ["offsetY", "垂直偏移 Y", -200, 200, 1, "px，向上为正"],
    ["cardGap", "距锚点间距", 0, 20, .1, "卡高 %"]
  ],
  motion: [
    ["lifetime", "显示总时长", .1, 5, .01, "秒"],
    ["fadeIn", "淡入时长", 0, 2, .01, "秒"],
    ["fadeOut", "淡出时长", 0, 2, .01, "秒"],
    ["riseRatio", "匀速上浮距离", 0, 40, .1, "卡高 %"],
    ["stackGap", "连续叠放间距", 0, 10, .1, "卡高 %"],
    ["maxVisible", "最大同时显示", 1, 64, 1, "条"],
    ["spawnInterval", "连续弹出间隔", .02, 1, .01, "秒"],
    ["loopDelay", "循环间隔", 0, 3, .05, "秒"]
  ],
  text: [
    ["fontSize", "字号", 12, 60, .5, "px"],
    ["lineHeight", "行高", 1, 2, .01, "倍"],
    ["maxWidth", "最大宽度", 160, 640, 1, "px"],
    ["paddingX", "左右留白", 0, 80, 1, "px"],
    ["paddingY", "上下留白", 0, 40, 1, "px"]
  ],
  background: [
    ["backgroundOpacity", "底板不透明度", 0, 100, 1, "%"],
    ["edgeFade", "每端渐隐长度", 0, 45, 1, "宽度 %"],
    ["goldWidth", "上下金边厚度", 0, 5, .25, "px"],
    ["goldOpacity", "金边不透明度", 0, 100, 1, "%"]
  ]
};
const SPECS = Object.values(GROUPS).flat();
const STORAGE = "genshin.statusNamePopup.preview.v2";
const $ = selector => document.querySelector(selector);
const stage = $("#stage"), editor = $("#status-text"), mode = $("#mode");
const layer = $("#popup-layer"), template = $("#popup-template");
let settings = { ...DEFAULTS }, playback = 0, timers = new Set(), loopTimer = null, lastPlay = "still";
const clamp = (number, min, max) => Math.max(min, Math.min(max, number));
const round = number => Math.round(number * 10000) / 10000;

function normalize(values, edited) {
  const next = { ...DEFAULTS, ...values };
  for (const [key, , min, max] of SPECS) {
    const value = Number(next[key]); next[key] = Number.isFinite(value) ? clamp(value, min, max) : DEFAULTS[key];
  }
  next.maxVisible = Math.round(next.maxVisible);
  next.anchorY = [0, .5, 1].includes(Number(next.anchorY)) ? Number(next.anchorY) : 1;
  for (const key of ["textColor", "backgroundColor", "goldColor"])
    if (!/^#[0-9a-f]{6}$/i.test(next[key])) next[key] = DEFAULTS[key];
  next.fadeIn = Math.min(next.fadeIn, next.lifetime); next.fadeOut = Math.min(next.fadeOut, next.lifetime);
  if (next.fadeIn + next.fadeOut > next.lifetime) {
    if (edited === "fadeOut") next.fadeIn = Math.max(0, next.lifetime - next.fadeOut);
    else next.fadeOut = Math.max(0, next.lifetime - next.fadeIn);
  }
  next.paddingX = Math.min(next.paddingX, Math.floor((next.maxWidth - next.fontSize) / 2));
  return next;
}

const PopupMath = {
  alpha(age, p) {
    if (age >= p.lifetime) return 0;
    const enter = p.fadeIn > 0 ? clamp(age / p.fadeIn, 0, 1) : 1;
    return enter * (p.fadeOut > 0 ? clamp((p.lifetime - age) / p.fadeOut, 0, 1) : 1);
  },
  rise(age, p) { return p.cardHeight * p.riseRatio / 100 * clamp(age / p.lifetime, 0, 1); },
  bottom(p) { return 589 - p.cardHeight * p.anchorY - p.cardHeight * p.cardGap / 100 - p.offsetY; }
};

function backgroundSvg(p, width = 600, height = 72) {
  const fade = p.edgeFade / 100, edge = p.goldWidth / 2;
  const stops = fade ? `<stop offset="0" stop-color="white" stop-opacity="0"/><stop offset="${fade * .4}" stop-color="white" stop-opacity=".35"/><stop offset="${fade}" stop-color="white"/><stop offset="${1 - fade}" stop-color="white"/><stop offset="${1 - fade * .4}" stop-color="white" stop-opacity=".35"/><stop offset="1" stop-color="white" stop-opacity="0"/>` : '<stop offset="0" stop-color="white"/><stop offset="1" stop-color="white"/>';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" preserveAspectRatio="none"><defs><linearGradient id="ends">${stops}</linearGradient><mask id="fade" maskUnits="objectBoundingBox" maskContentUnits="objectBoundingBox"><rect width="1" height="1" fill="url(#ends)"/></mask></defs><g mask="url(#fade)"><rect width="${width}" height="${height}" fill="${p.backgroundColor}" fill-opacity="${p.backgroundOpacity / 100}"/><path d="M0 ${edge} H${width} M0 ${height - edge} H${width}" fill="none" stroke="${p.goldColor}" stroke-opacity="${p.goldOpacity / 100}" stroke-width="${p.goldWidth}"/></g></svg>`;
}

/** Pure presentation. Unity's upward-positive Y is converted to browser downward-positive Y. */
class StatusNamePopup {
  constructor(mount) { this.mount = mount; this.active = []; }
  clear() { for (const item of [...this.active]) this.remove(item); }
  remove(item) { this.active = this.active.filter(other => other !== item); item.animation?.cancel(); item.element.remove(); }
  spawn(message, animated, p) {
    while (this.active.length >= p.maxVisible) this.remove(this.active[0]);
    const element = template.content.firstElementChild.cloneNode(true);
    element.querySelector("span").textContent = message;
    this.mount.append(element);
    let bottom = PopupMath.bottom(p);
    for (const item of this.active) {
      const age = item.animation ? Number(item.animation.currentTime || 0) / 1000 : 0;
      const top = item.bottom - item.element.offsetHeight - (item.animation ? PopupMath.rise(age, item.params) : 0);
      bottom = Math.min(bottom, top - p.cardHeight * p.stackGap / 100);
    }
    element.style.left = `${480 + p.offsetX}px`; element.style.top = `${bottom}px`;
    element.style.setProperty("--status-background", `url("data:image/svg+xml,${encodeURIComponent(backgroundSvg(p, element.offsetWidth, element.offsetHeight))}")`);
    const item = { element, bottom, params: { ...p }, animation: null };
    this.active.push(item); measure();
    if (!animated) return Promise.resolve();
    document.dispatchEvent(new Event("status-preview-applied"));
    const times = new Set([0, p.fadeIn, p.lifetime - p.fadeOut, p.lifetime]);
    if (!p.fadeOut) times.add(Math.max(0, p.lifetime - .000001));
    const frames = [...times].filter(time => time >= 0 && time <= p.lifetime).sort((a, b) => a - b).map(time => ({
      offset: time / p.lifetime, opacity: PopupMath.alpha(time, p),
      transform: `translate(-50%, -100%) translateY(${-PopupMath.rise(time, p)}px)`
    }));
    item.animation = element.animate(frames, { duration: p.lifetime * 1000, easing: "linear", fill: "forwards" });
    // Use the same sampled clock as existing labels; a pending start otherwise loses a frame of stack spacing.
    item.animation.startTime = document.timeline.currentTime;
    return item.animation.finished.then(() => this.remove(item)).catch(() => {});
  }
}
const popup = new StatusNamePopup(layer);

function fitCanvas() {
  const width = stage.clientWidth - 24, height = stage.clientHeight - 24;
  const visibleWidth = stage.clientWidth < 600 ? Math.max(440, settings.maxWidth + Math.abs(settings.offsetX) * 2 + 80) : 960;
  stage.style.setProperty("--preview-scale", Math.max(.1, Math.min(width / visibleWidth, height / 720)));
}
function measure() {
  const element = popup.active.at(-1)?.element;
  if (!element) { $("#measurements").value = ""; return; }
  const lines = Math.max(1, Math.round((element.offsetHeight - 2 * settings.paddingY) / (settings.fontSize * settings.lineHeight)));
  $("#measurements").value = `${element.offsetWidth} × ${element.offsetHeight} px · ${lines} 行 · ${popup.active.length} 条`;
}
function save() {
  try { localStorage.setItem(STORAGE, JSON.stringify({ settings, text: editor.value })); }
  catch { $("#save-note").textContent = "浏览器无法保存本机参数，可用“复制当前参数”保留。"; }
}
function apply() {
  const p = settings;
  const variables = { "card-height": `${p.cardHeight}px`, "card-width": `${p.cardHeight * 7 / 12}px`,
    "card-top": `${589 - p.cardHeight}px`, "popup-bottom": `${PopupMath.bottom(p)}px`, "popup-x": `${480 + p.offsetX}px`,
    "popup-max-width": `${p.maxWidth}px`, "status-font-size": `${p.fontSize}px`, "status-line-height": p.lineHeight,
    "padding-x": `${p.paddingX}px`, "padding-y": `${p.paddingY}px`, "text-color": p.textColor };
  for (const [key, value] of Object.entries(variables)) stage.style.setProperty(`--${key}`, value);
  stage.classList.toggle("background-only", p.backgroundOnly); stage.classList.toggle("hide-guides", !p.guides);
  for (const input of document.querySelectorAll("[data-setting]")) {
    const value = p[input.dataset.setting];
    if (input.type === "checkbox") input.checked = value; else input.value = value;
  }
  $("#anchor").value = p.anchorY;
  for (const code of document.querySelectorAll("[data-color-value]")) code.textContent = p[code.dataset.colorValue].toUpperCase();
  $("#timing-summary").textContent = `淡入 ${round(p.fadeIn)}秒 → 清晰停留 ${round(p.lifetime - p.fadeIn - p.fadeOut)}秒 → 淡出 ${round(p.fadeOut)}秒。全程匀速上浮。`;
  $("#position-info").textContent = `卡高 ${round(p.cardHeight)}px · 锚点外间距 ${round(p.cardHeight * p.cardGap / 100)}px · 上浮 ${round(p.cardHeight * p.riseRatio / 100)}px · X ${p.offsetX}px / Y ${p.offsetY}px（向上为正）`;
  fitCanvas(); save(); document.dispatchEvent(new Event("status-preview-change"));
}
function stop() { playback++; for (const timer of timers) clearTimeout(timer); timers.clear(); loopTimer = null; popup.clear(); }
function later(callback, delay) { const timer = setTimeout(() => { timers.delete(timer); callback(); }, delay); timers.add(timer); return timer; }
function still() { stop(); lastPlay = "still"; mode.textContent = "静止查看"; popup.spawn(editor.value, false, settings); }
function play(count = 1) {
  stop(); lastPlay = count === 1 ? "single" : "burst";
  const token = playback; mode.textContent = count === 1 ? "播放中" : "连续弹出中";
  for (let i = 0; i < count; i++) {
    const spawn = async () => {
      if (token !== playback) return;
      await popup.spawn(editor.value, true, settings);
      if (token !== playback || i !== count - 1) return;
      mode.textContent = "播放结束"; measure();
      if ($("#loop").checked) loopTimer = later(() => play(count), settings.loopDelay * 1000);
    };
    if (i === 0) spawn(); else later(spawn, i * settings.spawnInterval * 1000);
  }
}
function update(key, value) {
  const wasPlaying = lastPlay !== "still", count = lastPlay === "burst" ? 3 : 1;
  settings = normalize({ ...settings, [key]: value }, key); apply();
  if (wasPlaying) play(count); else still();
}

for (const [group, specs] of Object.entries(GROUPS)) {
  const mount = $(`#${group}-settings`);
  for (const [key, label, min, max, step, unit] of specs) {
    const setting = document.createElement("div"); setting.className = "setting";
    const title = document.createElement("label"); title.htmlFor = `setting-${key}`; title.textContent = label;
    const suffix = document.createElement("span"); suffix.className = "unit"; suffix.textContent = unit; title.append(suffix);
    const row = document.createElement("div"); row.className = "setting-row";
    for (const type of ["range", "number"]) {
      const input = document.createElement("input"); Object.assign(input, { type, min, max, step });
      input.dataset.setting = key; input.setAttribute("aria-label", `${label}${type === "number" ? "数值" : "滑条"}`);
      if (type === "range") input.id = `setting-${key}`;
      input.addEventListener("input", () => { if (input.value !== "" && input.validity.valid) update(key, Number(input.value)); });
      input.addEventListener("change", () => { if (!input.validity.valid || input.value === "") apply(); });
      row.append(input);
    }
    setting.append(title, row); mount.append(setting);
  }
}
for (const [key, label] of [["textColor", "文字颜色"], ["backgroundColor", "底板颜色"], ["goldColor", "金边颜色"]]) {
  const row = document.createElement("label"); row.className = "color-setting"; row.textContent = label;
  const face = document.createElement("span"); face.className = "color-value";
  const code = document.createElement("span"); code.dataset.colorValue = key;
  const input = document.createElement("input"); input.type = "color"; input.dataset.setting = key;
  input.setAttribute("aria-label", label); input.addEventListener("input", () => update(key, input.value));
  face.append(code, input); row.append(face); $("#color-settings").append(row);
}
for (const [selector, key] of [["#guides", "guides"], ["#background-only", "backgroundOnly"]]) {
  const input = $(selector); input.dataset.setting = key;
  input.addEventListener("change", () => update(key, input.checked));
}
$("#anchor").addEventListener("change", event => update("anchorY", Number(event.target.value)));
editor.addEventListener("input", () => { save(); still(); });
$("#play").addEventListener("click", () => play()); $("#burst").addEventListener("click", () => play(3));
$("#still").addEventListener("click", still);
$("#loop").addEventListener("change", () => {
  if ($("#loop").checked) play(lastPlay === "burst" ? 3 : 1);
  else { clearTimeout(loopTimer); timers.delete(loopTimer); loopTimer = null; }
});
document.querySelectorAll("[data-sample]").forEach(button => button.addEventListener("click", () => {
  editor.value = button.dataset.sample.replaceAll("\\n", "\n"); save(); still();
}));
$("#unity-reset").addEventListener("click", () => { settings = normalize({ ...settings, ...UNITY_REFERENCE }); apply(); still(); });
$("#reset").addEventListener("click", () => { settings = { ...DEFAULTS }; editor.value = "攻击力提升"; $("#loop").checked = false; apply(); still(); });
$("#copy-settings").addEventListener("click", async () => {
  const p = settings;
  const text = JSON.stringify({ version: 1, preview: { ...p }, runtimeParameters: {
    Enabled: 1, FontHeightRatio: p.fontSize / p.cardHeight, LifetimeSeconds: p.lifetime,
    FadeInSeconds: p.fadeIn, FadeOutSeconds: p.fadeOut, RiseHeightRatio: p.riseRatio / 100,
    CardGapHeightRatio: p.cardGap / 100, StackGapHeightRatio: p.stackGap / 100,
    MaxVisible: p.maxVisible, Color: `${p.textColor.toUpperCase()}FF`
  } }, null, 2);
  $("#export-text").value = text; $("#export-panel").hidden = false; $("#export-panel").open = true;
  try { await navigator.clipboard.writeText(text); $("#save-note").textContent = "当前参数已复制。"; }
  catch { $("#save-note").textContent = "当前参数已展开，可选中文本复制。"; }
});
document.addEventListener("keydown", event => {
  if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) { event.preventDefault(); play(); }
  if (event.key === "Escape") { $("#loop").checked = false; still(); }
});
document.addEventListener("visibilitychange", () => { if (document.hidden) { $("#loop").checked = false; still(); } });
window.addEventListener("pagehide", stop);
new ResizeObserver(() => { fitCanvas(); measure(); }).observe(stage);
document.fonts.ready.then(() => { fitCanvas(); if (lastPlay === "still") still(); });
try {
  const saved = JSON.parse(localStorage.getItem(STORAGE));
  if (saved) { settings = normalize(saved.settings); if (typeof saved.text === "string") editor.value = saved.text; }
} catch { /* A damaged local preset must not prevent opening the preview. */ }
window.StatusPopupPreview = {
  getParameters: () => ({ ...settings }),
  validateParameters(values) {
    const normalized = normalize({ ...settings, ...values });
    for (const [key, value] of Object.entries(values))
      if (typeof value === "number" ? !Number.isFinite(value) || Math.abs(normalized[key] - value) > 1e-9 : normalized[key] !== value)
        throw new Error("参数超出有效范围或组合不合法：" + key);
    return normalized;
  },
  setParameters(values) { settings = this.validateParameters(values); apply(); still(); }
};
apply(); still();
