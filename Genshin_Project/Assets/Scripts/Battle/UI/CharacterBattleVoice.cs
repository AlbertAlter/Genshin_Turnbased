using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public sealed class CharacterBattleVoice : BattleSkillTrackBehaviour, ICharacterVoiceSequenceView
{
    public BattleManager battle;
    [Range(0, 1)] public float voiceVolume = 1;
    public AudioMixerGroup output;
    private CharacterVoiceTable table;
    private readonly System.Random soundRandom = new System.Random(); // Cosmetic only.
    private readonly Dictionary<int, AudioSource> speakers = new Dictionary<int, AudioSource>();
    private readonly Dictionary<int, long> owners = new Dictionary<int, long>();
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
    private AudioSource idleSource, victorySource;
    private Action<Exception> victoryDone;
    private readonly CharacterVoiceIdleTimer idleTimer = new CharacterVoiceIdleTimer();
    private float appliedVolume = -1;
    private Vector3 previousMouse;
    private bool inputWasHeld;

    protected override void OnEnable()
    {
        base.OnEnable();
        try { table = CharacterVoiceTable.Read(Path.Combine(Application.streamingAssetsPath, "Data", CharacterVoiceTable.FileName)); }
        catch (Exception e) { LogManager.LogError(LogCategory.Voice, "语音表加载失败：" + e.Message); }
        previousMouse = Input.mousePosition;
        idleTimer.Reset();
    }

    public void PrepareVoices(IReadOnlyList<BattlePresentationCue> cues)
    {
        if (table == null) return;
        var living = new List<int>();
        if (battle != null)
            foreach (var ally in battle.Allies)
                if (ally?.Entity != null && ally.Entity.IsAlive) living.Add(ally.Entity.EntityID);
        CharacterVoicePlan.Prepare(cues, table, living, soundRandom.Next);
    }

    public override void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        StopIdle();
        string path = cue.CharacterVoicePath;
        var source = Play(path, playbackID, cue.CharacterVoiceKey);
        // Scene transition is allowed after the winning line finishes (or skip/cancel releases it).
        if (source != null && cue.Result.Data is BattleEndedEventData)
        {
            victorySource = source; victoryDone = completed;
        }
        else completed(null);
    }

    private AudioSource Play(string path, long playbackID, string key)
    {
        if (path == null) return null;
        if (voiceVolume <= 0)
        {
            LogManager.Log(LogCategory.Voice, $"跳过 {key} {path}，语音音量为 0。");
            return null;
        }
        try
        {
            if (!clips.TryGetValue(path, out var clip))
            {
                clip = ReadClip(Path.Combine(Application.streamingAssetsPath, CharacterVoiceTable.AudioFolder, path));
                clips.Add(path, clip);
            }
            int actor = int.Parse(path.Substring(0, path.IndexOf('/')));
            if (!speakers.TryGetValue(actor, out var source))
            {
                var channel = new GameObject("Voice_" + actor);
                channel.transform.SetParent(transform, false);
                source = channel.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; speakers.Add(actor, source);
            }
            source.outputAudioMixerGroup = output;
            source.volume = Mathf.Clamp01(voiceVolume);
            source.clip = clip; owners[actor] = playbackID; source.Play();
            if (LogManager.IsEnabled(LogCategory.Voice))
            {
                string message = $"{key} → {path}，播放={source.isPlaying}，语音音量={source.volume:F2}，" +
                    $"Unity 总音量={AudioListener.volume:F2}，暂停={AudioListener.pause}";
#if UNITY_EDITOR
                message += $"，Game 视图静音={UnityEditor.EditorUtility.audioMasterMute}";
#endif
                LogManager.Log(LogCategory.Voice, message);
            }
            return source;
        }
        catch (Exception e) { LogManager.LogError(LogCategory.Voice, $"{key} {path} 播放失败：{e.Message}"); return null; }
    }

    // Published desktop WAVs are PCM16. Decode once on first use, so a late async load cannot speak after its hit or cancellation.
    private static AudioClip ReadClip(string path)
    {
        using var file = File.OpenRead(path);
        using var input = new BinaryReader(file, Encoding.ASCII);
        string Tag() => Encoding.ASCII.GetString(input.ReadBytes(4));
        if (Tag() != "RIFF") throw new InvalidDataException("不是 RIFF WAV");
        input.ReadUInt32();
        if (Tag() != "WAVE") throw new InvalidDataException("缺少 WAVE 标记");
        int channels = 0, rate = 0; byte[] data = null;
        while (file.Position + 8 <= file.Length)
        {
            string tag = Tag(); uint size = input.ReadUInt32(); long end = file.Position + size;
            if (end > file.Length) throw new InvalidDataException("WAV 数据不完整");
            if (tag == "fmt ")
            {
                if (size < 16 || input.ReadUInt16() != 1) throw new InvalidDataException("需要 PCM16 WAV");
                channels = input.ReadUInt16(); rate = input.ReadInt32();
                input.ReadUInt32(); input.ReadUInt16();
                if (input.ReadUInt16() != 16) throw new InvalidDataException("需要 16 位 WAV");
            }
            else if (tag == "data") data = input.ReadBytes(checked((int)size));
            file.Position = end + (size & 1);
        }
        if (channels < 1 || channels > 2 || rate <= 0 || data == null || data.Length == 0 || data.Length % (2 * channels) != 0)
            throw new InvalidDataException("无效 WAV 声道或样本");
        var samples = new float[data.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = (short)(data[2*i] | data[2*i+1] << 8) / 32768f;
        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples.Length / channels, channels, rate, false);
        if (clip.SetData(samples, 0)) return clip;
        Destroy(clip); throw new InvalidDataException("Unity 无法写入音频采样");
    }

    public void NotifyPlayerInput() { StopIdle(); idleTimer.RestartWait(); }
    private void StopIdle() { if (idleSource != null) idleSource.Stop(); idleSource = null; }

    private void Update()
    {
        bool held = Input.anyKey || Input.touchCount > 0 || Input.GetAxisRaw("Horizontal") != 0 || Input.GetAxisRaw("Vertical") != 0;
        Vector3 mouse = Input.mousePosition;
        if (held || Input.anyKeyDown || inputWasHeld || mouse != previousMouse || Input.mouseScrollDelta != Vector2.zero) NotifyPlayerInput();
        previousMouse = mouse; inputWasHeld = held;
        if (appliedVolume != voiceVolume)
        {
            appliedVolume = voiceVolume;
            foreach (var s in speakers.Values) s.volume = Mathf.Clamp01(voiceVolume);
        }
        if (victoryDone != null && (victorySource == null || !victorySource.isPlaying)) FinishVictory();
        int turn = battle != null && battle.IsAwaitingAllyInput ? battle.TurnCount : 0;
        bool canCount = table != null && Application.isFocused && player != null && player.VoicesEnabled
            && !BattleExecution.IsBusy && Time.timeScale > 0;
        if (turn == 0 || !canCount) StopIdle();
        if (!idleTimer.Advance(turn, canCount, Time.unscaledTimeAsDouble, table?.IdleWaitSeconds ?? 0)) return;
        var active = battle.GetActiveAlly()?.Entity;
        if (active == null || !active.IsOnField) return;
        foreach (var s in speakers.Values) if (s.isPlaying) return;
        idleTimer.MarkUsed();
        var idle = table.Action(active.EntityID, CharacterVoiceAction.Idle);
        if (idle.Count == 0) LogManager.Log(LogCategory.Voice, $"跳过 {active.EntityID} Idle，没有可用的 Soundtrack。");
        idleSource = Play(CharacterVoicePlan.Pick(active.EntityID, idle, soundRandom.Next), 0, nameof(CharacterVoiceAction.Idle));
    }

    private void FinishVictory()
    {
        var done = victoryDone; victoryDone = null; victorySource = null; done?.Invoke(null);
    }
    public override void Cancel(long playbackID)
    {
        foreach (var pair in speakers)
            if (playbackID < 0 || owners.TryGetValue(pair.Key, out long id) && id == playbackID)
            {
                pair.Value.Stop();
                if (pair.Value == idleSource) idleSource = null;
            }
        if (victoryDone != null && (victorySource == null || !victorySource.isPlaying)) FinishVictory();
        if (playbackID < 0) idleTimer.Reset();
    }
    protected override void OnDisable()
    {
        base.OnDisable();
        foreach (var source in speakers.Values) Destroy(source.gameObject);
        foreach (var clip in clips.Values) Destroy(clip);
        speakers.Clear(); owners.Clear(); clips.Clear(); table = null;
    }
}

// Turn 0 is outside the player-input window. Suspended frames never accrue idle time.
internal sealed class CharacterVoiceIdleTimer
{
    private int turn;
    private double elapsed;
    private double? previous;
    private bool used;

    public void Reset() { turn = 0; RestartWait(); }
    public void RestartWait() { previous = null; elapsed = 0; used = false; }
    public void MarkUsed() { used = true; }

    public bool Advance(int inputTurn, bool canCount, double now, double delay)
    {
        if (turn != inputTurn) { turn = inputTurn; RestartWait(); }
        if (turn == 0 || !canCount) { previous = null; return false; }
        if (previous.HasValue) elapsed += Math.Max(0, now - previous.Value);
        previous = now;
        return !used && elapsed > 0 && elapsed >= delay;
    }
}
