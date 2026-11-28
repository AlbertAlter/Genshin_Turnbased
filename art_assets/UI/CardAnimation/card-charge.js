/* Independent charge renderer: the same input works for a short wind-up or a
 * multi-turn hold. The caller owns its clock, release, interrupt and cleanup. */
(() => {
  "use strict";
  const {clamp,smooth}=window.CardMotion;
  // Eight staggered lanes begin just beyond all four card edges. Each lane
  // has its own period and fades before its head reaches the center.
  const lanes=Object.freeze([
    [-0.28,-0.55,0.12,0.94], [0.29,-0.54,-0.11,1.13],
    [0.58,-0.24,0.14,1.04], [0.57,0.28,-0.13,0.88],
    [0.27,0.55,0.12,1.17], [-0.28,0.55,-0.10,0.98],
    [-0.58,0.23,0.13,1.08], [-0.57,-0.27,-0.12,0.91]
  ]);
  function draw(ctx,{x,y,width=197,height=338,elapsed=0,phase="charging",phaseAge=0,strength=1}) {
    const fade=phase==="cancel"?1-smooth(phaseAge/0.24):phase==="release"?1-smooth(phaseAge/0.2):1;
    // Elapsed time only drives looping motion. It is not a charge meter.
    const intensity=fade*smooth(elapsed/0.2)*Math.min(1.5,Math.max(0,strength));
    if(intensity<=0||strength<=0) return 0;
    ctx.save();ctx.translate(x,y);ctx.globalCompositeOperation="lighter";
    ctx.lineCap="round";ctx.shadowColor="#efc98c";ctx.shadowBlur=3;
    for(let i=0;i<lanes.length;i++) {
      const [nx,ny,bend,period]=lanes[i];
      const travel=(elapsed/period+i*0.38196601125)%1;
      const head=travel+(phase==="release"?clamp(phaseAge/0.2)*0.25:0);
      const alpha=intensity*smooth(travel/0.09)*(1-smooth((head-0.72)/0.24));
      if(alpha<=0) continue;
      const sx=nx*width,sy=ny*height;
      const cx=sx*0.45-sy*bend,cy=sy*0.45+sx*bend;
      // A quadratic curve from the edge to the actual card center.
      const at=t=>[(1-t)*(1-t)*sx+2*(1-t)*t*cx,(1-t)*(1-t)*sy+2*(1-t)*t*cy];
      const tail=Math.max(0,head-0.36),steps=16;
      for(let j=0;j<steps;j++) {
        const u=j/steps,v=(j+1)/steps;
        const a=at(tail+(head-tail)*u),b=at(tail+(head-tail)*v);
        ctx.lineWidth=0.3+Math.pow(v,1.6)*(i%3===0?2.5:1.9);
        ctx.strokeStyle=`rgba(255,224,166,${alpha*Math.pow(v,1.1)*0.95})`;
        ctx.beginPath();ctx.moveTo(...a);ctx.lineTo(...b);ctx.stroke();
      }
      const point=at(head);
      ctx.fillStyle=`rgba(255,236,198,${alpha*0.85})`;
      ctx.beginPath();ctx.arc(point[0],point[1],1.45,0,Math.PI*2);ctx.fill();
    }
    ctx.restore();return intensity;
  }
  window.CardCharge=Object.freeze({draw});
})();
