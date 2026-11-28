/* Pure presentation sampling. Inputs are committed result snapshots, never GU rules.
 * Mirrors ElementContactEventData / ReactionEventData / AuraChangedEventData.
 * The preview supplies fixtures; a combat adapter can supply the same payloads.
 */
(() => {
  "use strict";
  const defaults = Object.freeze({approach:0.18, gather:0.06, flash:0.09, hold:0.06, settle:0.22,
    refreshSeconds:0.22, iconWidth:39, iconHeight:41, gap:4, travel:48, burst:0.72});
  const clamp = value => Math.max(0, Math.min(1, value));
  const smooth = value => { value = clamp(value); return value * value * (3 - 2 * value); };
  const contactEase = value => {
    value = clamp(value);
    return value < 0.5 ? 16 * value ** 5 : 1 - (-2 * value + 2) ** 5 / 2;
  };
  const mix = (a, b, t) => a + (b - a) * t;
  const freezePools = pools => Object.freeze(pools.map(pool => Object.freeze({...pool})));
  const visible = pool => (pool.Kind === "Intrinsic" || pool.Kind === "NormalAura") &&
    (pool.IsIntrinsic || Number.isFinite(pool.Amount) && pool.Amount > 0);
  const elements = pools => [...new Set(pools.filter(visible).map(pool => pool.Element))];

  function orderPools(previous, pools, refreshed = []) {
    const present = elements(pools);
    const order = previous.filter(element => present.includes(element));
    for (const key of refreshed) {
      const pool = pools.find(pool => key === pool.Kind + ":" + pool.Element && visible(pool));
      if (pool && !order.includes(pool.Element)) order.push(pool.Element);
    }
    for (const element of present) if (!order.includes(element)) order.push(element);
    return order;
  }

  function createClip({identity, before, order = [], contact = null, reactions = [], auraChange = null}, options = {}) {
    const config = Object.freeze({...defaults, ...options});
    for (const key of Object.keys(defaults)) {
      if (!Number.isFinite(config[key]) || config[key] < 0) throw new Error("Invalid aura animation parameter " + key);
    }
    const previous = freezePools(auraChange?.Before ?? before ?? []);
    const next = freezePools(auraChange?.After ?? previous);
    const records = Object.freeze(reactions.map(reaction => Object.freeze({...reaction,
      Elements:Object.freeze([...(reaction.Elements ?? [])])})));
    const involved = [...new Set(records.flatMap(reaction => reaction.Elements))];
    const beforeOrder = Object.freeze(orderPools(order, previous));
    const afterOrder = Object.freeze(orderPools(beforeOrder, next, auraChange?.RefreshedPools));
    const isRefresh = records.length === 0 && auraChange != null && contact?.CanApplyAura !== false &&
      beforeOrder.includes(contact?.Element) && beforeOrder.length === afterOrder.length &&
      beforeOrder.every(element => afterOrder.includes(element));
    const impact = isRefresh ? 0 : config.approach + config.gather;
    const release = isRefresh ? 0 : impact + config.flash + config.hold;
    const duration = isRefresh ? config.refreshSeconds : release + config.settle;
    return Object.freeze({identity:Object.freeze({...identity}), before:previous, after:next, beforeOrder, afterOrder,
      contact:contact ? Object.freeze({...contact}) : null, reactions:records, involved:Object.freeze(involved),
      config, impact, release, duration, isRefresh});
  }

  function still(order, config = defaults) {
    return order.map((element, index) => ({key:"aura:" + element, element, x:0,
      y:index * (config.iconHeight + config.gap), scale:1, opacity:1, glow:0, role:"aura"}));
  }

  function sample(clip, seconds) {
    const {config, beforeOrder, afterOrder, impact, release, duration} = clip;
    const time = Math.max(0, seconds);
    if (time >= duration) return {icons:still(afterOrder, config), flash:0, collisionY:0, progress:1};
    if (clip.isRefresh) {
      const progress = clamp(time / Math.max(0.001, duration));
      const pulse = progress < 0.28 ? smooth(progress / 0.28) : 1 - smooth((progress - 0.28) / 0.72);
      const icons = still(beforeOrder, config);
      const icon = icons.find(icon => icon.element === clip.contact.Element);
      icon.scale = 1 + pulse * 0.14;
      icon.glow = pulse;
      return {icons, flash:0, collisionY:0, progress};
    }
    const reaction = clip.reactions.length > 0;
    const gather = contactEase(time / Math.max(0.001, impact));
    const settleTime = clamp((time - release) / Math.max(0.001, config.settle));
    const settle = smooth(settleTime);
    // Return horizontally first; any post-reaction list compaction follows it.
    const returnX = smooth(settleTime / 0.6);
    const reflowY = smooth((settleTime - 0.6) / 0.4);
    const fade = smooth((time - impact) / Math.max(0.001, config.flash + config.hold));
    const step = config.iconHeight + config.gap;
    const incoming = clip.contact?.Element;
    const icons = [];
    const participants = beforeOrder.filter(element => clip.involved.includes(element));
    const resident = participants[0] ?? (beforeOrder.includes(incoming) ? incoming : null);
    const collisionIndex = resident ? beforeOrder.indexOf(resident) : Math.max(0, afterOrder.indexOf(incoming));
    const collisionY = collisionIndex * step;
    for (let index = 0; index < beforeOrder.length; index++) {
      const element = beforeOrder[index];
      const joins = reaction && clip.involved.includes(element);
      const retained = afterOrder.includes(element);
      const fromY = index * step;
      const finalY = retained ? afterOrder.indexOf(element) * step : fromY;
      icons.push({key:"aura:" + element, element, role:"aura",
        x:joins ? mix(-5 * gather, 0, returnX) : 0, y:mix(fromY, finalY, reflowY),
        scale:retained ? 1 + (joins ? 0.10 * gather * (1 - settle) : 0) : mix(1, 0.45, fade),
        opacity:retained ? 1 : 1 - fade, glow:joins ? (1 - fade) * gather : 0});
    }
    // Participants held only by virtual pools can appear during a real reaction,
    // but never become persistent aura-bar entries.
    const virtual = clip.involved.filter(element => element !== incoming && !beforeOrder.includes(element));
    for (const element of virtual) {
      icons.push({key:"participant:" + element, element, role:"participant", x:-5,
        y:collisionY, scale:0.9 + gather * 0.1, opacity:gather * (1 - fade), glow:gather * (1 - fade)});
    }
    if (incoming && incoming !== "Physical") {
      const becomesAura = !beforeOrder.includes(incoming) && afterOrder.includes(incoming);
      const finalY = becomesAura ? afterOrder.indexOf(incoming) * step : collisionY;
      const x = mix(config.travel, 13, gather);
      icons.push({key:"incoming:" + incoming, element:incoming, role:becomesAura ? "new-aura" : "incoming",
        x:mix(x, 0, returnX), y:mix(collisionY, finalY, reflowY), scale:mix(0.85, 1, gather),
        opacity:Math.min(1, time / 0.08) * (becomesAura ? 1 : 1 - fade), glow:gather * (1 - fade)});
    }
    // Support additions in a snapshot even when no ElementContact was emitted.
    for (const element of afterOrder) {
      if (beforeOrder.includes(element) || element === incoming) continue;
      icons.push({key:"added:" + element, element, role:"new-aura", x:0,
        y:afterOrder.indexOf(element) * step, scale:mix(0.7, 1, settle), opacity:settle, glow:0});
    }
    const age = time - impact;
    const flash = reaction && age >= 0 ? (1 - clamp(age / 0.32)) * config.burst : 0;
    return {icons, flash, collisionY, progress:clamp(time / duration)};
  }

  class View {
    constructor(card, palette, host) {
      this.card = card; this.palette = palette; this.nodes = new Map();
      this.root = document.createElement("div"); this.root.className = "element-aura-layer";
      this.root.dataset.owner = card.key;
      const outward = card.side === "ally" ? 1 : -1;
      this.outward = outward; this.vertical = card.row === 1 ? 1 : -1;
      const width = card.root.offsetWidth, height = card.root.offsetHeight;
      const centerX = card.root.offsetLeft + width / 2, centerY = card.root.offsetTop + height / 2;
      this.root.style.left = centerX + outward * (width / 2 + 9 + defaults.iconWidth / 2) + "px";
      this.root.style.top = centerY + (card.row === 1 ? -height / 2 + 24 : height / 2 - 24) + "px";
      host.append(this.root);
      this.burst = document.createElement("div"); this.burst.className = "aura-burst";
      this.root.append(this.burst);
      for (let index = 0; index < 6; index++) {
        const spark = document.createElement("i"); spark.style.setProperty("--angle", index * 60 + 15 + "deg");
        this.burst.append(spark);
      }
    }
    present(frame, clip = null) {
      for (const node of this.nodes.values()) node.hidden = true;
      for (const icon of frame.icons) {
        const style = this.palette[icon.element];
        if (!style?.icon || icon.opacity < 0.001) continue;
        let node = this.nodes.get(icon.key);
        if (!node) {
          node = document.createElement("span"); node.className = "aura-symbol";
          node.style.setProperty("--element-color", style.color);
          node.setAttribute("aria-label", style.name + "元素");
          // A clipped zero-blur colored shadow works for file:// images too.
          // CSS image masks request CORS and may disappear on local HTML pages.
          const glyph = document.createElement("span"); glyph.className = "aura-glyph";
          const image = document.createElement("img"); image.src = style.icon; image.alt = ""; image.draggable = false;
          glyph.append(image); node.append(glyph);
          this.root.append(node); this.nodes.set(icon.key, node);
        }
        node.hidden = false; node.dataset.role = icon.role; node.dataset.element = icon.element;
        node.style.opacity = icon.opacity;
        node.style.transform = `translate(-50%,-50%) translate(${icon.x * this.outward}px,${icon.y * this.vertical}px) scale(${icon.scale})`;
        node.style.filter = `brightness(${1 + icon.glow * 0.35}) drop-shadow(0 1px 2px #1d211e) drop-shadow(0 0 ${2 + icon.glow * 10}px ${style.color})`;
      }
      const color = this.palette[clip?.contact?.Element]?.color ?? "#FEFEFE";
      this.burst.style.setProperty("--burst-color", color);
      this.burst.style.top = (frame.collisionY ?? 0) * this.vertical + "px";
      this.burst.style.opacity = frame.flash;
      this.burst.style.transform = `translate(-50%,-50%) scale(${0.45 + (1 - frame.flash) * 0.85})`;
    }
    clear() { this.present({icons:[], flash:0}); }
    dispose() { this.root.remove(); this.nodes.clear(); }
  }
  window.ElementAuraMotion = Object.freeze({defaults, elements, orderPools, createClip, sample, still, View});
})();
