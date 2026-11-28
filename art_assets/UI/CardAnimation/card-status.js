/* One shared status-application motion. The preview host owns time and cleanup. */
(() => {
  "use strict";
  const presets=Object.freeze([Object.freeze({id:"status-cast",relation:"self",style:"cast",name:"施法抬起 · 三重光环",seconds:1.1,contact:0.44,description:"仅施加者抬起并轻微放大，三重光环在卡牌下半部逐层悬起，然后回正；不区分状态目标。"})]);
  const sourcePose=(style,t,options)=>window.CardMotion.sample("cast",t,options);
  // Horizontal circular ribbons at three heights, projected through a finite camera.
  // Rear and front halves have separate mounts so the card can occlude the rear.
  function drawHalo(ctx,{width=197,height=338,progress=0,strength=1,backHalf=false}) {
    ctx.clearRect(0,0,ctx.canvas.width,ctx.canvas.height);
    const t=window.CardMotion.clamp(progress);
    const alpha=Math.sin(t*Math.PI)*Math.min(1,0.72*Math.min(1.5,Math.max(0,strength)));
    if(t<=0||t>=1||alpha<=0||width<=0||height<=0) return;
    const unit=width/197, focal=width*2.85;
    const tilt=70*Math.PI/180, sinTilt=Math.sin(tilt), cosTilt=Math.cos(tilt);
    const radius=width*0.64*(0.66+0.44*window.CardMotion.smooth(t));
    const centerX=ctx.canvas.width/2, centerY=ctx.canvas.height/2+height*0.46;
    function project(x,z,elevation) {
      const scale=focal/(focal+z*sinTilt-elevation*cosTilt);
      return [centerX+x*scale,centerY-(z*cosTilt+elevation*sinTilt)*scale];
    }
    function lighting(r,elevation) {
      const top=project(0,r,elevation)[1], bottom=project(0,-r,elevation)[1];
      const light=ctx.createLinearGradient(0,top,0,bottom);
      for(let step=0;step<=16;step++) {
        const near=step/16,z=r*(1-2*near);
        light.addColorStop((project(0,z,elevation)[1]-top)/(bottom-top),`rgba(255,231,174,${0.34+0.66*Math.pow(near,1.3)})`);
      }
      return light;
    }
    const begin=backHalf?0:Math.PI;
    function band(r,thickness,opacity,elevation) {
      ctx.beginPath();
      for(let direction=0;direction<2;direction++) {
        const edge=r+(direction===0?1:-1)*thickness/2;
        for(let step=0;step<=64;step++) {
          const angle=begin+Math.PI*(direction===0?step/64:1-step/64);
          const point=project(Math.cos(angle)*edge,Math.sin(angle)*edge,elevation);
          if(direction===0&&step===0) ctx.moveTo(...point);else ctx.lineTo(...point);
        }
      }
      ctx.closePath();ctx.globalAlpha=opacity;ctx.fill();
    }
    const spread=window.CardMotion.smooth(t/0.3);
    const lift=height*(0.025+0.03*Math.sin(t*Math.PI));
    ctx.save();
    for(let ring=0;ring<3;ring++) {
      const r=radius*(1-ring*0.1), brightness=alpha*(0.9+ring*0.05);
      const elevation=lift+ring*height*0.11*spread;
      ctx.fillStyle=lighting(r,elevation);
      band(r,18*unit,brightness*0.09,elevation);
      band(r,10*unit,brightness*0.2,elevation);
      band(r,5.5*unit,brightness,elevation);
    }
    ctx.restore();
  }
  window.CardStatus=Object.freeze({presets,sourcePose,drawHalo});
})();
