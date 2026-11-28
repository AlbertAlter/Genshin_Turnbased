using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Public 3D-art playback for card battles. Hosts supply actual target rectangles;
/// this component never reads battle singletons, chooses targets or commits results.
/// The sandbox and formal battle adapters use this same implementation.
/// </summary>
public sealed partial class UIBattleEffectPlayer : MonoBehaviour, IBattleVisualCompletion
{
    public bool automaticClock;
    public bool Paused { get; set; }
    public string Error { get; private set; }
    public bool Playing { get; private set; }
    public bool HasPendingVisuals => Playing && (tablePlayback != null ? !tablePlayback.Sustained : settings != null && !BattleEffectParameters.IsSustained(settings.Kind));
    public float Elapsed { get; private set; }
    public float Duration { get; private set; }
    private RectTransform space, caster;
    private readonly List<RectTransform> targets = new List<RectTransform>();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly Dictionary<Mesh, BattleEffectMeshSource> meshCache = new Dictionary<Mesh, BattleEffectMeshSource>();
    private readonly Dictionary<BattleEffectPart, BattleEffectModelPose> models = new Dictionary<BattleEffectPart, BattleEffectModelPose>();
    private Material material;
    private BattleEffectMeshBatch back, front;
    private BattleEffectParameters settings;
    private BattleEffectRange range;
    private float fade, stoppingTime = -1;
    private int seed;
    private Color tint;
    private bool includeInactiveTargets;
    private float startDelay;
    private BattleEffectHitSlice hitSlice;
    private BattleEffectContactGate contactGate;
    private BattleTableEffectPlayback tablePlayback;
    private BattleTableEffectResource tableResource;

    public bool BindTableEffect(RectTransform coordinateSpace, RectTransform behindCards, RectTransform aboveCards, Shader shader, BattleTableEffectResource resource)
    {
        Release();Error=null;
        if(coordinateSpace==null||behindCards==null||aboveCards==null||shader==null||resource==null||(resource.Player!="Sequence"&&resource.Player!="Sprite"))
        {Error="缺少配表特效空间、挂点、Shader或Sequence资源定义。";return false;}
        tableResource=resource.Copy();tablePlayback=new BattleTableEffectPlayback(coordinateSpace,behindCards,aboveCards,shader);return true;
    }
    private void TableState()
    {Playing=tablePlayback.Playing;Elapsed=tablePlayback.Elapsed;Duration=tablePlayback.Duration;}
    public void SetTableParameters(BattleTableEffectResource value)
    {
        if(tablePlayback==null||value==null||tableResource.Key!=value.Key)throw new InvalidOperationException("不能在同一绑定中更换配表资源键。");
        tableResource=value.Copy();
    }
    public bool TryPlayTable(IReadOnlyList<RectTransform> actualTargets,RectTransform actualCaster=null,float delaySeconds=0,bool gateContact=false,int count=-1,string element=null,IReadOnlyList<int> sourceStatuses=null,IReadOnlyDictionary<string,float> sourceValues=null)
    {
        if(tablePlayback==null){Error="配表特效未绑定。";return false;}
        try{tablePlayback.Play(tableResource,actualTargets,actualCaster,delaySeconds,gateContact,count,element,sourceStatuses,sourceValues);TableState();Error=null;return true;}
        catch(Exception failure){Error=failure.Message;tablePlayback.Clear();Playing=false;return false;}
    }
    public void SetTableVariables(int count,string element=null)
    {if(tablePlayback!=null)RunTable(4,count:count,element:element);}
    public void ReleaseIdleTableResources(){if(tablePlayback!=null&&!Playing)tablePlayback.ReleaseIdleResources();}
    public void PulseTable(float gain,float seconds)=>tablePlayback?.Pulse(gain,seconds);
    private void RunTable(int operation,float seconds=0,int count=-1,string element=null)
    {
        try
        {
            switch(operation){case 1:tablePlayback.Advance(seconds);break;case 2:tablePlayback.ConfirmContact();break;
                case 3:tablePlayback.Seek(seconds);break;case 4:tablePlayback.SetVariables(count,element);break;}
            TableState();
        }
        catch(Exception failure){Error="配表特效播放失败："+failure.Message;tablePlayback.Clear();Playing=false;}
    }

    public bool Bind(RectTransform coordinateSpace, RectTransform behindCards, RectTransform aboveCards, Shader shader, BattleEffectAssets assets)
    {
        Release(); Error = null;
        if (coordinateSpace == null || behindCards == null || aboveCards == null || shader == null || assets == null)
        { Error = "缺少特效空间、前后挂点、Shader或模型绑定。"; return false; }
        try
        {
            space = coordinateSpace;
            var canvas = coordinateSpace.GetComponentInParent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("特效挂点不属于Canvas。");
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            material = new Material(shader) { name = "Card battle 3D effects" };
            defaultBehind = behindCards; defaultAbove = aboveCards;
            back = new BattleEffectMeshBatch(behindCards, material, space); front = new BattleEffectMeshBatch(aboveCards, material, space);
            foreach (var pair in assets.Models) models.Add(pair.Key, new BattleEffectModelPose(pair.Value, transform, meshCache));
            return true;
        }
        catch (Exception failure) { Error = failure.Message; Release(); return false; }
    }

    public bool TryPlay(BattleEffectParameters parameters, IReadOnlyList<RectTransform> actualTargets, RectTransform actualCaster = null, int visualSeed = 12345, bool includeInactive = false, float startDelaySeconds = 0, BattleEffectHitSlice slice = null)
    {
        Clear();
        if (material == null || parameters == null || !BattleEffectRange.TryCalculate(space, actualTargets, out var next, corners, includeInactive))
        { Error = "特效未就绪，或实际目标列表不是1～5个有效卡位。"; return false; }
        if ((parameters.Kind == BattleEffectKind.LisaVioletArc || parameters.Kind == BattleEffectKind.KaeyaIcicle || parameters.Kind == BattleEffectKind.VentiStormPulse) && actualCaster == null)
        { Error = "该特效需要显式的施放者卡牌。"; return false; }
        if ((parameters.Kind == BattleEffectKind.KaeyaGlacialWaltz || parameters.Kind == BattleEffectKind.KaeyaIcicle) && actualTargets.Count != 1)
        { Error = "凯亚环绕状态绑定一个实际宿主，单次冰棱只接受一个真实目标。"; return false; }
        if (BattleEffectParameters.UsesHolderLayers(parameters.Kind) && actualTargets.Count != 1)
        { Error = "自身环绕特效需要一个实际状态持有者。"; return false; }
        if (float.IsNaN(startDelaySeconds) || float.IsInfinity(startDelaySeconds) || startDelaySeconds < 0)
        { Error = "特效起播等待须为有限非负秒数。"; return false; }
        BattleEffectPart[] required = BattleEffectParameters.IsExplosion(parameters.Kind)
            ? new[] { BattleEffectPart.Flash, BattleEffectPart.Fire, BattleEffectPart.FireCloud, BattleEffectPart.Ring, BattleEffectPart.Spark, BattleEffectPart.Smoke }
            : parameters.Kind == BattleEffectKind.DandelionWindOrbit ? new[] { BattleEffectPart.DandelionWind, BattleEffectPart.DandelionSeed, BattleEffectPart.DandelionGlow }
            : parameters.Kind == BattleEffectKind.VentiUpdraft || parameters.Kind == BattleEffectKind.VentiWindDomain ? new[] { BattleEffectPart.VentiSigil, BattleEffectPart.VentiUprush, BattleEffectPart.VentiLiftLine }
            : BattleEffectParameters.IsVenti(parameters.Kind) ? new[] { BattleEffectPart.VentiArc, BattleEffectPart.VentiGust, BattleEffectPart.VentiFeather }
            : parameters.Kind == BattleEffectKind.AmberArrowRain ? new[] { BattleEffectPart.Arrow, BattleEffectPart.Impact }
            : parameters.Kind == BattleEffectKind.AmberBurningArea ? new[] { BattleEffectPart.BurnFootprint, BattleEffectPart.Flame, BattleEffectPart.Spark, BattleEffectPart.Ring }
            : parameters.Kind == BattleEffectKind.KaeyaGlacialWaltz || parameters.Kind == BattleEffectKind.KaeyaIcicle
                ? new[] { BattleEffectPart.KaeyaIce, BattleEffectPart.KaeyaMist, BattleEffectPart.KaeyaGrain, BattleEffectPart.KaeyaImpact }
            : new[] { BattleEffectPart.LisaCast, BattleEffectPart.LisaStrike };
        foreach (var part in required)
            if (!models.ContainsKey(part)) { Error = "缺少特效模型绑定：" + part; return false; }
        ResetKaeya();
        targets.AddRange(actualTargets); caster = actualCaster; range = next; includeInactiveTargets = includeInactive;
        settings = parameters.Copy(); seed = visualSeed; Playing = true; stoppingTime = -1; Error = null;
        hitSlice = slice; contactGate = slice != null ? new BattleEffectContactGate(BattleEffectTiming.Contact(settings)) : null;
        try
        {
            ConfigureEffectLayers();
            SetParameters(settings); Seek(0); startDelay = startDelaySeconds;
            if (startDelay > 0) ClearGeometry();
            return Error == null;
        }
        catch (Exception failure) { Error = failure.Message; Clear(); return false; }
    }
    public void SetParameters(BattleEffectParameters parameters)
    {
        if (settings != null && parameters.Kind != settings.Kind) throw new InvalidOperationException("不能在同一播放请求中更换模型类型。");
        settings = parameters.Copy(); Duration = PlaybackDuration(settings, targets.Count, hitSlice != null);
    }
    public static float PlaybackDuration(BattleEffectParameters parameters, int targetCount, bool sliced)
    {
        float duration = parameters["DurationSeconds"];
        if (parameters.Kind == BattleEffectKind.AmberArrowRain)
            duration = sliced ? parameters["FlightSeconds"] + .22f
                : Mathf.Max(duration, (Mathf.RoundToInt(parameters["ArrowsPerTarget"]) * targetCount - 1) * parameters["LaunchStaggerSeconds"] + parameters["FlightSeconds"] + .3f);
        if (parameters.Kind == BattleEffectKind.LisaVioletArc)
            duration = Mathf.Max(duration, parameters["StrikeDelaySeconds"] + parameters["StrikeSeconds"]);
        if (parameters.Kind == BattleEffectKind.KaeyaIcicle)
            duration = Mathf.Max(duration, parameters["FlightSeconds"] + parameters["ImpactSeconds"]);
        return duration;
    }
    private void Update() { if (automaticClock && !Paused) Advance(BattlePause.DeltaTime); }
    public void Advance(float seconds)
    {
        if (!Playing || Paused || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        if(tablePlayback!=null){RunTable(1,seconds);return;}
        if (startDelay > 0)
        {
            float waiting = Mathf.Min(startDelay, seconds); startDelay -= waiting; seconds -= waiting;
            if (seconds <= 0) return;
        }
        if (stoppingTime >= 0) stoppingTime += seconds;
        float next = Elapsed + seconds;
        if (contactGate != null) next = contactGate.Limit(next);
        Seek(next);
    }
    public void ConfirmContact()
    {
        if(tablePlayback!=null){RunTable(2);return;}
        if (contactGate == null || !contactGate.Waiting || settings == null) return;
        contactGate.Confirm(); Seek(contactGate.Contact);
    }
    public void Seek(float seconds)
    {
        if(tablePlayback!=null)
        {if(float.IsNaN(seconds)||float.IsInfinity(seconds))return;RunTable(3,seconds);return;}
        if (settings == null || material == null || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        startDelay = 0;
        if (!BattleEffectRange.TryCalculate(space, targets, out range, corners, includeInactiveTargets)) { Clear(); return; }
        try { RenderFrame(seconds); }
        catch (Exception failure) { Error = "特效播放失败：" + failure.Message; Clear(); }
    }
    private void RenderFrame(float seconds)
    {
        Elapsed = Mathf.Max(0, seconds);
        if ((!BattleEffectParameters.IsSustained(settings.Kind) && Elapsed >= Duration) ||
            (stoppingTime >= 0 && stoppingTime >= settings["FadeOutSeconds"])) { ClearGeometry(); Playing = false; return; }
        Playing = true;
        fade = settings["Opacity"] * (settings["FadeInSeconds"] > 0 ? Mathf.Clamp01(Elapsed / settings["FadeInSeconds"]) : 1);
        if (stoppingTime >= 0) fade *= 1 - Mathf.Clamp01(stoppingTime / Mathf.Max(.0001f, settings["FadeOutSeconds"]));
        tint = new Color(settings["ColorRed"], settings["ColorGreen"], settings["ColorBlue"], 1);
        material.SetFloat("_EffectTime", Elapsed); material.SetFloat("_Intensity", settings["Intensity"]);
        back.Begin(); front.Begin();
        switch (settings.Kind)
        {
            case BattleEffectKind.AmberArrowRain: RenderArrows(); break;
            case BattleEffectKind.AmberBurningArea: RenderFire(); break;
            case BattleEffectKind.LisaVioletArc: RenderLightning(); break;
            case BattleEffectKind.KaeyaGlacialWaltz: RenderKaeyaOrbit(); break;
            case BattleEffectKind.KaeyaIcicle: RenderKaeyaIcicle(); break;
            case BattleEffectKind.VentiStormEye: RenderVentiEye(); break;
            case BattleEffectKind.VentiStormPulse: RenderVentiPulse(); break;
            case BattleEffectKind.VentiUpdraft: RenderVentiUpdraft(); break;
            case BattleEffectKind.VentiWindDomain: RenderVentiWindDomain(); break;
            case BattleEffectKind.DandelionWindOrbit: RenderDandelionWindOrbit(); break;
            default: RenderExplosion(); break;
        }
        back.End(); front.End();
    }
    public void Stop(bool immediate = false)
    { if(tablePlayback!=null){tablePlayback.Stop(immediate);TableState();return;} startDelay = 0; contactGate = null; if (immediate || settings == null || settings["FadeOutSeconds"] <= 0) Clear(); else if (Playing) stoppingTime = 0; }
    public void Clear()
    { tablePlayback?.Clear(); Playing = false; Elapsed = 0; stoppingTime = -1; startDelay = 0; hitSlice = null; contactGate = null; ventiAbsorbedColor = null; ventiPhase = 0; targets.Clear(); caster = null; ClearGeometry(); }
    public void SetTargets(IReadOnlyList<RectTransform> currentTargets)
    {
        if(tablePlayback!=null){tablePlayback.SetTargets(currentTargets);TableState();return;}
        if (currentTargets == null || currentTargets.Count == 0) { Stop(); return; }
        if (settings != null && BattleEffectParameters.UsesHolderLayers(settings.Kind) && currentTargets.Count != 1)
            throw new ArgumentException("自身环绕特效需要一个实际宿主。");
        if (!BattleEffectRange.TryCalculate(space, currentTargets, out var next, corners, includeInactiveTargets))
            throw new ArgumentException("持续特效的目标卡位已失效。");
        targets.Clear(); targets.AddRange(currentTargets); range = next;
        ConfigureEffectLayers();
    }
    private void ClearGeometry() { back?.Begin(); front?.Begin(); back?.End(); front?.End(); }
    private float Hash(int index, int channel = 0)
    {
        uint value = unchecked((uint)seed + (uint)index * 747796405u + (uint)channel * 2891336453u);
        value = (value ^ (value >> 16)) * 2246822519u; value ^= value >> 13;
        return (value & 0xFFFFFFu) / 16777216f;
    }
    private BattleEffectModelPose Model(BattleEffectPart part)
    { if (!models.TryGetValue(part, out var model)) throw new InvalidOperationException("特效模型未绑定：" + part); return model; }
    private Vector2 Half
    {
        get
        {
            Vector2 half = range.HalfSize / range.PixelsPerUnit;
            // The same footprint as the Amber source: 58% of full bounds plus margin.
            if (settings.Kind == BattleEffectKind.AmberArrowRain || settings.Kind == BattleEffectKind.AmberBurningArea)
                half = half * 1.16f + Vector2.one * .15f;
            return new Vector2(Mathf.Max(.75f, half.x) * settings["RangeScale"] * settings["WidthScale"],
                Mathf.Max(.9f, half.y) * settings["RangeScale"] * settings["HeightScale"]);
        }
    }
    private void Draw(BattleEffectPart part, Vector3 position, Quaternion rotation, Vector3 scale, bool rear, float alpha = 1, float noise = 0, Vector2? origin = null)
    {
        if (alpha <= .0001f || scale.sqrMagnitude <= .0000001f) return;
        Model(part).Draw(rear ? back : front, Matrix4x4.TRS(position, rotation, scale), origin ?? range.Center,
            range.PixelsPerUnit, tint, fade * alpha, noise, settings["Perspective"]);
    }
    private static float Pulse(float t, float start, float end)
    { return t <= start || t >= end ? 0 : Mathf.Sin((t - start) / (end - start) * Mathf.PI); }
    private void RenderExplosion()
    {
        float t = Elapsed / Duration;
        float strength = settings.Kind == BattleEffectKind.ExplosionLight ? .75f : settings.Kind == BattleEffectKind.ExplosionLarge ? 1.25f : 1;
        Vector2 half = Half * strength;
        float flash = Pulse(t, 0, .16f);
        Draw(BattleEffectPart.Flash, new Vector3(0, 0, -1), Quaternion.identity, Vector3.one * flash * .65f * strength, false, flash);
        float wave = Mathf.Clamp01(t / .58f), waveAlpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.24f, .72f, t));
        Draw(BattleEffectPart.Ring, new Vector3(0, -.05f * half.y, 2), Quaternion.Euler(80, 0, -8),
            new Vector3(half.x * wave, .1f, half.y * wave) * settings["RingSize"], true, waveAlpha);
        int count = Mathf.Clamp(Mathf.CeilToInt(3 * range.Count * settings["ParticleDensity"] * strength), 3, 36);
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.39996323f + Hash(i, 1) * .24f;
            float life = Pulse(t, Hash(i) * .06f, .72f + Hash(i, 2) * .1f);
            float travel = .08f + .78f * Mathf.Clamp01(t / .65f);
            Vector3 direction = new Vector3(Mathf.Cos(a), Mathf.Sin(a), .12f).normalized;
            bool rear = i % 3 != 1;
            Vector3 p = new Vector3(Mathf.Cos(a) * half.x * travel, Mathf.Sin(a) * half.y * travel, rear ? 1.4f : -1.4f);
            float size = settings["FireSize"] * strength * life * (.7f + Hash(i, 3) * .4f);
            Draw(BattleEffectPart.Fire, p, Quaternion.FromToRotation(Model(BattleEffectPart.Fire).UpAxis, direction), Vector3.one * size, rear, life, .95f);
        }
        for (int i = 0; i < 3; i++)
        {
            float life = Pulse(t, .03f + i * .015f, .78f);
            Vector3 p = new Vector3((i - 1) * half.x * .36f, -half.y * (.35f + .42f * Mathf.Clamp01(t / .55f)), i == 1 ? -.5f : -1.5f);
            Draw(BattleEffectPart.FireCloud, p, Quaternion.Euler(i * 27, i * 41, i * 23),
                new Vector3(.7f, .65f, .5f) * life * settings["LowerFill"] * strength, false, life, .95f);
        }
        for (int i = 0; i < 4; i++)
        {
            float life = Pulse(t, .04f, .65f);
            float a = i * 2.39996f;
            Draw(BattleEffectPart.FireCloud, new Vector3(Mathf.Cos(a) * half.x * .18f, Mathf.Sin(a) * half.y * .18f, i % 2 == 0 ? 1.2f : -.7f),
                Quaternion.Euler(i * 41, i * 63, i * 37), Vector3.one * life * .55f * strength, i % 2 == 0, life, .95f);
        }
        int sparks = Mathf.Clamp(Mathf.CeilToInt(5 * range.Count * settings["ParticleDensity"]), 5, 60);
        for (int i = 0; i < sparks; i++)
        {
            float a = (i + .35f) * 2.39996f;
            float life = Pulse(t, Hash(i, 4) * .06f, .86f);
            float travel = 1 - Mathf.Pow(1 - Mathf.Clamp01(t / .8f), 2);
            Vector3 direction = new Vector3(Mathf.Cos(a), Mathf.Sin(a), .08f);
            Draw(BattleEffectPart.Spark, new Vector3(direction.x * half.x * travel * 1.2f,
                direction.y * half.y * travel * 1.2f - half.y * .15f * t * t, -1.8f),
                Quaternion.FromToRotation(Model(BattleEffectPart.Spark).UpAxis, direction), new Vector3(.2f, .18f, .2f) * life * settings["SparkSize"], false, life);
        }
        for (int i = 0; i < 4; i++)
        {
            float life = Pulse(t, .18f, 1);
            Draw(BattleEffectPart.Smoke, new Vector3((Hash(i, 5) - .5f) * half.x, (.2f + t * .45f) * half.y, 2),
                Quaternion.Euler(i * 32, i * 57, i * 19), Vector3.one * life * .55f, true, life * settings["SmokeOpacity"], .9f);
        }
    }
    private Vector2 Disk(int index)
    { float a = index * 2.39996323f + Hash(index, 2) * .25f, r = Mathf.Sqrt(Hash(index, 3)) * .92f; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
    private void RenderArrows()
    {
        int count = Mathf.RoundToInt(settings["ArrowsPerTarget"]) * range.Count;
        if (hitSlice != null) count = hitSlice.DecorativeCount(count);
        Vector2 half = Half; var arrow = Model(BattleEffectPart.Arrow);
        float angle = settings["PitchDegrees"] * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(0, Mathf.Sin(angle), Mathf.Cos(angle));
        Quaternion rotation = Quaternion.FromToRotation(arrow.ArrowAxis, direction);
        for (int i = 0; i < count; i++)
        {
            float launch = i * settings["LaunchStaggerSeconds"];
            if (hitSlice != null) launch = Mathf.Min(launch, count <= 1 ? 0 : i * settings["FlightSeconds"] * .5f / (count - 1));
            float age = Elapsed - launch;
            float flight = settings["FlightSeconds"] - (hitSlice != null ? launch : 0);
            Vector2 disk = Disk(i); Vector3 landing = new Vector3(disk.x * half.x, disk.y * half.y, 0);
            if (age >= 0 && age < flight)
            {
                float distance = Mathf.Lerp(settings["TravelUnits"], arrow.ArrowTip * settings["ArrowSize"], age / flight);
                Draw(BattleEffectPart.Arrow, landing - direction * distance, rotation, Vector3.one * settings["ArrowSize"], false, 1, .1f);
            }
            float hit = (age - flight) / .22f;
            if (hit > 0 && hit < 1) Draw(BattleEffectPart.Impact, landing + Vector3.back * .2f,
                Quaternion.Euler(80, 0, 0), Vector3.one * Mathf.Sin(hit * Mathf.PI) * settings["ImpactSize"], false, 1 - hit, .8f);
        }
    }
    private void RenderFire()
    {
        Vector2 half = Half;
        Draw(BattleEffectPart.BurnFootprint, new Vector3(0, 0, 1.7f), Quaternion.Euler(80, 0, 0),
            new Vector3(half.x, 1, half.y), true, settings["BoundaryOpacity"]);
        Draw(BattleEffectPart.Ring, new Vector3(0, 0, 1.6f), Quaternion.Euler(80, 0, 0),
            new Vector3(half.x, 1, half.y), true, settings["BoundaryOpacity"] * .35f);
        int count = Mathf.RoundToInt(settings["FlamesPerTarget"]) * range.Count + 1;
        for (int i = 0; i < count; i++)
        {
            float time = Elapsed / settings["FlameCycleSeconds"] + i * .382f;
            int cycle = Mathf.FloorToInt(time); float age = time - cycle;
            Vector2 p = Disk(i + cycle * 71) * settings["ScatterInset"];
            float strength = Mathf.Pow(Mathf.Sin(age * Mathf.PI), .65f);
            Draw(BattleEffectPart.Flame, new Vector3(p.x * half.x, p.y * half.y, 1.5f),
                Quaternion.FromToRotation(Model(BattleEffectPart.Flame).UpAxis, Vector3.up),
                new Vector3(.8f, 1, .8f) * strength * settings["FlameSize"], true, strength, 1);
        }
        int embers = Mathf.RoundToInt(settings["EmberCount"]);
        for (int i = 0; i < embers; i++)
        {
            float age = Mathf.Repeat(Elapsed * .8f + i * .27f, 1);
            Vector2 p = Disk(i + 30);
            Draw(BattleEffectPart.Spark, new Vector3(p.x * half.x, p.y * half.y + age * .6f, 1.2f),
                Quaternion.FromToRotation(Model(BattleEffectPart.Spark).UpAxis, Vector3.up), Vector3.one * Mathf.Sin(age * Mathf.PI) * .1f, true);
        }
    }
    private void RenderLightning()
    {
        if (caster == null) return;
        var cast = Model(BattleEffectPart.LisaCast);
        if ((hitSlice == null || hitSlice.Index == 0) && Elapsed <= settings["CastSeconds"])
        {
            cast.Sample(Elapsed / settings["CastSeconds"]);
            Vector2 origin = space.InverseTransformPoint(caster.TransformPoint(caster.rect.center));
            float width = space.InverseTransformVector(caster.TransformVector(Vector3.right * caster.rect.width)).magnitude;
            float height = space.InverseTransformVector(caster.TransformVector(Vector3.up * caster.rect.height)).magnitude;
            origin += new Vector2(width * settings["CastOffsetX"], height * settings["CastOffsetY"]);
            Draw(BattleEffectPart.LisaCast, Vector3.zero, Quaternion.identity, Vector3.one, false, 1, 0, origin);
        }
        float strikeTime = Elapsed - settings["StrikeDelaySeconds"];
        if (strikeTime < 0 || strikeTime > settings["StrikeSeconds"]) return;
        var strike = Model(BattleEffectPart.LisaStrike); strike.Sample(strikeTime / settings["StrikeSeconds"]);
        foreach (var target in targets)
        {
            Vector2 origin = space.InverseTransformPoint(target.TransformPoint(target.rect.center));
            Draw(BattleEffectPart.LisaStrike, Vector3.zero, Quaternion.identity,
                new Vector3(settings["StrikeWidth"], settings["StrikeHeight"], 1), false, 1, 0, origin);
        }
    }
    private void OnDisable() { Release(); }
    private void OnDestroy() { Release(); }
    private void Release()
    {
        Clear(); tablePlayback?.Dispose();tablePlayback=null;tableResource=null; back?.Dispose(); front?.Dispose(); back = front = null;
        ReleaseEffectLayers();
        foreach (var model in models.Values) model.Dispose(); models.Clear(); meshCache.Clear();
        if (material != null) Destroy(material); material = null; space = null; settings = null;
    }
}
