(() => {
  "use strict";
  const M=window.CardMotion, S=window.CardStatus, $=id=>document.getElementById(id);
  const ROOT="../../";
  // Centers from the saved BattleField1 RectTransforms. 9 is this preview's
  // composition, not a constraint of CardMotion or CardCharge.
  let cards;
  const fx=[], targetMemory=new Map();
  const settings={duration:0.64,strength:1,tilt:8,contact:0.44,gap:18};
  const state={preset:"status-cast",lastAttack:"lean",lastStatus:"status-cast",time:0,energyTime:0,playing:false,ready:false,finished:false,raf:0,last:0,released:false,cancelled:false,cancelAge:0,queue:[],queueDelay:0};
  const avatar=c=>ROOT+(c.side==="ally"?"Character_Icon/":"Enemy_Icon/")+c.id+(c.side==="ally"?"_04.png":"_01.png");
  const isCharge=()=>state.preset==="charge"||state.preset==="enemycharge";
  const selected=()=>cards.get($("source").value);
  const target=()=>cards.get($("target").value);
  const preset=()=>M.presets.find(p=>p.id===state.preset)||S.presets.find(p=>p.id===state.preset);
  const isStatus=()=>Boolean(preset().relation);
  const relation=()=>preset().relation||"enemy";
  const targetKey=()=>selected().key+"/"+relation();
  const img=(src,cls="")=>{const e=document.createElement("img");e.src=src;e.alt="";e.draggable=false;if(cls)e.className=cls;return e;};
  function buildField() {
    cards=window.BattlePreviewScene.mount({cardsHost:$("cards"),enemyHud:$("enemy-hud"),partyHud:$("party-hud")});
    for(const c of cards.values()) {
      const option=document.createElement("option");option.value=c.key;option.textContent=`${c.side==="ally"?"我方":"敌方"} ${c.slot} · ${c.name}`;$("source").append(option);
    }
    $("source").value="ally1";rebuildTargets();
    const layer=$("effect-layer");
    for(let i=0;i<5;i++) {
      const slash=img(ROOT+"UI/SlashComponents/SwordNormal_Slash_A.png","hit-vfx");
      const flash=img(ROOT+"UI/SlashComponents/SwordNormal_HitFlash.png","hit-flash");
      const number=document.createElement("span");number.className="damage-label";
      layer.append(slash,flash,number);fx.push({slash,flash,number});
    }
    const projectile=img(ROOT+"UI/SlashComponents/Bow_Projectile.png","projectile");projectile.id="projectile";layer.append(projectile);
    for(const back of [true,false]) {
      const ring=document.createElement("canvas");ring.className="cast-ring "+(back?"cast-ring-back":"cast-ring-front");
      ring.id=back?"cast-ring-back":"cast-ring";layer.append(ring);
    }
  }
  function eligibleTargets() {
    const source=selected();
    return [...cards.values()].filter(c=>relation()==="self"?c.key===source.key:relation()==="ally"?c.side===source.side&&c.key!==source.key:c.side!==source.side).sort((a,b)=>a.slot-b.slot);
  }
  function rebuildTargets() {
    const old=targetMemory.get(targetKey())||$("target").value;$("target").replaceChildren();
    const choices=eligibleTargets();
    for(const c of choices) {const o=document.createElement("option");o.value=c.key;o.textContent=`${c.side==="ally"?"我方":"敌方"} ${c.slot} · ${c.name}`;$("target").append(o);}
    if(choices.length>1) {const all=document.createElement("option");all.value="all";all.textContent=relation()==="ally"?"全体队友（不含自身）":isStatus()?"全体对手":"全部对手（群攻预览）";$("target").append(all);}
    if(old==="all"&&choices.length>1||choices.some(c=>c.key===old)) $("target").value=old;
    targetMemory.set(targetKey(),$("target").value);
  }
  const targets=()=>$("target").value==="all"?eligibleTargets():[target()];
  const cardRect=c=>({x:c.root.offsetLeft+c.root.offsetWidth/2,y:c.root.offsetTop+c.root.offsetHeight/2,width:c.root.offsetWidth,height:c.root.offsetHeight});
  function primaryTarget(a,list) {
    if(state.preset!=="target-lunge") return list[0];
    const from=cardRect(a),distance=c=>{const r=cardRect(c);return Math.hypot(r.x-from.x,r.y-from.y);};
    return list.reduce((nearest,c)=>distance(c)<distance(nearest)?c:nearest,list[0]);
  }
  function updateParticipants() {
    const a=selected(),victims=targets();
    for(const c of cards.values()) {
      c.root.classList.toggle("is-attacker",c.key===a.key);
      c.root.classList.toggle("is-target",!isStatus()&&victims.some(v=>v.key===c.key));
      c.identity.dataset.role=c.key===a.key?(isStatus()?"施法":"攻击"):victims.some(v=>v.key===c.key)?"受击":"";
    }
    $("source-label").textContent=isStatus()?"施加者":"攻击者";$("target-label").textContent=isStatus()?"获得状态":"受击者";
    $("target").closest("label").hidden=isStatus();
    $("participant-hint").textContent=isStatus()?"只播放施加者动画 · 不区分状态目标":"可选双方阵营 · 更改后重播当前动作";
  }
  function changeParticipants(sourceChanged) {
    stop();if(sourceChanged) rebuildTargets();else targetMemory.set(targetKey(),$("target").value);updateParticipants();
    if(state.ready) play();
  }
  function applyPose(node,p) {node.style.transform=M.transform(p);node.style.opacity=p.alpha;}
  function resetVisuals() {
    for(const c of cards.values()) {c.motion.style.transform="";c.motion.style.opacity="";c.reaction.style.transform="";c.reaction.style.opacity="";c.glow.style.opacity="0";c.chargeFrame.style.opacity="0";c.root.style.zIndex=c.row*10;}
    for(const f of fx) for(const e of Object.values(f)) e.style.opacity="0";
    $("projectile").style.opacity="0";$("cast-ring").style.opacity="0";$("cast-ring-back").style.opacity="0";$("arena").style.transform="";
    const chargeCanvas=$("charge-layer");chargeCanvas.getContext("2d").clearRect(0,0,chargeCanvas.width,chargeCanvas.height);$("action-prompt").hidden=true;
  }
  function mark(node,x,y,opacity,transform="") {node.style.left=x+"px";node.style.top=y+"px";node.style.opacity=opacity;node.style.transform=`translate(-50%,-50%) ${transform}`;}
  function render() {
    resetVisuals();const a=selected(), list=targets(), b=primaryTarget(a,list), duration=settings.duration;
    const t=M.clamp(state.time/duration), direction=Math.sign(b.x-a.x)||-1;
    let attackT=t, attackContact=settings.contact;
    const combo=state.preset==="combo", localT=combo?Math.min(0.999999,t*3)%1:t;
    if(combo) {attackT=localT;attackContact=0.44;}
    const poseOptions={...settings,direction,width:a.root.offsetWidth,height:a.root.offsetHeight,contact:attackContact,sourceRect:cardRect(a),targetRect:cardRect(b)};
    let actorPose=isStatus()?S.sourcePose(preset().style,t,poseOptions):M.sample(state.preset,attackT,poseOptions);
    if(state.cancelled) actorPose=M.pose();
    if(!state.finished&&state.time>0) {
      applyPose(a.motion,actorPose);a.glow.style.opacity=actorPose.glow;
      if(!isCharge()||(!state.cancelled&&t>=settings.contact-0.13)) a.root.style.zIndex=75;
    }
    const selfHit=state.preset==="hit"||state.preset==="recoil";
    const attack=!isStatus()&&!['enter','exit','cast'].includes(state.preset)&&!state.cancelled;
    const heavy=state.preset==="heavy"||state.preset==="recoil"||isCharge()||(combo&&t>=2/3);
    const hits=combo?[0,1,2].map(i=>duration*(i+0.44)/3):[duration*(selfHit?0.12:settings.contact)];
    for(let i=0;i<list.length;i++) {
      const c=list[i], f=fx[i];
      if(!attack||state.finished) continue;
      const age=state.time-[...hits].reverse().find(v=>v<=state.time);
      if(!Number.isFinite(age)) continue;
      const hitIndex=hits.filter(v=>v<=state.time).length-1;
      const thisHeavy=combo?hitIndex===2:heavy;
      const hitDirection=Math.sign(c.x-a.x)||direction;
      if($("hit-reaction").checked) applyPose(c.reaction,M.reaction(age,{heavy:thisHeavy,direction:hitDirection,strength:settings.strength,seed:517+i*73}));
      if($("hit-effects").checked&&age<0.3) {
        const v=age/0.3,alpha=(1-M.smooth(v))*0.95, size=(0.82+v*0.36)*(thisHeavy?1.35:1);
        mark(f.slash,c.x,c.y-12,alpha,`rotate(${hitDirection<0?-22:22}deg) scale(${size})`);
        mark(f.flash,c.x+hitDirection*10,c.y-12,Math.max(0,1-age/0.12)*0.65,`scale(${0.7+v*0.8})`);
      }
      if($("damage-numbers").checked&&age<0.62) {f.number.textContent=thisHeavy?"742":"128";mark(f.number,c.x+15,c.y-110-age*65,Math.min(1,age/0.03)*M.clamp((0.62-age)/0.2));}
    }
    if(attack&&heavy&&$("screen-shake").checked&&!state.finished) {
      const hit=hits.filter(h=>h<=state.time).pop();
      if(hit!==undefined&&(!combo||hit===hits[2])) {const p=M.shake("screen",state.time-hit,1920,1080,185,settings.strength);$("arena").style.transform=`translate(${p.x}px,${p.y}px)`;}
    }
    if(state.preset==="ranged"&&!state.finished) {
      const launch=Math.max(0,settings.contact-0.27), flight=(t-launch)/(settings.contact-launch);
      if(flight>=0&&flight<1) {
        const x=a.x+(b.x-a.x)*flight, y=a.y-25+(b.y-a.y)*flight-Math.sin(flight*Math.PI)*75;
        const angle=Math.atan2(b.y-a.y,b.x-a.x)*180/Math.PI;
        mark($("projectile"),x,y,1,`rotate(${angle}deg)`);
      }
    }
    if(isStatus()&&!state.finished) {
      const width=a.root.offsetWidth,height=a.root.offsetHeight;
      const size=Math.ceil(Math.max(width*3.5,height*2));
      for(const backHalf of [true,false]) {
        const ring=$(backHalf?"cast-ring-back":"cast-ring");
        if(ring.parentElement!==a.root) a.root.append(ring);
        if(ring.width!==size||ring.height!==size) {
          ring.width=ring.height=size;ring.style.width=ring.style.height=size+"px";
        }
        ring.style.opacity="1";
        S.drawHalo(ring.getContext("2d"),{width,height,progress:t,strength:settings.strength,backHalf});
      }
    }
    if(isCharge()&&!state.finished) {
      const releaseAt=duration*(settings.contact-0.13), age=state.time-releaseAt;
      const phase=state.cancelled?"cancel":age>=0?"release":"charging";
      const canvas=$("charge-layer");
      if(canvas.parentElement!==a.visual) a.visual.append(canvas);
      const intensity=window.CardCharge.draw(canvas.getContext("2d"),{x:canvas.width/2,y:canvas.height/2,elapsed:state.energyTime,phase,phaseAge:state.cancelled?state.cancelAge:Math.max(0,age),strength:settings.strength});
      a.chargeFrame.style.opacity=Math.min(1,intensity)*0.2;
    }
    if(state.time>0&&!state.finished&&!state.cancelled) {
      const p=$("action-prompt");p.hidden=false;p.classList.toggle("enemy",a.side==="enemy");
      if($("prompt-avatar").getAttribute("src")!==avatar(a)) $("prompt-avatar").src=avatar(a);
      $("prompt-side").textContent=a.side==="enemy"?"敌方行动":"角色行动";
      $("prompt-name").textContent=selfHit?`${b.name} · ${preset().name}`:preset().name;
    }
    $("seek").value=Math.round(t*1000);
    $("time").textContent=isCharge()&&$("hold-charge").checked&&!state.released&&!state.cancelled&&!state.finished
      ? `持续 ${state.energyTime.toFixed(1)} s` : `${state.time.toFixed(2)} / ${duration.toFixed(2)} s`;
  }
  function cancelFrame() {if(state.raf) cancelAnimationFrame(state.raf);state.raf=0;}
  function label(value) {$("playback-status").textContent=value;}
  function updateButtons() {$("pause").textContent=state.playing?"暂停":"继续";$("pause").disabled=!state.ready||state.finished;$("release-charge").disabled=!state.ready||!isCharge()||state.finished;$("cancel-charge").disabled=$("release-charge").disabled;}
  function ensureFrame() {if(!state.raf&&state.playing){state.last=performance.now();state.raf=requestAnimationFrame(tick);}}
  function tick(now) {
    state.raf=0;if(!state.playing) return;
    const dt=Math.max(0,(now-state.last)/1000)*Number($("speed").value);state.last=now;
    if(state.queueDelay>0) {state.queueDelay-=dt;if(state.queueDelay<=0) advanceDemo();}
    else {
      state.energyTime+=dt;
      if(state.cancelled) {state.cancelAge+=dt;if(state.cancelAge>=0.24){finish("已打断蓄力");return;}}
      else {
        state.time=Math.min(settings.duration,state.time+dt);
        if(isCharge()&&$("hold-charge").checked&&!state.released) {
          const holdAt=settings.duration*(settings.contact-0.17);
          state.time=Math.min(state.time,holdAt);
          if(state.time===holdAt) label("持续蓄力中");
        }
      }
      render();
      if(state.time>=settings.duration) {finish("播放完成");return;}
    }
    if(state.playing) state.raf=requestAnimationFrame(tick);
  }
  function play(keepQueue=false) {
    if(!state.ready) return;cancelFrame();if(!keepQueue) state.queue=[];
    Object.assign(state,{time:0,energyTime:0,playing:true,finished:false,released:false,cancelled:false,cancelAge:0,queueDelay:0});
    render();label(preset().name);updateButtons();ensureFrame();
  }
  function finish(text) {
    state.finished=true;render();label(text);
    if(state.queue.length) {state.queueDelay=0.45;ensureFrame();}
    else if($("loop").checked&&!state.cancelled) {state.queueDelay=0.65;ensureFrame();}
    else {state.playing=false;updateButtons();}
  }
  function stop() {cancelFrame();Object.assign(state,{playing:false,finished:false,time:0,energyTime:0,queue:[],queueDelay:0,cancelled:false,cancelAge:0,released:false});resetVisuals();$("seek").value=0;$("time").textContent=`0.00 / ${settings.duration.toFixed(2)} s`;label("已复位");updateButtons();}
  function advanceDemo() {
    if(state.queue.length) {const next=state.queue.shift();choosePreset(next,true);}
    else play(true);
  }
  function choosePreset(id,auto=true) {
    if(selected()) targetMemory.set(targetKey(),$("target").value);
    const oldQueue=state.queue;stop();state.queue=oldQueue;state.preset=id;
    state[isStatus()?"lastStatus":"lastAttack"]=id;rebuildTargets();
    settings.duration=preset().seconds;settings.contact=preset().contact??0.44;$("duration").value=settings.duration;$("contact").value=Math.round(settings.contact*100);
    for(const btn of document.querySelectorAll("[data-preset]")) btn.setAttribute("aria-pressed",String(btn.dataset.preset===id));
    for(const btn of document.querySelectorAll("[data-mode]")) btn.setAttribute("aria-pressed",String(btn.dataset.mode===(isStatus()?"status":"attack")));
    $("presets").hidden=isStatus();$("status-presets").hidden=!isStatus();
    $("description").textContent=preset().description;$("charge-controls").hidden=!isCharge();
    updateSettings();updateParticipants();fit();if(auto) play(true);
  }
  function updateSettings() {
    $("duration-value").textContent=settings.duration.toFixed(2)+" s";$("strength-value").textContent=Math.round(settings.strength*100)+"%";$("tilt-value").textContent=settings.tilt+"°";$("contact-value").textContent=Math.round(settings.contact*100)+"%";$("gap-value").textContent=settings.gap+" px";
    $("time").textContent=`${state.time.toFixed(2)} / ${settings.duration.toFixed(2)} s`;
  }
  function fit() {const box=$("viewport").getBoundingClientRect();document.documentElement.style.setProperty("--scale",Math.min(box.width/1920,box.height/1080));}
  function clean(value) {document.body.classList.toggle("clean",value);$("exit-clean").hidden=!value;$("clean-view").setAttribute("aria-pressed",String(value));fit();}
  function bind() {
    for(const p of [...M.presets,...S.presets]) {const b=document.createElement("button");b.type="button";b.dataset.preset=p.id;b.textContent=p.name;b.title=p.description;b.setAttribute("aria-pressed",String(p.id===state.preset));b.onclick=()=>{state.queue=[];choosePreset(p.id);};$(p.relation?"status-presets":"presets").append(b);}
    for(const btn of document.querySelectorAll("[data-mode]")) btn.onclick=()=>{state.queue=[];choosePreset(btn.dataset.mode==="status"?state.lastStatus:state.lastAttack);};
    $("play").onclick=()=>play();$("stop").onclick=stop;
    $("pause").onclick=()=>{if(state.finished)return;state.playing=!state.playing;if(!state.playing)cancelFrame();else ensureFrame();label(state.playing?preset().name:"已暂停");updateButtons();};
    $("demo").onclick=()=>{ $("hold-charge").checked=false;state.queue=isStatus()?S.presets.map(p=>p.id):["lean","heavy","lunge","target-lunge","ranged","charge","enemycharge","combo","enter","exit"];choosePreset(state.queue.shift());};
    $("source").onchange=()=>changeParticipants(true);$("target").onchange=()=>changeParticipants(false);
    $("settings-toggle").onclick=()=>{const show=$("settings").hidden;$("settings").hidden=!show;$("settings-toggle").setAttribute("aria-expanded",String(show));fit();};
    $("clean-view").onclick=()=>clean(true);$("exit-clean").onclick=()=>clean(false);
    $("seek").oninput=()=>{cancelFrame();state.playing=false;state.queue=[];state.queueDelay=0;state.finished=false;state.cancelled=false;state.time=settings.duration*Number($("seek").value)/1000;state.energyTime=state.time;render();label("进度预览");updateButtons();};
    for(const key of ["duration","strength","tilt","contact","gap"]) $(key).oninput=()=>{
      const progress=state.time/settings.duration;settings[key]=Number($(key).value)/(key==="strength"||key==="contact"?100:1);state.time=settings.duration*progress;updateSettings();if(state.time>0)render();
    };
    for(const id of ["hit-effects","hit-reaction","screen-shake","damage-numbers"]) $(id).onchange=()=>{if(state.time>0) render();};
    $("release-charge").onclick=()=>{if(!isCharge())return;state.cancelled=false;state.released=true;state.time=settings.duration*(settings.contact-0.13);state.playing=true;label("释放蓄力");updateButtons();ensureFrame();};
    $("cancel-charge").onclick=()=>{if(!isCharge())return;state.queue=[];state.cancelled=true;state.cancelAge=0;state.playing=true;label("打断蓄力");ensureFrame();updateButtons();};
    $("reset").onclick=()=>{stop();settings.strength=1;settings.tilt=8;settings.gap=18;$("strength").value=100;$("tilt").value=8;$("gap").value=18;for(const id of ["hit-effects","hit-reaction","screen-shake","damage-numbers"])$(id).checked=true;$("hold-charge").checked=true;choosePreset(state.preset,false);};
    document.addEventListener("keydown",e=>{
      if(e.repeat||e.ctrlKey||e.metaKey||e.altKey||/INPUT|SELECT|TEXTAREA/.test(e.target.tagName))return;
      if(e.code==="Space"){e.preventDefault();if(state.finished)play();else $("pause").click();}
      else if(e.code==="KeyR"){e.preventDefault();play();}
      else if(e.code==="Escape"){e.preventDefault();if(document.body.classList.contains("clean"))clean(false);else stop();}
    });
    document.addEventListener("visibilitychange",()=>{if(document.hidden&&state.playing){state.playing=false;cancelFrame();label("页面隐藏，已暂停");updateButtons();}});
    window.addEventListener("pagehide",()=>{cancelFrame();state.playing=false;resetVisuals();});
    new ResizeObserver(fit).observe($("viewport"));
  }
  async function init() {
    buildField();bind();choosePreset(state.preset,false);fit();
    const failures=[];
    await Promise.all([...document.images].filter(e=>e.hasAttribute("src")).map(async e=>{try{await e.decode();}catch{failures.push(e.getAttribute("src"));}}));
    if(failures.length){$("load-status").textContent="素材加载失败："+[...new Set(failures)].join("、");return;}
    state.ready=true;$("load-status").hidden=true;for(const id of ["play","pause","stop","demo"])$(id).disabled=false;label("选择动作后播放");updateButtons();
  }
  // Read-only diagnostics for preview verification; no battle or inventory API.
  window.CardAnimationPreview=Object.freeze({getSnapshot:()=>({preset:state.preset,time:state.time,energyTime:state.energyTime,playing:state.playing,ready:state.ready,finished:state.finished,queue:state.queue.length,source:$("source").value,target:$("target").value,targets:targets().map(c=>c.key),relation:relation(),chargeHolding:isCharge()&&$("hold-charge").checked&&!state.released&&!state.cancelled,settings:{...settings}})});
  init().catch(e=>{$("load-status").textContent="预览未就绪："+e.message;console.error(e);});
})();
