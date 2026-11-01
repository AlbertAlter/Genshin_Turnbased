/*******************************************************************************
The content of this file includes portions of the proprietary AUDIOKINETIC Wwise
Technology released in source code form as part of the game integration package.
The content of this file may not be used without valid licenses to the
AUDIOKINETIC Wwise Technology.
Note that the use of the game engine is subject to the Unity(R) Terms of
Service at https://unity3d.com/legal/terms-of-service
 
License Usage
 
Licensees holding valid licenses to the AUDIOKINETIC Wwise Technology may use
this file in accordance with the end user license agreement provided with the
software or, alternatively, in accordance with the terms contained
in a written agreement between you and Audiokinetic Inc.
Copyright (c) 2026 Audiokinetic Inc.
*******************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
using AK.Wwise.Unity.WwiseAddressables;
#endif

using AK.Wwise.Unity.Logging;

#if UNITY_WEBGL
using System.Collections;
#else
using System.Threading.Tasks;
#endif

namespace Wwise.API.Runtime.WwiseTypes.WwiseObjectsManagers
{
    public class WwiseEventReferencesManager
    {
        private ConcurrentDictionary<string, WwiseEventReference> m_wwiseEventReferences =
            new ConcurrentDictionary<string, WwiseEventReference>();
        
        private static WwiseEventReferencesManager instance;
        public static WwiseEventReferencesManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new WwiseEventReferencesManager();
                }
                return instance;
            }
            private set { Instance = value; }
        }

#if !UNITY_WEBGL
        // Track the current language change task to wait for it before starting a new one
        private static Task m_currentLanguageChangeTask = Task.CompletedTask;
#endif

        public void AddReference(WwiseEventReference eventReference)
        {
            m_wwiseEventReferences.TryAdd(eventReference.DisplayName, eventReference);
        }

        public void RemoveReference(WwiseEventReference eventReference)
        {
            m_wwiseEventReferences.TryRemove(eventReference.DisplayName, out eventReference);
        }

        private List<WwiseEventReference> CollectEventReferencesForLanguageChange(List<string> eventNames)
        {
            var eventReferences = new List<WwiseEventReference>();
            
            foreach (var eventReference in m_wwiseEventReferences.Values)
            {
                if (eventNames == null || (eventNames.Count > 0 && eventNames.Contains(eventReference.DisplayName)))
                {
                    eventReferences.Add(eventReference);
                }
            }
            
            return eventReferences;
        }

        private void PerformLanguageChange(string language)
        {
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
#if WWISE_ADDRESSABLES_24_1_OR_LATER
            AkAddressableBankManager.Instance.SetLanguageAndReloadLocalizedBanks(language, false);
#else
            AkAddressableBankManager.Instance.SetLanguageAndReloadLocalizedBanks(language);
#endif
#else
            AkBankManager.DoUnloadBanks();
            AkUnitySoundEngine.SetCurrentLanguage(language);
            AkUnitySoundEngine.RenderAudio();
#endif
        }

        private void ReloadEventReferences(List<WwiseEventReference> eventReferences)
        {
            foreach (var eventReference in eventReferences)
            {
                eventReference.LoadAutoBank();
            }
        }

#if UNITY_WEBGL
        public IEnumerator SetLanguageAndReloadLocalizedBanks(string language, List<string> eventNames = null)
        {
            var eventReferences = CollectEventReferencesForLanguageChange(eventNames);
            
            foreach (var eventReference in eventReferences)
            {
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
                yield return eventReference.UnloadAutoBankAsync();
#else
                eventReference.UnloadAutoBank();
#endif
            }
            
            PerformLanguageChange(language);
            ReloadEventReferences(eventReferences);

            WwiseLogger.Verbose($"SetLanguageAndReloadLocalizedBanks: Language change initiated for {language}");

            // Wait for banks to load
            yield return WaitForBanksToLoadCoroutine(eventReferences);
        }

        private IEnumerator WaitForBanksToLoadCoroutine(List<WwiseEventReference> eventReferences)
        {
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
            foreach (var eventReference in eventReferences)
            {
                if (eventReference.AutoBank != null)
                {
                    yield return eventReference.CompleteLoadBank();
                }
            }
#endif
            yield break;
        }

#else
        public async Task SetLanguageAndReloadLocalizedBanks(string language, List<string> eventNames = null)
        {
            // Wait for the previous language change to complete

            try
            {
                await m_currentLanguageChangeTask;
            }
            catch (Exception ex)
            {
                WwiseLogger.Warning($"SetLanguageAndReloadLocalizedBanks: Previous language change operation failed: {ex.Message}. Proceeding with new language change request.");
            }
            var tcs = new TaskCompletionSource<bool>();
            m_currentLanguageChangeTask = tcs.Task;
            
            try
            {
                var eventReferences = CollectEventReferencesForLanguageChange(eventNames);
                
                foreach (var eventReference in eventReferences)
                {
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
                    await eventReference.UnloadAutoBankAsync();
#else
                    eventReference.UnloadAutoBank();
#endif
                }
                
                PerformLanguageChange(language);
                ReloadEventReferences(eventReferences);
                
                //Fire and forget WaitForBanksToLoadAsync to avoid blocking the SetLanguageAndReloadLocalizedBanks caller.
                //WaitForBanksToLoadAsync is still necessary to ensure all banks loads from this operation are completed before the next SetLanguageAndReloadLocalizedBanks.
                _ = WaitForBanksToLoadAsync(eventReferences, tcs);
            }
            catch (Exception ex)
            {
                WwiseLogger.Error($"SetLanguageAndReloadLocalizedBanks: Error during language change to {language}: {ex}");
                tcs.SetException(ex);
                throw;
            }
        }

        private async Task WaitForBanksToLoadAsync(List<WwiseEventReference> eventReferences, TaskCompletionSource<bool> tcs)
        {
            try
            {
                var loadTasks = new List<Task>();
#if AK_WWISE_ADDRESSABLES && UNITY_ADDRESSABLES
                foreach (var eventReference in eventReferences)
                {
                    if (eventReference.AutoBank != null)
                    {
                        loadTasks.Add(eventReference.CompleteLoadBank());
                    }
                }
#endif
                
                if (loadTasks.Count > 0)
                {
                    await Task.WhenAll(loadTasks);
                }
                
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }
#endif
    }
}