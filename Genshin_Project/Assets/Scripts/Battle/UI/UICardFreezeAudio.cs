using UnityEngine;

/// <summary>Optional Wwise audio for the shared three-click thaw interaction.</summary>
[DisallowMultipleComponent]
public sealed class UICardFreezeAudio : MonoBehaviour
{
    public AkGameObj emitter;
    public string soundBank = "UI_Freeze";
    public string firstClickEvent = "Play_Freeze_Dull";
    public string secondClickEvent = "Play_Freeze_Crack";
    public string thawEvent = "Play_Freeze_Shatter";

    private readonly uint[] playing = new uint[3];
    private string loadedBank;
    private bool reportedUnavailable;
    public FreezeAudioSettings Settings { get; private set; }
    public string LastError { get; private set; }
    private const string VolumeParameter = "Freeze_VolumeDb";
    // Bound to the per-voice Pitch Shifter effect, never the Sound object's varispeed Pitch.
    // Keep the table's semitone values; the effect RTPC converts them to cents without changing source speed.
    private const string PitchParameter = "Freeze_PitchSemitones";

    private void Start() { ReloadSettings(); Prepare(); }

    public bool ReloadSettings()
    {
        try
        {
            Settings = FreezeAudioSettings.Load(); LastError = null;
            for (int i = 1; i <= 3; ++i) ApplyToPlayingVoice(i);
            return true;
        }
        catch (System.Exception ex)
        {
            Settings = null; LastError = "解冻音效表：" + ex.Message;
            Debug.LogError(LastError, this); return false;
        }
    }

    private bool Prepare()
    {
        if (!isActiveAndEnabled || !Application.isPlaying) return false;
        if (Settings == null) return false;
        if (emitter == null || !emitter.isActiveAndEnabled || !AkUnitySoundEngine.IsInitialized())
        {
            if (!reportedUnavailable)
            {
                Debug.LogWarning("Freeze audio requires an enabled Wwise emitter and initialized sound engine.", this);
                LastError = "需要启用Wwise发声对象并完成音频引擎初始化。";
                reportedUnavailable = true;
            }
            return false;
        }
        if (loadedBank == null && !string.IsNullOrEmpty(soundBank))
        {
            // The integration owns bank reference counting, including other views using this bank.
            AkBankManager.LoadBank(soundBank, false, false);
            loadedBank = soundBank;
        }
        reportedUnavailable = false;
        return true;
    }

    public bool PlayClick(int completedClicks)
    {
        if (completedClicks < 1 || completedClicks > 3 || !Prepare()) return false;
        string eventName = completedClicks == 1 ? firstClickEvent :
            completedClicks == 2 ? secondClickEvent : thawEvent;
        if (string.IsNullOrEmpty(eventName)) { LastError = "未绑定对应的Wwise音效事件。"; return false; }
        int slot = completedClicks - 1;
        // Keep one voice per stage on this view; rapid clicks at different stages can overlap.
        if (playing[slot] != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
            AkUnitySoundEngine.StopPlayingID(playing[slot]);
        playing[slot] = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        if (Settings.VolumePercent[slot] == 0) { LastError = null; return true; }
        playing[slot] = AkUnitySoundEngine.PostEvent(eventName, emitter.gameObject);
        if (playing[slot] == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        { LastError = "Wwise未接受事件：" + eventName; Debug.LogError(LastError, this); return false; }
        bool applied = ApplyToPlayingVoice(completedClicks);
        return applied;
    }

    public bool ApplyToPlayingVoice(int completedClicks)
    {
        if (Settings == null || completedClicks < 1 || completedClicks > 3 || !AkUnitySoundEngine.IsInitialized()) return false;
        int index = completedClicks - 1;
        uint id = playing[index];
        if (id == AkUnitySoundEngine.AK_INVALID_PLAYING_ID) return true;
        if (Settings.VolumePercent[index] == 0)
        { AkUnitySoundEngine.StopPlayingID(id); playing[index] = 0; return true; }
        float db = Mathf.Max(-96, 20 * Mathf.Log10(Settings.VolumePercent[index] / 100));
        var volume = AkUnitySoundEngine.SetRTPCValueByPlayingID(VolumeParameter, db, id);
        var pitch = AkUnitySoundEngine.SetRTPCValueByPlayingID(PitchParameter, Settings.PitchSemitones[index], id);
        if (volume == AKRESULT.AK_PlayingIDNotFound || pitch == AKRESULT.AK_PlayingIDNotFound)
        { playing[index] = 0; LastError = null; return true; }
        if (volume != AKRESULT.AK_Success || pitch != AKRESULT.AK_Success)
        { LastError = $"解冻音效RTPC设置失败：音量={volume}，音高={pitch}"; return false; }
        LastError = null; return true;
    }

    private void OnDisable()
    {
        if (AkUnitySoundEngine.IsInitialized())
        {
            for (int i = 0; i < playing.Length; ++i)
                if (playing[i] != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                    AkUnitySoundEngine.StopPlayingID(playing[i]);
            if (loadedBank != null) AkBankManager.UnloadBank(loadedBank);
        }
        System.Array.Clear(playing, 0, playing.Length);
        loadedBank = null;
        reportedUnavailable = false;
    }
}
