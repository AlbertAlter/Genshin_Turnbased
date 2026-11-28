/* Shared BattleField1 preview composition and static card/HUD assembly. */
(() => {
  "use strict";
  const composition = Object.freeze([
    {key:"enemy5",side:"enemy",slot:5,id:2008,name:"火箭丘丘人",x:180.5,y:608,row:2},
    {key:"enemy4",side:"enemy",slot:4,id:2010,name:"木盾丘丘人",x:354.5,y:405,row:1},
    {key:"enemy3",side:"enemy",slot:3,id:2014,name:"水丘丘萨满",x:509.5,y:609,row:2},
    {key:"enemy2",side:"enemy",slot:2,id:2020,name:"水史莱姆",x:683.5,y:405,row:1},
    {key:"enemy1",side:"enemy",slot:1,id:2005,name:"打手丘丘人",x:838.5,y:609,row:2},
    {key:"ally1",side:"ally",slot:1,id:1015,name:"迪卢克",x:1167.5,y:608,row:2,hp:4820,maxHp:6216},
    {key:"ally2",side:"ally",slot:2,id:1023,name:"行秋",x:1330.5,y:405,row:1,hp:4532,maxHp:4532},
    {key:"ally3",side:"ally",slot:3,id:1020,name:"班尼特",x:1496.5,y:608,row:2,hp:3976,maxHp:5496},
    {key:"ally4",side:"ally",slot:4,id:1026,name:"砂糖",x:1659.5,y:405,row:1,hp:4098,maxHp:4098}
  ].map(Object.freeze));

  function mount({cardsHost, enemyHud, partyHud, assetRoot = "../../"}) {
    const cards = new Map();
    const img = (src, className = "") => {
      const node = document.createElement("img");
      Object.assign(node, {src, className, alt:"", draggable:false});
      return node;
    };
    const portrait = c => assetRoot + (c.side === "ally" ? "Character_Icon/" : "Enemy_Icon/") +
      c.id + (c.side === "ally" ? "_04.png" : "_01.png");
    const health = ratio => {
      const node = document.createElement("span");
      node.className = "hud-hp";
      const fill = document.createElement("i");
      fill.style.width = ratio * 100 + "%";
      node.append(fill);
      return node;
    };
    cardsHost.replaceChildren(); enemyHud.replaceChildren(); partyHud.replaceChildren();
    for (const c of composition) {
      const root = document.createElement("div");
      root.className = "card-slot " + c.side;
      root.dataset.card = c.key;
      root.setAttribute("aria-label", `${c.side === "ally" ? "我方" : "敌方"}${c.slot} · ${c.name}`);
      root.style.cssText = `left:${c.x - 98.5}px;top:${c.y - 169}px;z-index:${c.row * 10}`;
      const motion = document.createElement("div"), reaction = document.createElement("div"), visual = document.createElement("div");
      motion.className = "card-motion"; reaction.className = "card-reaction"; visual.className = "card-visual";
      const frame = assetRoot + "Character_Bare/Frame/01.png";
      visual.append(img(assetRoot + (c.side === "ally" ? "Character_Bare/" : "Enemy/") + c.id + "_01.png", "card-art"),
        img(frame, "card-frame"), img(assetRoot + "UI/BattleField1/UI_components/card/CardInnerShadow.png", "card-inner"));
      const glow = document.createElement("div"); glow.className = "card-glow";
      const chargeFrame = img(frame, "charge-frame");
      const identity = document.createElement("span"); identity.className = "card-identity"; identity.textContent = c.slot;
      visual.append(glow, chargeFrame, identity);
      const caption = document.createElement("div"); caption.className = "card-base-label";
      const index = document.createElement("span"); index.className = "slot-number";
      index.textContent = `${c.side === "ally" ? "我方" : "敌方"} ${c.slot}`;
      caption.append(index, c.name);
      if (c.side === "enemy") {
        const mini = document.createElement("div"); mini.className = "card-mini-hp";
        mini.append(document.createElement("i")); root.append(mini);
      }
      reaction.append(visual); motion.append(reaction); root.append(motion, caption); cardsHost.append(root);
      cards.set(c.key, {...c, root, motion, reaction, visual, glow, chargeFrame, identity});
    }
    for (const c of composition.filter(c => c.side === "enemy").sort((a, b) => a.slot - b.slot)) {
      const row = document.createElement("div"); row.className = "enemy-member";
      const icon = document.createElement("span"); icon.className = "hud-portrait"; icon.append(img(portrait(c)));
      const name = document.createElement("span"); name.innerHTML = `<b>Lv. 20</b>${c.name}`;
      row.append(icon, name, health(1)); enemyHud.append(row);
    }
    for (const c of composition.filter(c => c.side === "ally")) {
      const row = document.createElement("div"); row.className = "party-member";
      const icon = document.createElement("span"); icon.className = "hud-portrait"; icon.append(img(portrait(c)));
      const name = document.createElement("strong"); name.textContent = c.name;
      const values = document.createElement("div");
      const amount = document.createElement("small"); amount.textContent = `${c.hp.toLocaleString()} / ${c.maxHp.toLocaleString()}`;
      values.append(amount, health(c.hp / c.maxHp)); row.append(icon, name, values); partyHud.append(row);
    }
    return cards;
  }
  window.BattlePreviewScene = Object.freeze({composition, mount});
})();
