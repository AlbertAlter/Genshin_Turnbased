(() => {
  "use strict";

  // Read-only battle snapshot. Localized descriptions and level tables come from the roster HTML.
  const roster = JSON.parse(document.getElementById("character-catalog").textContent);
  const en = new URLSearchParams(location.search).get("lang") === "en";
  const choose = pair => pair[en ? 1 : 0];
  const copy = {
    title:["角色详情","Character Details"], battle:["战斗画面","Battlefield"], switchParty:["切换队伍角色","Switch party member"],
    attributes:["属性","Attributes"], equipment:["装备","Equipment"], status:["状态","Status"], active:["当前出战","On field"], reserve:["后台","Off field"],
    resources:["当前资源","Current Resources"], energy:["元素能量","Energy"], shield:["护盾","Shield"], detail:["详细属性","Detailed Attributes"],
    base:["基础属性","Base Attributes"], advanced:["进阶属性","Advanced Attributes"], elemental:["元素属性","Elemental Attributes"],
    skills:["技能","Skills"], talents:["天赋","Talents"], cons:["命之座","Constellation"], consNumber:["命之座{0}","Constellation {0}"],
    noStatus:["当前没有生效的状态。","No active status effects."], source:["来源","Source"], remaining:["剩余回合","Turns remaining"], stacks:["层数：{0}","Stacks: {0}"],
    healingBonus:["治疗加成","Healing Bonus"], incomingHealingBonus:["受治疗加成","Incoming Healing Bonus"], shieldStrength:["护盾强效","Shield Strength"],
    cd:["剩余冷却：{0} 回合","Cooldown: {0} turns remaining"], ready:["可用","Ready"], lockedAt:["Lv. {0} 解锁","Unlocks at Lv. {0}"],
    weapon:["武器","Weapon"], artifacts:["圣遗物","Artifacts"], refinement:["精炼 {0} 阶","Refinement Rank {0}"], pieces:["{0} 件套","{0}-Piece Set"],
    setBonus:["已生效的套装效果","Active Set Bonuses"], close:["关闭","Close"], weaponTypes:[["单手剑","Sword"],["双手剑","Claymore"],["弓","Bow"],["长柄武器","Polearm"],["法器","Catalyst"]],
    skillTypes:[["普通攻击","Normal Attack"],["重击","Heavy Attack"],["元素战技","Elemental Skill"],["元素爆发","Elemental Burst"]],
    slots:[["生之花","Flower of Life"],["死之羽","Plume of Death"],["时之沙","Sands of Eon"],["空之杯","Goblet of Eonothem"],["理之冠","Circlet of Logos"]]
  };
  const l = key => choose(copy[key]);
  const ui = (key, fallback) => roster.ui[key]?.[en ? 1 : 0] || fallback;
  const fmt = (text, value) => text.replace("{0}",value);
  const number = new Intl.NumberFormat(en ? "en" : "zh-CN");
  const pct = value => `${value.toFixed(1)}%`;
  const percent = (value,max) => max > 0 ? Math.max(0, Math.min(100, 100 * value / max)) : 0;
  const enc = value => String(value ?? "").replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"})[c]);
  const el = id => document.getElementById(id);
  const photo = id => `Character_Icon/${id}_04.png`;
  const card = id => `Character_Bare/${id}_01.png`;
  const frame = "Character_Bare/Frame/01.png";
  const vitalDir = "UI/BattleField1/UI_components/CharacterPage/";
  // Match the detail rows in character-roster.html.
  const elementNames = {
    Pyro:["火元素","Pyro"], Hydro:["水元素","Hydro"], Dendro:["草元素","Dendro"], Electro:["雷元素","Electro"],
    Anemo:["风元素","Anemo"], Cryo:["冰元素","Cryo"], Geo:["岩元素","Geo"], Physical:["物理","Physical"]
  };
  const rarityBackgrounds = {1:"gray",2:"green",3:"blue",4:"purple",5:"gold"};
  const img = (src, cls = "") => `<img src="${enc(src)}" alt=""${cls ? ` class="${cls}"` : ""}>`;
  const disc = (src, element, charge) => {
    const background = element ? img(`Skill_Icon/%23Others/${element}_BG.png`,"disc-background") : "";
    const style = charge === undefined ? "" : ` style="--charge:${charge}%"`;
    return `<span class="disc${style ? " energy-disc" : ""}"${style} aria-hidden="true">${background}${img(src,"disc-icon")}</span>`;
  };
  const avatar = src => `<span class="avatar">${img(src)}</span>`;
  const art = src => img(src,"card-art") + img(frame,"card-frame");
  const hpBar = p => `<span class="battle-hp" aria-hidden="true"><i style="width:${p}%"></i></span>`;
  const name = c => en ? c.english : c.name;
  const skillIcon = key => {
    const icon = key.replace(/^skill/,"");
    return /^\d_[12]$/.test(icon)
      ? `Skill_Icon/%23Normal/${icon}.png`
      : `Skill_Icon/${icon.split("_")[0]}/${icon}.png`;
  };

  const snapshots = {
    1015:{level:56, hp:4820, maxHp:6216, shield:680, energy:26, maxEnergy:40, atk:982, def:475, em:98, crit:31.7, critDmg:86.6, recharge:116.2, damageBonuses:{Pyro:46.6}, levels:[6,6,8,7], cd:1, constellation:2, weapon:3170, weaponLevel:50, rank:1, sets:[523,523,523,523,518], status:["shield","rain"]},
    1023:{level:52, hp:4532, maxHp:4532, shield:680, energy:64, maxEnergy:80, atk:618, def:428, em:46, crit:22.8, critDmg:72.5, recharge:181.3, damageBonuses:{Hydro:34.8}, levels:[4,4,6,7], cd:3, constellation:1, weapon:3031, weaponLevel:50, rank:2, sets:[537,537,537,537,518], status:["shield","rain"]},
    1020:{level:50, hp:3976, maxHp:5496, shield:680, energy:35, maxEnergy:60, atk:661, def:361, em:44, crit:17.1, critDmg:61.7, recharge:158.6, damageBonuses:{Pyro:15}, levels:[4,4,6,6], cd:0, constellation:1, weapon:3029, weaponLevel:50, rank:1, sets:[524,524,524,524,518], status:["shield","rain"]},
    1026:{level:48, hp:4098, maxHp:4098, shield:0, energy:58, maxEnergy:80, atk:492, def:325, em:286, crit:13.2, critDmg:59.4, recharge:139.4, damageBonuses:{Anemo:26.6}, levels:[3,3,5,5], cd:0, constellation:0, weapon:3133, weaponLevel:40, rank:2, sets:[519,519,519,519,518], status:[]}
  };
  const party = roster.profiles.map(c => ({...c,...snapshots[c.id]}));
  const effects = {
    shield:{name:["护盾","Shield"],icon:"Shield",turns:2,description:["吸收受到的伤害，直至护盾值耗尽或持续回合结束。","Absorbs incoming damage until the shield is depleted or expires."]},
    rain:{name:["雨帘剑","Rain Swords"],icon:"HydroBuff",source:1023,turns:2,stacks:2}
  };
  const pages = ["attributes","skills","equipment","status"];
  const skillPages = ["skills","talents","cons"];
  const skillKeys = ["normal","heavy","skill","burst"];
  const dialog = el("character-page");
  const portrait = el("portrait");
  const state = {member:0, page:"attributes", skillPage:"skills", entry:0};
  let opener;
  let portraitRequest = 0;

  function formatTerms(text, heading = false) {
    return enc(text).replace(/#([0-8])([^#]+)#/g, (_, index, label) =>
      `<span class="term${heading ? " term-heading" : ""}" style="--term:${roster.colors[index]}">${label}</span>`);
  }

  function richText(template, getValue = () => "") {
    const resolved = (template || "").replace(/\{(\d+)\}/g, (token, index) => getValue(index) ?? token)
      .replace(/\\n/g,"\n");
    return resolved.split(/\n{2,}/).map(block => {
      const lines = block.split("\n").map(line => formatTerms(line, /^#[0-8][^#]+#$/.test(line.trim())));
      return `<p>${lines.join("<br>")}</p>`;
    }).join("");
  }

  function description(key, fallback, character) {
    const entry = roster.text[key];
    return richText(entry?.[en ? "en" : "zh"] || fallback, index => {
      const parameter = entry?.parameters?.[index];
      if (!parameter) return null;
      const value = parameter.values[character.levels[parameter.skill] - 1];
      return typeof value === "number" ? value.toFixed(1) : value;
    });
  }

  function drawField() {
    const alliedPositions = [[1080,443],[1243,251],[1410,443],[1573,251]];
    const enemies = roster.enemies;
    el("arena").parentElement.setAttribute("aria-label", l("battle"));
    el("enemies").innerHTML = enemies.map((e,i) =>
      `<div class="field-card enemy" style="left:${82+i*165}px;top:${i%2===0?443:251}px;z-index:${i%2===0?2:1}">${art(`Enemy/${e.id}_01.png`)}</div>`).join("");
    el("enemy-hud").innerHTML = enemies.map(e =>
      `<div>${avatar(`Enemy_Icon/${e.id}_01.png`)}<span>Lv.35 · ${enc(choose(e.name))}</span>${hpBar(100)}</div>`).join("");
    el("allies").innerHTML = party.map((c,i) =>
      `<button class="field-card" type="button" data-open="${i}" aria-label="${enc(`${l("title")} · ${name(c)}`)}" style="left:${alliedPositions[i][0]}px;top:${alliedPositions[i][1]}px;z-index:${i%2===0?2:1}">${art(card(c.id))}</button>`).join("");
    el("battle-party").innerHTML = party.map((c,i) =>
      `<button class="battle-member" type="button" data-open="${i}" aria-label="${enc(`${l("title")} · ${name(c)}`)}">${avatar(photo(c.id))}<span>${number.format(c.hp)}</span>${hpBar(percent(c.hp,c.maxHp))}</button>`).join("");
    el("battle-controls").innerHTML = `<span class="ap">AP 64 / 100</span>${party[0].skills.slice(0,3).map(skill =>
      `<span class="control-disc">${img(skillIcon(skill.icon))}</span>`).join("")}`;
  }

  async function showPortrait(member, slide = false) {
    const request = ++portraitRequest;
    const current = portrait.lastElementChild;
    if (current?.dataset.member === String(member)) return;

    const incoming = document.createElement("div");
    incoming.className = "portrait-card";
    incoming.dataset.member = member;
    incoming.innerHTML = art(card(party[member].id));
    if (!slide || !current || matchMedia("(prefers-reduced-motion: reduce)").matches || !incoming.animate) {
      portrait.replaceChildren(incoming);
      return;
    }

    await Promise.allSettled([...incoming.querySelectorAll("img")].map(image => image.decode()));
    if (request !== portraitRequest || !dialog.open) return;

    for (const animation of portrait.getAnimations({subtree:true})) animation.cancel();
    portrait.replaceChildren(current,incoming);
    const direction = Math.sign(member - Number(current.dataset.member));
    const timing = {duration:260,easing:"cubic-bezier(.22,.72,.24,1)"};
    const leaving = current.animate([{translate:"0"},{translate:`${-100*direction}%`}],{...timing,fill:"forwards"});
    incoming.animate([{translate:`${100*direction}%`},{translate:"0"}],timing);
    leaving.onfinish = leaving.oncancel = () => current.remove();
  }

  function renderParty(c) {
    el("identity").innerHTML = `<h1 id="character-name">${enc(name(c))}</h1>
      <p class="identity-meta">${img(`ElementSVG/Element_${c.elementKey}.svg`)}<span>${choose(elementNames[c.elementKey])} · ${choose(copy.weaponTypes[c.type])} · Lv. ${c.level}</span></p>
      <p class="field-role">${l(state.member === 0 ? "active" : "reserve")}</p>`;
    el("party-switch").innerHTML = party.map((character, i) => `<button type="button" data-member="${i}" aria-pressed="${i === state.member}" aria-label="${enc(name(character))}">
      ${avatar(photo(character.id))}</button>`).join("");
  }

  function renderAttributes(c) {
    const row = (key, value, fallback = key) => {
      const label = ui(`ui_stat_${key}`, fallback).replace(/#[0-8]([^#]+)#/g,"$1");
      return `<div class="stat-row"><dt>${enc(label)}</dt><dd class="numeric">${value}</dd></div>`;
    };
    const statGroup = (heading, rows) => `<section class="attribute-group"><h3>${l(heading)}</h3><dl>${rows.join("")}</dl></section>`;
    return `<section class="tile vitals"><h2 class="section-title">${l("resources")}</h2>
      <div class="vitals-body">
        <div class="energy">
          ${disc(skillIcon(c.skills[3].icon),c.elementKey,percent(c.energy,c.maxEnergy))}
          <span class="numeric">${c.energy} / ${c.maxEnergy}</span><span class="muted">${l("energy")}</span>
        </div>
        <div class="health">
          <div class="value-row">${disc(vitalDir+"HealthValueIcon.png")}<span>${enc(ui("ui_stat_hp",en?"HP":"生命值"))}</span>
            <span class="numeric">${number.format(c.hp)} / ${number.format(c.maxHp)}</span></div>
          <div class="bar" aria-hidden="true"><i style="width:${percent(c.hp,c.maxHp)}%"></i></div>
          <div class="value-row shield-row">${disc(vitalDir+"ShieldValueIcon.png")}<span>${l("shield")}</span><span class="numeric">${number.format(c.shield ?? 0)}</span></div>
        </div>
      </div>
    </section>
    <h2 class="section-title stat-heading">${l("detail")}</h2>
    ${statGroup("base", [row("max_hp",number.format(c.maxHp)),row("atk",number.format(c.atk)),row("def",number.format(c.def)),row("elemental_mastery",number.format(c.em))])}
    ${statGroup("advanced", [
      row("crit_rate",pct(c.crit)),row("crit_dmg",pct(c.critDmg)),row("heal_bonus",pct(c.healBonus ?? 0),l("healingBonus")),
      row("incoming_healing_bonus",pct(c.incomingHealingBonus ?? 0),l("incomingHealingBonus")),
      row("energy_recharge",pct(c.recharge)),row("shield_strength",pct(c.shieldStrength ?? 0),l("shieldStrength"))
    ])}
    ${statGroup("elemental", Object.entries(elementNames).flatMap(([element, names]) => [
      row(`${element.toLowerCase()}_dmg_bonus`,pct(c.damageBonuses?.[element] ?? 0)),
      row(`${element.toLowerCase()}_res`,pct(c.resistances?.[element] ?? 0),`${choose(names)}${en ? " RES" : "抗性"}`)
    ]))}`;
  }

  function renderEntries(entries) {
    const entry = entries[state.entry];
    return `<div class="detail-grid">
      <div class="detail-list" role="group" aria-label="${l(state.page)}">${entries.map((item,i) =>
        `<button type="button" class="entry${item.locked?" is-locked":""}" data-entry="${i}" aria-pressed="${i === state.entry}">
          ${disc(item.icon,item.element)}<span>${enc(item.title)}<small>${enc(item.label)}</small></span></button>`).join("")}</div>
      <article class="tile reading">
        <header class="reading-head">${disc(entry.icon,entry.element)}<div><h2>${enc(entry.title)}</h2><p class="muted">${enc(entry.meta || entry.label)}</p></div></header>
        <div class="description">${entry.body}</div>${entry.footer || ""}
      </article>
    </div>`;
  }

  function renderSkills(c) {
    let entries;
    if (state.skillPage === "skills") {
      entries = c.skills.map((skill,i) => {
        const label = `${choose(copy.skillTypes[i])} · Lv. ${c.levels[i]}`;
        return {title:en ? c.enSkills[i] || skill.name : skill.name, icon:skillIcon(skill.icon),label,element:c.elementKey,
          meta:i === 2 ? `${label} · ${c.cd ? fmt(l("cd"),c.cd) : l("ready")}` : label,
          body:description(`character_${c.id}_${skillKeys[i]}_desc`,skill.description,c)};
      });
    } else {
      const talent = state.skillPage === "talents";
      entries = c[state.skillPage].map(skill => {
        const key = `character_${c.id}_${talent ? "talent" : "constellation"}_${skill.index}`;
        const locked = talent ? c.level < skill.threshold : c.constellation < skill.index;
        const label = talent
          ? locked ? fmt(l("lockedAt"),skill.threshold) : ui("ui_talent_unlocked",en?"Unlocked":"已解锁")
          : `${fmt(l("consNumber"),skill.index)} · ${locked ? (en?"Inactive":"未激活") : ui("ui_constellation_activated",en?"Activated":"已激活")}`;
        return {title:(en ? roster.names[key+"_name"] : "") || skill.name,icon:skillIcon(skill.icon),label,locked,
          element:locked ? undefined : c.elementKey,
          body:description(key+"_desc",skill.description,c)};
      });
    }
    return `<div class="subtabs" role="group" aria-label="${l("skills")}">${skillPages.map(key =>
      `<button type="button" data-skill-page="${key}" aria-pressed="${key === state.skillPage}">${l(key)}</button>`).join("")}</div>
      <div class="skill-scroll" tabindex="0" aria-label="${l(state.skillPage)}">${renderEntries(entries)}</div>`;
  }

  function renderEquipment(c) {
    const weapon = roster.weapons[c.weapon];
    const rarity = rarityBackgrounds[weapon.stars];
    const background = rarity ? ` style="--weapon-background:url('RarityBackground/background_${rarity}.png')"` : "";
    const sets = c.sets.reduce((counts,id) => (counts[id] = (counts[id] || 0) + 1,counts),{});
    return `<div class="equipment">
      <section class="tile">
        <header class="weapon-info"${background}>${img(`Weapon_Icon/${c.weapon}_01.png`,"item-art")}
          <div><p class="muted">${l("weapon")}</p><h2>${enc(name(weapon))}</h2>
          <p class="muted">Lv. ${c.weaponLevel} · ${fmt(l("refinement"),c.rank)}</p></div></header>
        <div class="weapon-passive description"><p>${enc(choose(weapon.title))}</p>
          ${richText(choose(weapon.description), i => weapon.params?.[i]?.[c.rank - 1])}</div>
      </section>
      <section class="tile">
        <h2>${l("artifacts")}</h2>
        <div class="artifacts">${c.sets.map((id,i) => `<div class="artifact" title="${enc(name(roster.sets[id]))}">
          ${img(`Artifact_Icon/${id}${i+1}.png`,"item-art")}<span>${choose(copy.slots[i])}</span></div>`).join("")}</div>
        <p class="muted">${l("setBonus")}</p>
        ${Object.entries(sets).filter(([,count]) => count >= 2).map(([id,count]) => {
          const set = roster.sets[id];
          return `<div class="set-effect"><h3>${enc(name(set))}<span>· ${fmt(l("pieces"),count)}</span></h3>
            ${[2,4].filter(size => count >= size).map(size => `<div class="description set-tier"><h4>${fmt(l("pieces"),size)}</h4>${richText(choose(set.effects[size]))}</div>`).join("")}</div>`;
        }).join("")}
      </section>
    </div>`;
  }

  function renderStatus(c) {
    if (!c.status.length) return `<p class="tile empty">${l("noStatus")}</p>`;
    return renderEntries(c.status.map(key => {
      const effect = effects[key];
      const source = party.find(p => p.id === effect.source);
      const turns = `${l("remaining")} · ${effect.turns}`;
      const label = effect.stacks == null ? turns : `${turns} · ${fmt(l("stacks"),effect.stacks)}`;
      let body = richText(effect.description ? choose(effect.description) : "");
      if (key === "rain") {
        const entry = roster.text.character_1023_skill_desc;
        const lines = (entry[en?"en":"zh"] || entry.zh).split(/\\n|\n/);
        const heading = lines.findIndex(line => /^#8[^#]+#$/.test(line.trim()));
        const rain = heading === -1 ? lines.at(-1) : lines.slice(heading + 1).join("\n");
        body = richText(rain,index => {
          const p = entry.parameters[index];
          return p?.values[source.levels[p.skill] - 1]?.toFixed(1);
        });
      }
      const info = key === "shield" ? `<span>${l("shield")} · ${number.format(c.shield)}</span>` : "";
      const owner = source ? `<span>${l("source")} · ${enc(name(source))}</span>` : "";
      return {title:choose(effect.name),icon:`Status_Icon/${effect.icon}.png`,label,body,
        footer:`<div class="status-meta">${info}${owner}</div>`};
    }));
  }

  const renderers = {attributes:renderAttributes,skills:renderSkills,equipment:renderEquipment,status:renderStatus};
  function render(focusSelector) {
    const c = party[state.member];
    renderParty(c);
    el("tabs").innerHTML = pages.map(key => {
      const selected = key === state.page;
      return `<button type="button" id="tab-${key}" data-page="${key}" role="tab" aria-selected="${selected}"
        tabindex="${selected?0:-1}" aria-controls="panel-${key}">${l(key)}</button>`;
    }).join("");
    el("panels").innerHTML = pages.map(key => `<div id="panel-${key}" role="tabpanel" class="panel" aria-labelledby="tab-${key}"
      tabindex="0"${key === state.page ? "" : " hidden"}>${key === state.page ? renderers[key](c) : ""}</div>`).join("");
    if (focusSelector) dialog.querySelector(focusSelector)?.focus({preventScroll:true});
  }

  function setPage(page, focus = false) {
    state.page = page;
    state.entry = 0;
    render(focus ? `#tab-${page}` : null);
  }

  function setMember(member) {
    state.member = member;
    state.entry = 0;
    showPortrait(member,true);
    render(`[data-member="${member}"]`);
  }

  function open(member, trigger) {
    opener = trigger;
    Object.assign(state,{member,page:"attributes",skillPage:"skills",entry:0});
    showPortrait(member);
    render();
    dialog.showModal();
  }

  el("arena").addEventListener("click", event => {
    const button = event.target.closest("[data-open]");
    if (button) open(Number(button.dataset.open),button);
  });
  dialog.addEventListener("click", event => {
    if (event.target === dialog) {
      const box = dialog.getBoundingClientRect();
      if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
      return;
    }
    const button = event.target.closest("button");
    if (!button) return;
    const {page,member,skillPage,entry} = button.dataset;
    if (page) setPage(page,true);
    else if (member !== undefined) setMember(Number(member));
    else if (skillPage) {
      state.skillPage = skillPage;
      state.entry = 0;
      render(`[data-skill-page="${skillPage}"]`);
    } else if (entry !== undefined) {
      state.entry = Number(entry);
      render(`[data-entry="${entry}"]`);
    }
  });
  dialog.addEventListener("keydown", event => {
    if (event.altKey || event.ctrlKey || event.metaKey) return;
    const key = event.key.toLowerCase();
    if (["a","d"].includes(key)) {
      event.preventDefault();
      setMember((state.member + party.length + (key === "d" ? 1 : -1)) % party.length);
    } else if (event.target.matches('[role="tab"]') && ["arrowleft","arrowright","home","end"].includes(key)) {
      event.preventDefault();
      const i = pages.indexOf(state.page);
      const next = key === "home" ? 0 : key === "end" ? pages.length-1 : (i+pages.length+(key==="arrowright"?1:-1))%pages.length;
      setPage(pages[next],true);
    }
  });
  dialog.addEventListener("close", () => {
    portraitRequest++;
    for (const animation of portrait.getAnimations({subtree:true})) animation.cancel();
    opener?.focus({preventScroll:true});
  });

  function resize() {
    document.documentElement.style.setProperty("--scale",Math.min(innerWidth / 1920, innerHeight / 1080));
  }
  document.documentElement.lang = en ? "en" : "zh-CN";
  document.title = l("title");
  document.querySelector("[data-copy=title]").textContent = l("title");
  el("tabs").setAttribute("aria-label",l("title"));
  el("close").setAttribute("aria-label",l("close"));
  el("party-switch").setAttribute("aria-label",l("switchParty"));
  window.addEventListener("resize",resize);
  resize();
  drawField();
  open(0,el("battle-party").querySelector("button"));
})();
