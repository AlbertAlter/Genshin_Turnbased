using System;
using System.Collections.Generic;
using UnityEngine;

public static class CardAirborneFlipMotion
{
    public static float Lift(float progress, float height)
    {
        double p = Math.Max(0, Math.Min(1, progress));
        // A brief gust supplies the launch impulse; gravity takes over after the early apex.
        const double apex = .34;
        double t = p < apex ? p / apex : (p - apex) / (1 - apex);
        return (float)(p < apex ? t * (2 - t) : 1 - t * t) * height;
    }
    public static float Angle(float progress)
    {
        double p = Math.Max(0, Math.Min(1, progress));
        // Start turning with the launch, then lose angular speed through air resistance.
        return (float)(p * (2 - p) * 360);
    }
    public static bool BackVisible(float degrees) => Math.Cos(degrees * Math.PI / 180) < 0;
    public static float ProjectionScale(float depth, float cardHeight)
        => (float)(1 / Math.Max(.5, 1 - depth / Math.Max(1, cardHeight * (24 / 3.38))));
}

/// <summary>Public two-sided card lift/turnover. Uses the shared card handle and its factory-supplied back.</summary>
public sealed class UICardAirborneFlip : MonoBehaviour
{
    public bool AutomaticClock = true;
    public bool Paused;
    private sealed class Flight
    {
        public long Playback; public UICardView Card; public float Age, Duration, Height, Waiting;
        public bool Begun; public Action<Exception> Finish;
    }
    private readonly List<Flight> flights = new List<Flight>();
    public void Play(long playback, UICardView card, float seconds, float heightRatio, Action<Exception> completed)
    {
        if (!isActiveAndEnabled || card == null || !card.Ready || seconds <= 0) { completed?.Invoke(null); return; }
        FinishWhere(f => f.Card == card);
        flights.Add(new Flight { Playback = playback, Card = card, Duration = seconds,
            Height = card.Appearance.Root.rect.height * heightRatio, Finish = completed });
    }
    private void Update() { if (AutomaticClock) Advance(BattlePause.DeltaTime); }
    public void Advance(float seconds)
    {
        if (Paused || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        foreach (var f in flights.ToArray())
        {
            if (f.Card == null || f.Card.Released || !f.Card.isActiveAndEnabled) { Finish(f); continue; }
            if (f.Begun && !f.Card.IsTurnoverOwnedBy(f)) { Finish(f); continue; }
            if (!f.Begun)
            {
                f.Waiting += seconds;
                if (!f.Card.BeginTurnover(f))
                {
                    if (f.Waiting > 2) { Debug.LogWarning("卡牌正反面未就绪，本次击飞翻面未播放。", this); Finish(f); }
                    continue;
                }
                f.Begun = true;
            }
            f.Age += seconds;
            if (f.Age >= f.Duration) { Finish(f); continue; }
            float t = f.Age / f.Duration;
            f.Card.SampleTurnover(f, CardAirborneFlipMotion.Lift(t, f.Height), CardAirborneFlipMotion.Angle(t));
        }
    }
    private void Finish(Flight f)
    {
        if (!flights.Remove(f)) return;
        if (f.Card != null) f.Card.EndTurnover(f);
        var done = f.Finish; f.Finish = null; done?.Invoke(null);
    }
    private void FinishWhere(Predicate<Flight> match) { foreach (var f in flights.ToArray()) if (match(f)) Finish(f); }
    public void Cancel(long playback) => FinishWhere(f => playback < 0 || f.Playback == playback);
    private void OnDisable() => Cancel(-1);
}
