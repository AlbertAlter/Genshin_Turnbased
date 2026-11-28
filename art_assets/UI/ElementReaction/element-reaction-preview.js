/* Preview host only. The independent ElementAuraMotion module owns all motion.
 * presentResults accepts already-resolved records, matching the three C# result
 * payloads. It never infers a reaction from changed GU or from a character ID.
 */
(() => {
  "use strict";
  const $ = id => document.getElementById(id);
  const motion = window.ElementAuraMotion;
  const palette = window.ElementReactionPalette;
  const cases = window.ElementReactionCases;
  const cards = window.BattlePreviewScene.mount({cardsHost:$("cards"), enemyHud:$("enemy-hud"), partyHud:$("party-hud")});
  const views = new Map([...cards].map(([key, card]) => [key, new motion.View(card, palette, $("aura-layer"))]));
  const state = {ready:false, playing:false, time:0, duration:0, raf:0, last:0, loopWait:0, caseId:"vaporize", session:0};
  let tracks = new Map();

  function targets() {
    const key = $("target").value;
    return [...cards.values()].filter(card => key === "all-enemy" ? card.side === "enemy" :
      key === "all-ally" ? card.side === "ally" : card.key === key);
  }
  const caseEntry = () => cases.find(entry => entry.id === state.caseId);
  const poolNames = pools => motion.elements(pools).map(element => palette[element].name).join("、") || "无";
  function label(value) { if ($("playback-status").textContent !== value) $("playback-status").textContent = value; }
  function buttons() {
    $("pause").textContent = state.playing ? "暂停" : "继续";
    $("pause").disabled = !state.ready || state.time >= state.duration;
  }
  function stopClock() { cancelAnimationFrame(state.raf); state.raf = 0; state.playing = false; }

  // Each target has its own queue. A shared hit starts all its targets together.
  // Collisions wait for the previous collision on that target. Pure refreshes
  // use their own contact times and combine capped feedback on the same icon.
  // Event identity deduplicates repeated delivery without merging distinct hits.
  function presentResults(results) {
    stopClock();
    const pending = new Map(), seen = new Set();
    let totalDuration = 0;
    for (const record of results) {
      if (!cards.has(record.target)) throw new Error("反应结果缺少有效目标 " + record.target);
      const id = record.identity;
      if (id?.SessionId == null || id?.EventId == null) throw new Error("反应结果缺少会话或事件身份");
      const key = JSON.stringify([id.SessionId, id.EventId, record.target]);
      if (seen.has(key)) continue;
      seen.add(key);
      if (!Number.isFinite(record.at) || record.at < 0) throw new Error("反应接触时间须为非负秒数");
      let track = pending.get(record.target);
      if (!track) {
        track = {clips:[], end:0, initial:[]};
        pending.set(record.target, track);
      }
      const previous = track.clips.at(-1)?.clip;
      const clip = motion.createClip({...record, before:record.before ?? previous?.after,
        order:previous?.afterOrder ?? []});
      if (!track.clips.length) track.initial = clip.beforeOrder;
      const start = Math.max(track.end, Math.max(0, record.at - clip.impact));
      const end = start + clip.duration;
      track.clips.push({clip, start, end, requestedContact:record.at});
      if (!clip.isRefresh) track.end = end;
      totalDuration = Math.max(totalDuration, end);
    }
    tracks = pending;
    state.time = 0; state.loopWait = 0; state.duration = Math.max(0.01, totalDuration + 0.22);
    for (const card of cards.values()) {
      const selected = tracks.has(card.key);
      card.root.classList.toggle("is-target", selected);
      card.identity.dataset.role = selected ? "反应" : "";
    }
    render(); buttons(); label("准备就绪");
  }

  function buildDemo() {
    const entry = caseEntry(), records = [];
    state.session++;
    for (const card of targets()) {
      let before = entry.before;
      entry.steps.forEach((step, index) => {
        records.push({target:card.key, at:0.56 + index * (entry.interval ?? 0.35),
          identity:{SessionId:"html:" + state.session, EventId:index * 10 + card.slot,
            ActionExecutionId:1, EffectExecutionId:1, HitId:index + 1, HitTargetOrdinal:card.slot, TargetInstanceId:card.key},
          contact:{Element:step.Element, CanApplyAura:step.Element !== "Anemo" && step.Element !== "Geo", CanTriggerReaction:true},
          reactions:step.reactions,
          auraChange:{Before:before, After:step.after, Reason:step.reactions.length ? "ReactionConsumption" : "ApplyElement",
            RefreshedPools:step.after.filter(pool => !pool.IsIntrinsic).map(pool => pool.Kind + ":" + pool.Element)}});
        before = step.after;
      });
    }
    presentResults(records);
    for (const button of $("cases").querySelectorAll("button")) button.setAttribute("aria-pressed", String(button.dataset.case === entry.id));
    $("description").textContent = entry.description;
    $("reaction-result").textContent = `附着 ${poolNames(entry.before)} → ${poolNames(entry.steps.at(-1).after)}`;
  }

  function render() {
    let phase = state.time >= state.duration ? "播放完成" : "准备接触";
    for (const [key, view] of views) {
      const track = tracks.get(key);
      if (!track) { view.clear(); continue; }
      const active = track.clips.filter(item => state.time >= item.start && state.time < item.end);
      const current = active.find(item => !item.clip.isRefresh);
      let frame;
      if (current) {
        const elapsed = state.time - current.start;
        frame = motion.sample(current.clip, elapsed);
        phase = elapsed < current.clip.impact ? "元素靠拢" : elapsed < current.clip.release ?
          current.clip.reactions.length ? "反应碰撞" : "附着更新" : "残留归位";
      } else {
        const previous = track.clips.filter(item => state.time >= item.start).at(-1);
        frame = {icons:motion.still(previous?.clip.afterOrder ?? track.initial), flash:0};
      }
      for (const item of active.filter(item => item.clip.isRefresh)) {
        const refresh = motion.sample(item.clip, state.time - item.start);
        const pulse = refresh.icons.find(icon => icon.element === item.clip.contact.Element);
        const icon = frame.icons.find(icon => icon.key === pulse.key);
        if (!icon) continue;
        icon.scale = Math.max(icon.scale, pulse.scale);
        icon.glow = Math.max(icon.glow, pulse.glow);
        if (!current) phase = "附着刷新";
      }
      view.present(frame, current?.clip);
    }
    $("seek").value = Math.round(state.time / state.duration * 1000);
    $("time").textContent = `${state.time.toFixed(2)} / ${state.duration.toFixed(2)} s`;
    if (!state.playing) return;
    label(phase);
  }

  function tick(now) {
    state.raf = 0;
    if (!state.playing) return;
    const delta = Math.max(0, now - state.last) / 1000 * Number($("speed").value);
    state.last = now;
    if (state.loopWait > 0) {
      state.loopWait -= delta;
      if (state.loopWait <= 0) state.time = 0;
    } else {
      state.time = Math.min(state.duration, state.time + delta);
      render();
      if (state.time >= state.duration) {
        if ($("loop").checked) state.loopWait = 0.8;
        else { stopClock(); buttons(); label("播放完成"); return; }
      }
    }
    state.raf = requestAnimationFrame(tick);
  }
  function resume() { state.playing = true; state.last = performance.now(); state.raf = requestAnimationFrame(tick); buttons(); }
  function play() {
    if (!state.ready) return;
    stopClock(); state.time = 0; state.loopWait = 0; render(); resume();
  }
  function reset() { stopClock(); state.time = 0; state.loopWait = 0; render(); buttons(); label("已复位"); }
  function fit() {
    const box = $("viewport").getBoundingClientRect();
    document.documentElement.style.setProperty("--scale", Math.min(box.width / 1920, box.height / 1080));
  }
  function clean(enabled) {
    document.body.classList.toggle("clean", enabled);
    $("exit-clean").hidden = !enabled; fit();
  }

  for (const card of [...cards.values()].sort((a, b) => a.side.localeCompare(b.side) || a.slot - b.slot)) {
    const option = document.createElement("option"); option.value = card.key;
    option.textContent = `${card.side === "ally" ? "我方" : "敌方"} ${card.slot} · ${card.name}`;
    $("target").append(option);
  }
  for (const [value, text] of [["all-enemy", "全体敌人"], ["all-ally", "全体我方"]]) {
    const option = document.createElement("option"); option.value = value; option.textContent = text; $("target").append(option);
  }
  $("target").value = "enemy1";
  for (const entry of cases) {
    const button = document.createElement("button"); button.type = "button";
    button.dataset.case = entry.id; button.textContent = entry.name;
    button.onclick = () => { state.caseId = entry.id; buildDemo(); play(); };
    $("cases").append(button);
  }
  $("play").onclick = play; $("stop").onclick = reset;
  $("pause").onclick = () => {
    if (state.playing) { stopClock(); label("已暂停"); buttons(); }
    else if (state.time < state.duration) resume();
  };
  $("target").onchange = () => { buildDemo(); play(); };
  $("seek").oninput = () => {
    stopClock(); state.loopWait = 0; state.time = state.duration * Number($("seek").value) / 1000;
    render(); label("进度预览"); buttons();
  };
  $("clean-view").onclick = () => clean(true); $("exit-clean").onclick = () => clean(false);
  document.addEventListener("keydown", event => {
    if (event.repeat || event.ctrlKey || event.metaKey || event.altKey || /INPUT|SELECT|TEXTAREA/.test(event.target.tagName)) return;
    if (event.code === "Space") { event.preventDefault(); if (state.time >= state.duration) play(); else $("pause").click(); }
    else if (event.code === "KeyR") { event.preventDefault(); play(); }
    else if (event.code === "Escape") { event.preventDefault(); if (document.body.classList.contains("clean")) clean(false); else reset(); }
  });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden && state.playing) { stopClock(); buttons(); label("页面隐藏，已暂停"); }
  });
  window.addEventListener("pagehide", () => { stopClock(); for (const view of views.values()) view.clear(); });
  new ResizeObserver(fit).observe($("viewport"));
  window.ElementReactionPreview = Object.freeze({presentResults, play, reset,
    getSnapshot:() => ({...state, targets:[...tracks.keys()], clips:[...tracks].map(([key, track]) =>
      ({target:key, clips:track.clips.map(item => ({identity:item.clip.identity, start:item.start, end:item.end,
        before:[...item.clip.beforeOrder], after:[...item.clip.afterOrder], isRefresh:item.clip.isRefresh}))}))})});

  async function init() {
    buildDemo(); fit();
    const symbols = Object.entries(palette).filter(([element]) => element !== "Physical").map(([, style]) => {
      const image = new Image(); image.src = style.icon; return image;
    });
    const assets = [...document.images, ...symbols];
    const failed = [];
    await Promise.all(assets.map(async image => { try { await image.decode(); } catch { failed.push(image.getAttribute("src")); } }));
    if (failed.length) throw new Error("素材加载失败 " + [...new Set(failed)].join("、"));
    state.ready = true; $("load-status").hidden = true;
    $("play").disabled = $("stop").disabled = false; buttons(); label("准备就绪");
  }
  init().catch(error => { $("load-status").textContent = error.message; console.error(error); });
})();
