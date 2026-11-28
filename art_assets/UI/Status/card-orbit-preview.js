"use strict";
(() => {
  const definitions = [
    ["ArrowCount",6,1,12,1,"箭头组数"], ["ChevronsPerArrow",2,1,3,1,"每组箭头数"],
    ["TravelSeconds",.75,.1,5,.05,"单箭头移动时间 / 秒"], ["SpawnStaggerSeconds",.045,0,.5,.005,"逐组错开 / 秒"],
    ["SpreadWidthRatio",1.4,.2,2.4,.01,"横向分布 / 卡宽"], ["TravelHeightRatio",.8,.1,1.5,.01,"移动距离 / 卡高"],
    ["ArrowWidthRatio",.16,.02,.5,.01,"箭头宽 / 卡宽"], ["ArrowHeightRatio",.037,.005,.15,.001,"箭头高 / 卡高"],
    ["ChevronGapRatio",.033,.005,.2,.001,"双箭头间距 / 卡高"], ["TrailLengthRatio",.13,0,.5,.01,"拖尾长 / 卡高"],
    ["GlowScale",1.85,1,4,.05,"外光倍率"], ["GlowOpacity",.28,0,1,.01,"外光不透明度"],
    ["Opacity",.92,0,1,.01,"整体不透明度"], ["FadeInSeconds",.1,0,3,.01,"整组淡入 / 秒"],
    ["FadeOutSeconds",.16,0,3,.01,"停止淡出 / 秒"], ["DurationSeconds",1,.1,5,.1,"单次时长 / 秒"],
    ["CoreWhiteMix",.8,0,1,.01,"白色核心混合"]
  ];
  const defaults = Object.fromEntries(definitions.map(d => [d[0],d[1]]));
  defaults.BuffColor = "#FFC14F"; defaults.DebuffColor = "#B69CFF";
  let parameters = {...defaults};
  const $ = s => document.querySelector(s), controls = new Map();
  const canvases = [$("#orbit-behind"), $("#orbit-front")], contexts = canvases.map(c => c.getContext("2d"));
  let age = 0, running = false, paused = false, stopping = false, stopAge = 0, stopAlpha = 1, last = null;
  const clamp = x => Math.max(0,Math.min(1,x));
  const smooth = x => (x=clamp(x))*x*(3-2*x);
  function validate(values) {
    if (Object.keys(values).length !== Object.keys(defaults).length) throw Error("状态箭头参数字段数量不正确。");
    for (const [key,,min,max,step] of definitions) {
      const n=values[key]; if (!Number.isFinite(n) || n<min || n>max || step===1 && n!==Math.round(n)) throw Error("状态箭头参数超出范围："+key);
    }
    for (const key of ["BuffColor","DebuffColor"]) if (!/^#[0-9a-f]{6}$/i.test(values[key])) throw Error(key+" 必须为 #RRGGBB。");
    return {...values};
  }
  function changed() {
    document.dispatchEvent(new Event("orbit-preview-change"));
    try {localStorage.setItem("card-status-arrow-preview-v2",JSON.stringify(parameters));} catch {}
  }
  function sync() {for (const [key,inputs] of controls) for (const input of inputs) input.value=parameters[key];}
  for (const [key,value,min,max,step,name] of definitions) {
    const box=document.createElement("div"); box.className="setting";
    const label=document.createElement("label");label.textContent=name;
    const row=document.createElement("div");row.className="setting-row";
    const range=document.createElement("input"), number=document.createElement("input");
    range.type="range";number.type="number";
    for (const input of [range,number]) {input.min=min;input.max=max;input.step=step;input.value=value;input.setAttribute("aria-label",name);}
    controls.set(key,[range,number]);
    for (const input of [range,number]) input.addEventListener("input",()=>{
      try {parameters=validate({...parameters,[key]:Number(input.value)});sync();changed();} catch(error) {input.setAttribute("aria-invalid","true");$("#orbit-excel-status").textContent=error.message;return;}
      input.removeAttribute("aria-invalid");
    });
    row.append(range,number);box.append(label,row);$("#orbit-settings").append(box);
  }
  for (const [key,name] of [["BuffColor","Buff 颜色"],["DebuffColor","Debuff 颜色"]]) {
    const label=document.createElement("label");label.className="color-setting";label.textContent=name;
    const input=document.createElement("input");input.type="color";input.value=parameters[key];input.setAttribute("aria-label",name);
    input.addEventListener("input",()=>{parameters[key]=input.value.toUpperCase();changed();});
    controls.set(key,[input]);label.append(input);$("#orbit-colors").append(label);
  }
  function alpha() {return stopping ? stopAlpha*clamp(1-stopAge/Math.max(.001,parameters.FadeOutSeconds)) : parameters.FadeInSeconds ? clamp(age/parameters.FadeInSeconds) : 1;}
  function play() {age=stopAge=0;running=true;paused=stopping=false;last=null;$("#orbit-pause").textContent="暂停";}
  function stop() {if (!running || stopping) return;stopAlpha=alpha();stopping=true;stopAge=0;if (!parameters.FadeOutSeconds) running=false;}
  $("#orbit-play").addEventListener("click",()=>{$("#play").click();});
  document.addEventListener("status-preview-applied",play);
  $("#still").addEventListener("click",stop);
  $("#orbit-stop").addEventListener("click",stop);
  $("#orbit-pause").addEventListener("click",()=>{paused=!paused;$("#orbit-pause").textContent=paused?"继续":"暂停";});
  $("#orbit-mode").addEventListener("change",()=>{
    const text=$("#status-text");
    if(["攻击力提升","攻击力降低"].includes(text.value)) {
      text.value=$("#orbit-mode").value==="Buff"?"攻击力提升":"攻击力降低";text.dispatchEvent(new Event("input"));
    }
    running=false;
  });
  document.addEventListener("keydown",e=>{if(e.key==="Escape")stop();});
  document.addEventListener("visibilitychange",()=>{last=null;});
  function sample(index,time,width,height,top) {
    const p=parameters,phase=(time-index*p.SpawnStaggerSeconds)/p.TravelSeconds;
    const seat=p.ArrowCount===1?.5:index/(p.ArrowCount-1),depth=index%2===0?1:-1;
    const vertical=$("#orbit-mode").value==="Buff"?clamp(phase):1-clamp(phase);
    return {x:480+(seat-.5)*width*p.SpreadWidthRatio,y:top+height*(.5-(vertical-.5)*p.TravelHeightRatio),
      phase,depth,scale:1,opacity:smooth(phase/.15)*smooth((1-phase)/.15)};
  }
  const weight=(depth,front)=>front?smooth((depth+.12)/.24):1-smooth((depth+.12)/.24);
  const rgb=hex=>[1,3,5].map(i=>parseInt(hex.slice(i,i+2),16));
  const rgba=(c,a)=>`rgba(${c.join(",")},${clamp(a)})`;
  function path(ctx,points,scale=1) {ctx.beginPath();points.forEach(([x,y],i)=>i?ctx.lineTo(x*scale,y*scale):ctx.moveTo(x*scale,y*scale));ctx.closePath();}
  function draw(ctx,front) {
    ctx.clearRect(0,0,960,720);if (!running) return;
    const p=parameters,card=window.StatusPopupPreview.getParameters(),h=card.cardHeight,w=h*7/12,top=589-h;
    const sign=$("#orbit-mode").value==="Buff"?1:-1;
    const tint=rgb(sign>0?p.BuffColor:p.DebuffColor),core=tint.map(c=>Math.round(c+(255-c)*p.CoreWhiteMix)),opacity=alpha()*p.Opacity;
    for(let i=0;i<p.ArrowCount;i++) {
      const head=sample(i,age,w,h,top);
      if(p.TrailLengthRatio>0) for(let j=1;j<=12;j++) {
        const t=j/12,previous=(j-1)/12;
        const a=sample(i,age-(1-previous)*p.TrailLengthRatio*p.TravelSeconds/p.TravelHeightRatio,w,h,top);
        const b=sample(i,age-(1-t)*p.TrailLengthRatio*p.TravelSeconds/p.TravelHeightRatio,w,h,top);
        ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);
        ctx.strokeStyle=rgba(tint,opacity*b.opacity*head.opacity*weight(b.depth,front)*t*t*.45);ctx.lineWidth=w*.01*head.scale*t;
        ctx.shadowColor=rgba(tint,.35);ctx.shadowBlur=w*.012;ctx.stroke();ctx.shadowBlur=0;
      }
      const a=head.opacity*weight(head.depth,front)*(.65+.35*(head.depth+1)*.5)*opacity;if(a<.001)continue;
      for(let n=0;n<p.ChevronsPerArrow;n++) {
        const width=w*p.ArrowWidthRatio*head.scale,height=h*p.ArrowHeightRatio;
        const points=[[-.5*width,.5*height*sign],[0,-.5*height*sign],[.5*width,.5*height*sign],
          [.28*width,.5*height*sign],[0,-.08*height*sign],[-.28*width,.5*height*sign]];
        ctx.save();ctx.translate(head.x,head.y+sign*n*h*p.ChevronGapRatio);
        const strength=a*(1-n*.22);
        // Feathered glow is independent from the sharp double-chevron core.
        ctx.shadowColor=rgba(tint,strength*p.GlowOpacity);ctx.shadowBlur=width*(p.GlowScale-1)*.8;
        path(ctx,points);ctx.fillStyle=rgba(tint,strength*.35);ctx.fill();ctx.shadowBlur=0;
        const gradient=ctx.createLinearGradient(0,.5*height*sign,0,-.5*height*sign);
        gradient.addColorStop(0,rgba(tint,strength));gradient.addColorStop(1,rgba(core,strength));
        path(ctx,points);ctx.fillStyle=gradient;ctx.fill();ctx.restore();
      }
    }
  }
  function advance(delta) {
    if(running&&(!paused||stopping)) {
      age+=delta;
      if(stopping)stopAge+=delta;
      else if(age>=parameters.DurationSeconds){stop();stopAge=age-parameters.DurationSeconds;}
    }
    if(stopping&&stopAge>=parameters.FadeOutSeconds)running=false;
  }
  function frame(time) {
    const delta=last===null?0:Math.max(0,(time-last)/1000);last=time;
    if(!document.hidden)advance(delta);
    draw(contexts[0],false);draw(contexts[1],true);requestAnimationFrame(frame);
  }
  try {const saved=JSON.parse(localStorage.getItem("card-status-arrow-preview-v2"));if(saved)parameters=validate(saved);}catch{}
  sync();
  const api={getParameters:()=>({...parameters}),validateParameters:validate,setParameters(values){parameters=validate(values);sync();changed();}};
  window.CardOrbitPreview=api;
  window.StatusParameterWorkbook.mount({fields:[...definitions.map(d=>[d[0],d[0],1,"",d[5],"number"]),
    ["BuffColor","BuffColor",1,"","Buff颜色","text"],["DebuffColor","DebuffColor",1,"","Debuff颜色","text"]],
    api,prefix:"orbit-excel",event:"orbit-preview-change",storage:"card-orbit-parameter-file-charts-v1",picker:"card-orbit-charts"});
  requestAnimationFrame(frame);
})();
