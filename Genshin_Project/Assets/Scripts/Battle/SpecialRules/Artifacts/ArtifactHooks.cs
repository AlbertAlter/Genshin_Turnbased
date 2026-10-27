/// <summary>圣遗物特殊逻辑唯一注册清单；不保存战斗实例，不另设响应队列。</summary>
public static class ArtifactHooks
{
    internal static void RegisterSpecialRules()
    {
        HuskCountHandler.RegisterHooks();
        PaleFlameCountHandler.RegisterHooks();
        HeavenlyGiftArtifactHooks.RegisterConditions();
        SeaHuedClamArtifact.Register();
        EarlyArtifactHooks.Register();
        AdventurerArtifactHooks.Register();
        ArtifactMaxHPBonuses.Register();
        ScholarArtifactHooks.Register();
        ArtifactHitSnapshots.Register();
        ArtifactEligibilityHooks.Register();
        ArtifactCountEventHooks.Register();
        MoonMorningCountHandler.RegisterHooks();
        ArtifactConditionHooks.Register();
        SongOfDaysPastArtifact.Register();
        EchoesArtifactHooks.Register();
        ArtifactReactionHooks.Register();
        ArchaicPetraArtifactHooks.Register();
        ViridescentVenererArtifactHooks.Register();
    }

    internal static void RegisterCounts()
    {
        CountStatusSystem.Register(ThunderingFuryCountHandler.StatusID, new ThunderingFuryCountHandler());
        CountStatusSystem.Register(HuskCountHandler.StatusID, new HuskCountHandler());
        CountStatusSystem.Register(CrimsonWitchCountHandler.StatusID, new CrimsonWitchCountHandler());
        CountStatusSystem.Register(PaleFlameCountHandler.StatusID, new PaleFlameCountHandler());
        CountStatusSystem.Register(VermillionCountHandler.StatusID, new VermillionCountHandler());
        CountStatusSystem.Register(SeaHuedClamArtifact.BubbleID, new SeaHuedClamCountHandler());
        CountStatusSystem.Register(ParadiseCountHandler.StatusID, new ParadiseCountHandler());
        CountStatusSystem.Register(MarechausseeCountHandler.StatusID, new MarechausseeCountHandler());
        CountStatusSystem.Register(WhimsyCountHandler.StatusID, new WhimsyCountHandler());
        CountStatusSystem.Register(UnfinishedReverieCountHandler.StatusID, new UnfinishedReverieCountHandler());
        CountStatusSystem.Register(MoonMorningCountHandler.StatusID, new MoonMorningCountHandler());
        CountStatusSystem.Register(SongOfDaysPastArtifact.RecordID, new SongRecordCountHandler());
        CountStatusSystem.Register(SongOfDaysPastArtifact.WaveID, new SongWaveCountHandler());
    }
}
