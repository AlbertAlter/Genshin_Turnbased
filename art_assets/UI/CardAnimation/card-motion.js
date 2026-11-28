/* Shared, time-sampled card motion for this HTML preview. No battle state or timers.
 * Ratios refer to the real card width/height. Unity integration is a later step.
 * Shake uses HitShakeMath's damped cosine and the current HitShake.xlsx defaults. */
(() => {
  "use strict";
  const presets = Object.freeze([
    {id:"lean", name:"攻击前倾", seconds:0.64, description:"脚部为支点，轻微后收 → 向目标前倾 → 回正。"},
    {id:"heavy", name:"重击释放", seconds:0.85, description:"向目标压下，命中后回正；蓄力由独立聚能特效负责。"},
    {id:"charge", name:"重击蓄力", seconds:2.2, contact:0.68, description:"蓄力状态：8 条细动线从卡牌四周向中心收束，持续循环；手动释放或移除。"},
    {id:"enemycharge", name:"怪物蓄力", seconds:3.6, contact:0.7, description:"怪物共用卡牌中心聚能动线，持续存在时卡面保持原位，移除时仅淡出特效。"},
    {id:"lunge", name:"突进 · 固定距离", seconds:0.78, description:"保留原版：移动卡宽的 72%，只按目标左右决定方向，命中后归位。"},
    {id:"target-lunge", name:"突进 · 到目标", seconds:0.78, contact:0.44, description:"沿用固定距离版的快出快收节奏，斜向突进到目标边缘再归位；群攻朝最近目标突进。"},
    {id:"ranged", name:"远程攻击", seconds:0.95, description:"起手轻抬卡面，飞行物到达后衔接受击。"},
    {id:"combo", name:"三段连击", seconds:1.45, description:"连续三次前倾，最后一段更强；每段各有命中与受击。"},
    {id:"hit", name:"轻受击", seconds:0.55, description:"目标卡牌微抖，使用项目 CardNormal 的幅度和频率。"},
    {id:"recoil", name:"重受击", seconds:0.8, description:"目标向后倾斜、退开，再回正；叠加项目 CardHeavy 抖动。"},
    {id:"enter", name:"入场", seconds:0.8, description:"卡牌从下方升起，淡入后轻轻落定。"},
    {id:"exit", name:"退场", seconds:0.9, description:"卡牌后退、倾倒并淡出；演示结束后恢复，方便重播。"}
  ]);
  const profiles = Object.freeze({
    normal:{duration:0.14,x:0.012,y:0.006,rotation:0.45,frequency:28,decay:2,maxTranslation:0.03,maxRotation:1.2},
    heavy:{duration:0.2,x:0.02,y:0.009,rotation:0.8,frequency:24,decay:2,maxTranslation:0.05,maxRotation:1.8},
    screen:{duration:0.2,x:0.004,y:0.003,rotation:0,frequency:25,decay:2,maxTranslation:0.008,maxRotation:0}
  });
  const clamp = (n,a=0,b=1) => Math.min(b,Math.max(a,n));
  const smooth = n => { n=clamp(n); return n*n*(3-2*n); };
  const pose = () => ({x:0,y:0,z:0,rx:0,ry:0,rz:0,sx:1,sy:1,alpha:1,glow:0});
  function track(t,keys) {
    if(t<=keys[0][0]) return keys[0][1];
    for(let i=1;i<keys.length;i++) if(t<=keys[i][0]) {
      const [a,x]=keys[i-1], [b,y]=keys[i];
      return x+(y-x)*smooth((t-a)/(b-a));
    }
    return keys[keys.length-1][1];
  }
  function attackPulse(t,contact) {
    return track(t,[[0,0],[contact*0.52,-0.2],[contact,1],[Math.min(0.95,contact+0.2),0.12],[1,0]]);
  }
  // Swept axis-aligned card bounds: reach the near edge of the target's
  // expanded rectangle, rather than moving to its center or through it.
  function approachOffset(from,to,gap=18,paddingX=0,paddingY=0) {
    if(!from||!to) return {x:0,y:0};
    const dx=to.x-from.x,dy=to.y-from.y;
    const halfX=(from.width+to.width)/2+Math.max(0,gap)+paddingX;
    const halfY=(from.height+to.height)/2+Math.max(0,gap)+paddingY;
    const entryX=Math.abs(dx)>0.0001?1-halfX/Math.abs(dx):-Infinity;
    const entryY=Math.abs(dy)>0.0001?1-halfY/Math.abs(dy):-Infinity;
    const travel=clamp(Math.max(entryX,entryY));
    return {x:dx*travel,y:dy*travel};
  }
  function sample(id,t,options={}) {
    const p=pose(), s=options.strength??1, d=options.direction??-1, w=options.width??197, h=options.height??338;
    const angle=options.tilt??8, c=clamp(options.contact??0.44,0.25,0.7);
    t=clamp(t);
    if(id==="target-lunge") {
      const progress=track(t,[[0,0],[c*0.52,0],[c,1],[Math.min(0.95,c+0.2),0.12],[1,0]]);
      const maxTilt=angle*0.45*s, radians=Math.abs(maxTilt)*Math.PI/180;
      // Reserve room for the leaning card's corners as well as its base.
      const offset=approachOffset(options.sourceRect,options.targetRect,options.gap??18,h*Math.sin(radians),w*Math.sin(radians));
      const distanceScale=clamp(s);
      p.x=offset.x*progress*distanceScale;
      p.y=offset.y*progress*distanceScale-h*0.035*Math.sin(Math.PI*clamp(t/c))*s;
      p.rz=d*maxTilt*progress;
    } else if(id==="charge"||id==="enemycharge") {
      const start=c-0.13;
      if(t>start) return sample("heavy",clamp((t-start)/(1-start)),{...options,contact:(c-start)/(1-start)});
    } else if(id==="lean"||id==="heavy"||id==="lunge"||id==="ranged"||id==="combo") {
      const heavy=id==="heavy";
      const power=heavy?track(t,[[0,0],[c*0.65,0],[c,1],[Math.min(0.95,c+0.2),0.12],[1,0]]):attackPulse(t,c);
      const a=heavy?power*1.45:power;
      p.rz=d*angle*a*s; p.ry=-d*3*power*s; p.rx=-Math.abs(power)*2.5*s;
      p.x=d*w*(id==="lunge"?0.72:heavy?0.11:0.065)*power*s;
      p.y=-h*(id==="lunge"?0.045:0.012)*Math.max(0,power)*s;
      p.sx=p.sy=1+Math.max(0,power)*0.025*s;
      if(id==="ranged") {p.rz*=0.4;p.y-=h*0.025*Math.max(0,power)*s;}
    } else if(id==="cast") {
      const v=track(t,[[0,0],[0.3,1],[0.65,0.8],[1,0]]);
      p.y=-h*0.065*v*s;p.rx=-4*v*s;p.sx=p.sy=1+0.045*v*s;p.glow=v*0.5;
    } else if(id==="enter") {
      const v=track(t,[[0,1],[0.64,-0.06],[1,0]]);
      p.y=h*0.2*v*s;p.rx=8*v*s;p.sx=p.sy=1-0.1*v*s;p.alpha=smooth(t/0.28);
    } else if(id==="exit") {
      const v=smooth(t);p.x=d*w*0.13*v*s;p.y=h*0.08*v*s;p.rx=15*v*s;p.rz=d*angle*v*s;p.sx=p.sy=1-0.1*v*s;p.alpha=1-smooth((t-0.35)/0.65);
    }
    return p;
  }
  // Same visual hash as HitEffectMath.Unit. Never consumes a gameplay random stream.
  function mix(x) {x>>>=0;x^=x>>>16;x=Math.imul(x,0x7feb352d);x^=x>>>15;x=Math.imul(x,0x846ca68b);return (x^(x>>>16))>>>0;}
  function unit(seed,channel) {return (mix((seed^mix((channel+0x51ed270b)>>>0))>>>0)>>>8)/16777216;}
  function shake(kind,age,w,h,seed=517,strength=1) {
    const p=pose(), a=profiles[kind];
    if(!a||age<0||age>=a.duration) return p;
    const env=Math.pow(1-age/a.duration,a.decay)*strength, wave=age*a.frequency*2*Math.PI;
    const osc=c=>Math.cos(wave*(c===2?1.19:1)+unit(seed,c+900)*2*Math.PI);
    p.x=osc(1)*a.x*w*env;p.y=osc(2)*a.y*h*env;p.rz=osc(3)*a.rotation*env;
    const length=Math.hypot(p.x,p.y), limit=Math.min(w,h)*a.maxTranslation;
    if(length>limit) {p.x*=limit/length;p.y*=limit/length;}
    p.rz=clamp(p.rz,-a.maxRotation,a.maxRotation);return p;
  }
  function reaction(age,{heavy=false,direction=-1,strength=1,width=197,height=338,seed=517}={}) {
    const p=shake(heavy?"heavy":"normal",age,width,height,seed,strength);
    if(heavy&&age>=0) {
      const v=track(age,[[0,0],[0.065,1],[0.15,0.6],[0.36,0]]);
      p.x+=direction*width*0.09*v*strength;p.rz+=direction*4*v*strength;p.y+=height*0.007*v*strength;
    }
    return p;
  }
  function transform(p) {return `translate3d(${p.x}px,${p.y}px,${p.z}px) rotateX(${p.rx}deg) rotateY(${p.ry}deg) rotateZ(${p.rz}deg) scale(${p.sx},${p.sy})`;}
  window.CardMotion=Object.freeze({presets,profiles,pose,sample,shake,reaction,transform,approachOffset,clamp,smooth,track});
})();
