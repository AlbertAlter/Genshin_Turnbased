/* HTML-only sample snapshots, not a second reaction resolver.
 * Result fields mirror the current C# events. Values are demonstration inputs.
 * Colors are the current Charts/UI/ElementColors.xlsx Colors snapshot.
 * Element glyphs use the same ElementSVG source as existing character pages.
 */
(() => {
  "use strict";
  const palette = {
    Pyro:{name:"火",color:"#FFA971"}, Hydro:{name:"水",color:"#08E4FF"},
    Electro:{name:"雷",color:"#DEBAFF"}, Cryo:{name:"冰",color:"#CFFFFF"},
    Anemo:{name:"风",color:"#A6F7CF"}, Geo:{name:"岩",color:"#F5D761"},
    Dendro:{name:"草",color:"#B1EB29"}, Physical:{name:"物理",color:"#FEFEFE"}
  };
  for (const [element, style] of Object.entries(palette)) {
    style.icon = element === "Physical" ? null : "../../ElementSVG/Element_" + element + ".svg";
    Object.freeze(style);
  }
  const pool = (Element, Amount = 1, Kind = "NormalAura") => Kind === "Intrinsic" ?
    {Element, Kind, IsIntrinsic:true} : {Element, Amount, Kind, IsIntrinsic:false};
  const reaction = (Reaction, DisplayName, Elements) => ({Reaction, DisplayName, Elements});
  const step = (Element, after, Reaction = null, DisplayName = "", other = null) => ({Element, after,
    reactions:Reaction ? [reaction(Reaction, DisplayName, [other, Element].filter(Boolean))] : []});
  const cases = [
    {id:"vaporize",name:"蒸发",description:"水火图标碰撞，消耗完成后一起消失。",before:[pool("Hydro")],
      steps:[step("Pyro",[],"Vaporize","蒸发","Hydro")]},
    {id:"residual",name:"蒸发 · 残留",description:"发生反应后，剩余的水元素归回原位。",before:[pool("Hydro",2)],
      steps:[step("Pyro",[pool("Hydro",1.5)],"Vaporize","蒸发","Hydro")]},
    {id:"melt",name:"融化",description:"冰与火发生反应，按结果消耗并收尾。",before:[pool("Cryo")],
      steps:[step("Pyro",[],"Melt","融化","Cryo")]},
    {id:"overloaded",name:"超载",description:"雷火图标短促闪亮，再淡出。",before:[pool("Electro")],
      steps:[step("Pyro",[],"Overloaded","超载","Electro")]},
    {id:"superconduct",name:"超导",description:"同一套动效使用实际参与反应的冰、雷元素色。",before:[pool("Cryo")],
      steps:[step("Electro",[],"Superconduct","超导","Cryo")]},
    {id:"charged",name:"感电",description:"反应结束后保留水雷共存；后续周期结算再次消费对应结果。",before:[pool("Hydro",2)],
      steps:[step("Electro",[pool("Hydro"),pool("Electro")],"ElectroCharged","感电","Hydro")]},
    {id:"frozen",name:"冻结",description:"冻结的虚拟冰元素池保留在结果中，普通元素栏不增加冰图标。",before:[pool("Hydro")],
      steps:[step("Cryo",[pool("Cryo",1,"Frozen")],"Frozen","冻结","Hydro")]},
    {id:"quicken",name:"激化 → 超激化",description:"两次独立结果依次播放，原激化状态不占普通附着栏。",before:[pool("Dendro")],
      steps:[step("Electro",[],"Quicken","原激化","Dendro"),
        step("Electro",[pool("Electro",0.8)],"Aggravate","超激化","Dendro")]},
    {id:"bloom",name:"绽放",description:"展示水草接触的反应，草原核不作为普通附着图标。",before:[pool("Hydro")],
      steps:[step("Dendro",[],"Bloom","绽放","Hydro")]},
    {id:"swirl",name:"扩散",description:"风只参与这次碰撞，剩余水附着仍保留。可切全体目标查看并行。",before:[pool("Hydro")],
      steps:[step("Anemo",[pool("Hydro",0.5)],"Swirl","扩散","Hydro")]},
    {id:"intrinsic",name:"固有元素 · 结晶",description:"固有火元素参与反应后继续保留，不按有限元素量删除。",before:[pool("Pyro",0,"Intrinsic")],
      steps:[step("Geo",[pool("Pyro",0,"Intrinsic")],"Crystallize","结晶","Pyro")]},
    {id:"lunar",name:"月感电",description:"反应名称来自本次结果，水雷共存继续按附着快照显示。",before:[pool("Hydro",1.2)],
      steps:[step("Electro",[pool("Hydro",0.8),pool("Electro",0.8)],"LunarElectroCharged","月感电","Hydro")]},
    {id:"apply",name:"首次附着",description:"没有反应记录时，只播放元素进入与落位。",before:[],steps:[step("Hydro",[pool("Hydro",0.8)])]},
    {id:"refresh",name:"同元素刷新",description:"已有图标原地缩放、闪亮一次。",before:[pool("Hydro",0.8)],
      steps:[step("Hydro",[pool("Hydro",0.8)])]},
    {id:"rapid-refresh",name:"高频同元素",description:"每0.06秒刷新一次，共12次。原图标的缩放与闪光取当前最大值。",before:[pool("Hydro",0.8)],interval:0.06,
      steps:Array.from({length:12},() => step("Hydro",[pool("Hydro",0.8)]))},
    {id:"rapid-reactions",name:"高频反应",description:"每0.1秒一段，共9段。当前逐段排队，可观察碰撞动画的积压。",before:[pool("Hydro")],interval:0.1,
      steps:Array.from({length:3},() => [step("Pyro",[],"Vaporize","蒸发","Hydro"),
        step("Pyro",[pool("Pyro",0.8)]),step("Hydro",[pool("Hydro")],"Vaporize","蒸发","Pyro")]).flat()},
    {id:"sequence",name:"连续多段",description:"同目标依序回放蒸发、重新附着、再次蒸发，每一段保留独立身份。",before:[pool("Hydro")],
      steps:[step("Pyro",[],"Vaporize","蒸发","Hydro"),step("Pyro",[pool("Pyro",0.8)]),
        step("Hydro",[],"Vaporize","蒸发","Pyro")]}
  ];
  window.ElementReactionCases = Object.freeze(cases);
  window.ElementReactionPalette = Object.freeze(palette);
})();
